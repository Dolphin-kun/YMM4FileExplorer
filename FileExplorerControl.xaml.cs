using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YMM4FileExplorer.Helpers;
using YMM4FileExplorer.Settings;

namespace YMM4FileExplorer
{
    [SuppressMessage("Design", "CA1001", Justification = "<保留中>")]
    public partial class FileExplorerControl : UserControl
    {
        public class FileItem
        {
            public string? Name { get; set; }
            public string? FullPath { get; set; }
            public string? Type { get; set; }
            public string? Size { get; set; }
            public long SizeInBytes { get; set; }
            public string? LastWriteString { get; set; }
            public DateTime LastWriteTime { get; set; }
            public ImageSource? Icon { get; set; }

            public bool IsDirectory { get; set; }
        }

        //watcher
        private FileSystemWatcher? _watcher;

        //D&D
        private Point _dragStartPoint;
        private string? _currentDirectory;

        //sort
        private GridViewColumnHeader? _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        //preview
        private readonly DispatcherTimer _timer;

        //Save
        private readonly string _initialPath;
        private bool _isInitialContentLoaded = false;
        public event Action<string>? PathChanged;

        // Navigation
        private readonly List<string> _navigationHistory = [];
        private int _currentHistoryIndex = -1;
        private bool _isNavigatingViaHistory = false;

        // Search
        private readonly DispatcherTimer _searchTimer;
        private CancellationTokenSource? _searchCts;

        public FileExplorerControl(string initialPath = "C:\\")
        {
            InitializeComponent();

            _initialPath = initialPath;

            this.Loaded += FileExplorerControl_Loaded;
            this.Unloaded += FileExplorerControl_Unloaded;

            PreviewPopup.Closed += PreviewPopup_Closed;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += Timer_Tick;

            _searchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchTimer.Tick += SearchTimer_Tick;
        }

        private async void FileExplorerControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                // エラー処理を追加
                Debug.WriteLine($"初期化中にエラーが発生しました: {ex.Message}");
                MessageBox.Show(
                    "初期化中にエラーが発生しました。\n" + ex.Message,
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async Task InitializeAsync()
        {
            if (_isInitialContentLoaded)
                return;

            _isInitialContentLoaded = true;

            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.Title = "YMM4 エクスプローラー";

                if (FileExplorerSettings.Default.IsTopmost)
                {
                    parentWindow.Topmost = true;

                    parentWindow.Deactivated += (s, args) =>
                    {
                        parentWindow.Topmost = false;
                    };

                    parentWindow.Activated += (s, args) =>
                    {
                        parentWindow.Topmost = true;
                    };
                }
            }

            if (FileExplorerSettings.Default.IsCheckVersion && await GetVersion.CheckVersionAsync("YMM4エクスプローラー"))
            {
                string url =
                    "https://ymm4-info.net/ymme/YMM4%E3%82%A8%E3%82%AF%E3%82%B9%E3%83%97%E3%83%AD%E3%83%BC%E3%83%A9%E3%83%BC%E3%83%97%E3%83%A9%E3%82%B0%E3%82%A4%E3%83%B3";
                var result = MessageBox.Show(
                    $"新しいバージョンがあります。\n\n最新バージョンを確認しますか？\nOKを押すと配布サイトが開きます。\n{url}",
                    "YMM4エクスプローラープラグイン",
                    MessageBoxButton.OKCancel);

                if (result == MessageBoxResult.OK)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                Debug.WriteLine("最新のバージョンです");
            }

            await LoadDrivesAsync();
            await NavigateToInitialPathAsync(_initialPath);
            AddHistory(_initialPath);
        }

        private async Task NavigateToInitialPathAsync(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                await SelectTreeViewItemByPathAsync(path);
            }
        }

        private void FileExplorerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _watcher?.Dispose();
        }

        private async Task LoadDrivesAsync()
        {
            DirectoryTree.Items.Clear();

            var drives = await Task.Run(() => DriveInfo.GetDrives().Where(d => d.IsReady).ToList());

            foreach (var drive in drives)
            {
                var item = new TreeViewItem
                {
                    Header = await CreateTreeViewItemHeaderAsync(
                        drive.Name,
                        drive.RootDirectory.FullName
                    ),
                    Tag = drive.RootDirectory.FullName
                };

                try
                {
                    bool hasSubDirectories = await Task.Run(
                        () => Directory.EnumerateDirectories(drive.RootDirectory.FullName).Any()
                    );

                    if (hasSubDirectories)
                    {
                        item.Items.Add(null);
                    }
                }
                catch { }

                item.Expanded += Folder_Expanded;
                DirectoryTree.Items.Add(item);
            }
        }

        private static async Task<StackPanel> CreateTreeViewItemHeaderAsync(
            string name,
            string fullPath
        )
        {
            var icon = await ShellIcon.GetSmallIconAsync(fullPath, true);

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Image
                    {
                        Source = icon,
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 4, 0),
                    },
                    new TextBlock { Text = name },
                },
            };
        }

        private async Task SelectTreeViewItemByPathAsync(string path)
        {
            var cleanPath = path.TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(cleanPath)) return;

            var pathParts = cleanPath.Split(Path.DirectorySeparatorChar).ToList();

            if (pathParts.Count > 0 && pathParts[0].Length == 2 && pathParts[0][1] == ':')
            {
                pathParts[0] += Path.DirectorySeparatorChar;
            }

            ItemsControl? parent = DirectoryTree;
            TreeViewItem? finalItem = null;

            foreach (var part in pathParts)
            {
                if (parent == null) break;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                TreeViewItem? currentItem = null;

                foreach (TreeViewItem item in parent.Items)
                {
                    var itemPath = (parent == DirectoryTree)
                        ? (string)item.Tag
                        : Path.GetFileName((string)item.Tag);

                    if (string.Equals(itemPath, part, StringComparison.OrdinalIgnoreCase))
                    {
                        currentItem = item;
                        break;
                    }
                }

                if (currentItem != null)
                {
                    foreach (TreeViewItem sibling in parent.Items)
                    {
                        if (sibling != currentItem)
                        {
                            sibling.IsExpanded = false;
                        }
                    }

                    currentItem.IsExpanded = true;
                    await ExpandNodeAsync(currentItem);
                    parent = currentItem;
                    finalItem = currentItem;
                }
                else
                {
                    Debug.WriteLine($"[階層復元エラー] パス '{part}' が見つかりません。探索中の親: '{(parent as TreeViewItem)?.Tag ?? "ルート"}'");
                    break;
                }
            }

            if (finalItem != null)
            {
                finalItem.IsSelected = true;
            }
        }

        private async void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is TreeViewItem item)
                {
                    await ExpandNodeAsync(item);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フォルダー展開中にエラーが発生しました: {ex.Message}");
            }
        }

        private async Task ExpandNodeAsync(TreeViewItem item)
        {
            if (item.Items.Count == 1 && item.Items[0] == null)
            {
                item.Items.Clear();
                try
                {
                    var parentDirInfo = new DirectoryInfo((string)item.Tag);
                    var directories = await Task.Run(
                        () =>
                            parentDirInfo
                                .GetDirectories()
                                .Where(dir =>
                                    FileExplorerSettings.Default.ShowHiddenFiles
                                    || !dir.Attributes.HasFlag(FileAttributes.Hidden)
                                )
                                .ToList()
                    );

                    foreach (var dir in directories)
                    {
                        var subItem = new TreeViewItem
                        {
                            Header = await CreateTreeViewItemHeaderAsync(dir.Name, dir.FullName),
                            Tag = dir.FullName
                        };

                        try
                        {
                            bool hasSubDirs = await Task.Run(
                                () => Directory.EnumerateDirectories(dir.FullName).Any()
                            );

                            if (hasSubDirs)
                            {
                                subItem.Items.Add(null);
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // アクセス不可能なファイルをスキップ
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"サブディレクトリの有無の確認に失敗: {dir.FullName}, Error: {ex.Message}");
                        }

                        subItem.Expanded += Folder_Expanded;
                        item.Items.Add(subItem);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.WriteLine($"アクセスが許可されていないフォルダ: {item.Tag}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"フォルダ展開中にエラー: {item.Tag}, Error: {ex.Message}");
                }
            }
        }


        private async void DirectoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isNavigatingViaHistory)
            {
                return;
            }

            try
            {
                _watcher?.Dispose();
                if (DirectoryTree.SelectedItem is TreeViewItem item)
                {
                    string? path = item.Tag as string;
                    if (Directory.Exists(path))
                    {
                        SearchTextBox.Text = string.Empty;

                        _currentDirectory = path;
                        await LoadFilesAsync(path);

                        _watcher = new FileSystemWatcher(path)
                        {
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                            EnableRaisingEvents = true,
                        };

                        _watcher.Created += OnFileSystemChanged;
                        _watcher.Deleted += OnFileSystemChanged;
                        _watcher.Renamed += OnFileSystemChanged;

                        PathChanged?.Invoke(path);

                        AddHistory(path);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"選択したディレクトリが存在しないか、アクセスできません。\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async Task LoadFilesAsync(string path)
        {
            var fileCollection = new ObservableCollection<FileItem>();
            FileList.ItemsSource = fileCollection;
            try
            {
                await Task.Run(async () =>
                {
                    //ディレクトリ
                    foreach (var dir in Directory.EnumerateDirectories(path))
                    {
                        var info = new DirectoryInfo(dir);

                        if (
                            !FileExplorerSettings.Default.ShowHiddenFiles
                            && info.Attributes.HasFlag(FileAttributes.Hidden)
                        )
                            continue;

                        var icon = await ShellIcon.GetSmallIconAsync(dir, true);
                        var fileItem = new FileItem
                        {
                            Name = info.Name,
                            FullPath = info.FullName,
                            Type = "フォルダー",
                            LastWriteString = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                            LastWriteTime = info.LastWriteTime,
                            Icon = icon,
                            IsDirectory = true,
                        };

                        await Dispatcher.InvokeAsync(() =>
                        {
                            fileCollection.Add(fileItem);
                        });
                    }

                    //ファイル
                    foreach (var file in Directory.EnumerateFiles(path))
                    {
                        var info = new FileInfo(file);

                        if (!FileExplorerSettings.Default.ShowHiddenFiles && info.Attributes.HasFlag(FileAttributes.Hidden))
                            continue;

                        var icon = await ShellIcon.GetSmallIconAsync(file, false);
                        var fileItem = new FileItem
                        {
                            Name = info.Name,
                            FullPath = info.FullName,
                            Type = info.Extension,
                            Size = $"{info.Length / 1024} KB",
                            SizeInBytes = info.Length,
                            LastWriteString = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                            LastWriteTime = info.LastWriteTime,
                            Icon = icon,
                            IsDirectory = false,
                        };

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            fileCollection.Add(fileItem);
                        });
                    }
                });

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ファイルの読み込みに失敗: {ex.Message}");
            }
        }

        private async void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_currentDirectory != null)
                    {
                        await LoadFilesAsync(_currentDirectory);
                    }
                });
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine(
                    $"ファイルシステムの変更を処理中にエラーが発生しました。: {ex.Message}"
                );
            }
        }

        private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);

            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null && source is not ListViewItem)
            {
                source = VisualTreeHelper.GetParent(source);
            }

            if (source is ListViewItem clickedItem && clickedItem.IsSelected && e.ClickCount == 1 &&
                !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl) &&
                !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
            }
            else if (source is not ListViewItem)
            {
                if (sender is ListView listView)
                {
                    listView.SelectedItem = null;
                }
            }
        }

        private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var selectedItems = FileList.SelectedItems;
                    if (selectedItems != null && selectedItems.Count > 0 && _currentDirectory != null)
                    {
                        var filePaths = new List<string>();
                        foreach (var item in selectedItems)
                        {
                            if (item is FileItem file && !string.IsNullOrEmpty(file.FullPath))
                            {
                                string fullPath = file.FullPath;
                                filePaths.Add(fullPath);
                            }
                        }

                        if (filePaths.Count > 0)
                        {
                            DataObject data = new(DataFormats.FileDrop, filePaths.ToArray());
                            DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy);
                        }
                    }
                }
            }
        }

        //Sort
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = sender as GridViewColumnHeader;
            if (header?.Tag == null) return;

            string sortBy = header.Tag.ToString()!;
            ListSortDirection direction;

            if (header != _lastHeaderClicked)
            {
                direction = ListSortDirection.Ascending;
            }
            else
            {
                direction = _lastDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }

            Sort(sortBy, direction);

            UpdateHeaderArrow(header, direction);

            _lastHeaderClicked = header;
            _lastDirection = direction;
        }

        private void Sort(string sortBy, ListSortDirection direction)
        {
            ICollectionView dataView = CollectionViewSource.GetDefaultView(FileList.ItemsSource);
            if (dataView is null)
                return;

            dataView.SortDescriptions.Clear();

            string actualSortProperty = sortBy == "Size" ? "SizeInBytes" : sortBy;

            dataView.SortDescriptions.Add(new SortDescription(actualSortProperty, direction));
            dataView.Refresh();
        }

        private void UpdateHeaderArrow(GridViewColumnHeader header, ListSortDirection direction)
        {
            string arrow = direction == ListSortDirection.Ascending ? "▲" : "▼";

            foreach (var column in ((GridView)FileList.View).Columns)
            {
                if (column.Header is GridViewColumnHeader ch &&
                    ch.Content is StackPanel panel &&
                    panel.Children.Count == 2 &&
                    panel.Children[1] is TextBlock arrowText)
                {
                    arrowText.Text = "";
                }
            }

            if (header.Content is StackPanel targetPanel &&
                targetPanel.Children.Count == 2 &&
                targetPanel.Children[1] is TextBlock targetArrow)
            {
                targetArrow.Text = arrow;
                targetArrow.Foreground = Brushes.Gray;
            }
        }

        #region プレビュー
        private async void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileList.SelectedItem is not FileItem selectedItem || string.IsNullOrEmpty(selectedItem.FullPath))
                return;

            if (selectedItem.IsDirectory)
            {
                await SelectTreeViewItemByPathAsync(selectedItem.FullPath);
            }
            else
            {
                _timer.Stop();
                if (PreviewContent.Content is MediaElement oldMedia)
                {
                    oldMedia.Close();
                }
                else if (PreviewContent.Content is Grid grid &&
                         grid.Children.Count > 1 &&
                         grid.Children[1] is MediaElement audioMedia)
                {
                    audioMedia.Close();
                }
                PreviewContent.Content = null;

                var fullPath = selectedItem.FullPath;
                var extension = Path.GetExtension(fullPath).ToLowerInvariant();

                try
                {
                    switch (extension)
                    {
                        case ".png":
                        case ".jpg":
                        case ".jpeg":
                        case ".bmp":
                        case ".gif":
                            var bitmap = await LoadImageAsync(fullPath);
                            var image = new Image
                            {
                                Source = bitmap,
                                Stretch = Stretch.Uniform
                            };
                            PreviewContent.Content = image;
                            break;

                        case ".mp4":
                        case ".wmv":
                        case ".avi":
                        case ".mov":
                            var media = new MediaElement
                            {
                                Source = new Uri(fullPath),
                                Volume = FileExplorerSettings.Default.PreviewVolumePercentage / 100d,
                                Stretch = Stretch.Uniform,
                                LoadedBehavior = MediaState.Manual,
                                UnloadedBehavior = MediaState.Manual
                            };
                            PreviewContent.Content = media;
                            media.Play();
                            break;

                        case ".mp3":
                        case ".wav":
                            var slider = new Slider { VerticalAlignment = VerticalAlignment.Center };
                            var audioMedia = new MediaElement
                            {
                                Source = new Uri(fullPath),
                                Volume = FileExplorerSettings.Default.PreviewVolumePercentage / 100d,
                                LoadedBehavior = MediaState.Manual,
                                UnloadedBehavior = MediaState.Manual
                            };

                            audioMedia.MediaOpened += (s, args) =>
                            {
                                if (audioMedia.NaturalDuration.HasTimeSpan)
                                {
                                    slider.Maximum = audioMedia.NaturalDuration.TimeSpan.TotalSeconds;
                                }
                            };

                            slider.PreviewMouseDown += Slider_PreviewMouseDown;
                            slider.PreviewMouseUp += Slider_PreviewMouseUp;

                            var grid = new Grid();
                            grid.Children.Add(slider);
                            grid.Children.Add(audioMedia);

                            PreviewContent.Content = grid;
                            audioMedia.Play();
                            _timer.Start();
                            break;

                        default:
                            return;
                    }

                    const double baseWidth = 300;
                    const double baseMaxHeight = 400;

                    double multiplier = FileExplorerSettings.Default.PreviewSizeMultiplier / 100d;

                    PreviewBorder.Width = baseWidth * multiplier;
                    PreviewBorder.MaxHeight = baseMaxHeight * multiplier;

                    PreviewPopup.IsOpen = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"プレビューの読み込みに失敗: {ex.Message}");
                }
            }


        }

        private static async Task<BitmapImage> LoadImageAsync(string fullPath)
        {
            return await Task.Run(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(fullPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // メモリにキャッシュ
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze(); // UIスレッドで安全に使用
                return bitmap;
            });
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (PreviewContent.Content is Grid grid &&
                grid.Children.Count == 2 &&
                grid.Children[0] is Slider slider &&
                grid.Children[1] is MediaElement media)
            {
                if (!slider.IsMouseCaptured)
                {
                    slider.Value = media.Position.TotalSeconds;
                }
            }
        }

        private void PreviewPopup_Closed(object? sender, EventArgs e)
        {
            _timer.Stop();
            if (PreviewContent.Content is MediaElement media)
            {
                media.Close();
            }
            else if (
                PreviewContent.Content is Grid grid
                && grid.Children.Count == 2
                && grid.Children[1] is MediaElement audioMedia
            )
            {
                audioMedia.Close();
            }

            PreviewContent.Content = null;
        }

        private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!PreviewPopup.IsOpen)
                return;

            if (PreviewPopup.Child != null && PreviewPopup.Child.IsMouseOver)
                return;

            PreviewPopup.IsOpen = false;
        }

        private void Slider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _timer.Stop();
        }

        private void Slider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider &&
                PreviewContent.Content is Grid grid &&
                grid.Children.Count > 1 &&
                grid.Children[1] is MediaElement media)
            {
                media.Position = TimeSpan.FromSeconds(slider.Value);
                _timer.Start();
            }
        }
        #endregion

        #region 検索ロジック

        private async Task PerformSearchAsync(CancellationToken token)
        {
            this.Cursor = Cursors.Wait;

            string searchTerm = SearchTextBox.Text;
            bool searchSubdirectories = SearchSubdirectoriesCheckBox.IsChecked == true;

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                if (!string.IsNullOrEmpty(_currentDirectory))
                {
                    await LoadFilesAsync(_currentDirectory);
                }
                this.Cursor = Cursors.Arrow;
                return;
            }

            if (string.IsNullOrEmpty(_currentDirectory))
            {
                this.Cursor = Cursors.Arrow;
                return;
            }

            var fileCollection = new ObservableCollection<FileItem>();
            FileList.ItemsSource = fileCollection;

            try
            {
                await SearchFilesAsync(_currentDirectory, searchTerm, searchSubdirectories, fileCollection, token);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchCts?.Cancel();

            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private async void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer.Stop();

            _searchCts = new CancellationTokenSource();
            try
            {
                await PerformSearchAsync(_searchCts.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("PerformSearchAsync was canceled.");
            }
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _searchTimer.Stop();
                _searchCts?.Cancel();
                SearchTimer_Tick(sender, e);
            }
        }

        private async Task SearchFilesAsync(string path, string searchTerm, bool searchSubdirectories, ObservableCollection<FileItem> fileCollection, CancellationToken token)
        {
            try
            {
                await Task.Run(async () =>
                {
                    if (searchSubdirectories)
                    {
                        await SearchRecursivelyAsync(path, searchTerm, fileCollection, token);
                    }
                    else
                    {
                        foreach (var file in Directory.EnumerateFiles(path, $"*{searchTerm}*"))
                        {
                            token.ThrowIfCancellationRequested();

                            var info = new FileInfo(file);
                            if (!FileExplorerSettings.Default.ShowHiddenFiles && info.Attributes.HasFlag(FileAttributes.Hidden))
                                continue;

                            var icon = await ShellIcon.GetSmallIconAsync(file, false);
                            var fileItem = new FileItem
                            {
                                Name = info.Name,
                                FullPath = info.FullName,
                                Type = info.Extension,
                                Size = $"{info.Length / 1024} KB",
                                SizeInBytes = info.Length,
                                LastWriteString = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                                LastWriteTime = info.LastWriteTime,
                                Icon = icon,
                            };

                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                fileCollection.Add(fileItem);
                            });
                        }
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Search was successfully canceled.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"検索中に予期せぬエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private static async Task SearchRecursivelyAsync(string directory, string searchTerm, ObservableCollection<FileItem> collection, CancellationToken token)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, $"*{searchTerm}*"))
                {
                    token.ThrowIfCancellationRequested();

                    var info = new FileInfo(file);
                    if (!FileExplorerSettings.Default.ShowHiddenFiles && info.Attributes.HasFlag(FileAttributes.Hidden))
                        continue;

                    var icon = await ShellIcon.GetSmallIconAsync(file, false);
                    var fileItem = new FileItem
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        Type = info.Extension,
                        Size = $"{info.Length / 1024} KB",
                        SizeInBytes = info.Length,
                        LastWriteString = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                        LastWriteTime = info.LastWriteTime,
                        Icon = icon,
                    };

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        collection.Add(fileItem);
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            try
            {
                foreach (var subDirectory in Directory.EnumerateDirectories(directory))
                {
                    token.ThrowIfCancellationRequested();
                    await SearchRecursivelyAsync(subDirectory, searchTerm, collection, token);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion

        #region 戻る、進む、リロードボタンの実装
        private void UpdateNavigationButtons()
        {
            BackButton.IsEnabled = _currentHistoryIndex > 0;
            ForwardButton.IsEnabled = _currentHistoryIndex < _navigationHistory.Count - 1;
        }

        private void AddHistory(string path)
        {
            if (_navigationHistory.Count > 0 && _navigationHistory[_currentHistoryIndex] == path)
            {
                return;
            }

            if (_currentHistoryIndex < _navigationHistory.Count - 1)
            {
                _navigationHistory.RemoveRange(_currentHistoryIndex + 1, _navigationHistory.Count - (_currentHistoryIndex + 1));
            }

            _navigationHistory.Add(path);
            _currentHistoryIndex++;
            UpdateNavigationButtons();
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentHistoryIndex > 0)
            {
                _currentHistoryIndex--;
                await NavigateHistory();
            }
        }

        private async void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentHistoryIndex < _navigationHistory.Count - 1)
            {
                _currentHistoryIndex++;
                await NavigateHistory();
            }
        }

        private async void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentDirectory))
            {
                await LoadFilesAsync(_currentDirectory);
            }
        }

        private async Task NavigateHistory()
        {
            _isNavigatingViaHistory = true;

            string pathToNavigate = _navigationHistory[_currentHistoryIndex];
            await SelectTreeViewItemByPathAsync(pathToNavigate);

            if (Directory.Exists(pathToNavigate))
            {
                _currentDirectory = pathToNavigate;

                await LoadFilesAsync(pathToNavigate);

                _watcher?.Dispose();
                _watcher = new FileSystemWatcher(pathToNavigate)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true,
                };
                _watcher.Created += OnFileSystemChanged;
                _watcher.Deleted += OnFileSystemChanged;
                _watcher.Renamed += OnFileSystemChanged;

                PathChanged?.Invoke(pathToNavigate);
            }

            _isNavigatingViaHistory = false;

            UpdateNavigationButtons();
        }

        private void FileExplorerControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.XButton1)
            {
                BackButton_Click(sender, e);
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.XButton2)
            {
                ForwardButton_Click(sender, e);
                e.Handled = true;
            }
        }
        #endregion
    }
}
