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
using YMM4FileExplorer.Model;
using YMM4FileExplorer.Settings;
using YukkuriMovieMaker.Commons;

namespace YMM4FileExplorer
{
    [SuppressMessage("Design", "CA1001", Justification = "<保留中>")]
    public partial class FileExplorerControl : UserControl
    {
        // tree
        private bool _isUpdatingTree = false;

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
        private readonly DispatcherTimer _largePreviewTimer;
        private MediaElement? _largePreviewMediaElement;

        //Save
        private readonly string _initialPath;
        public event Action<string>? PathChanged;

        // Navigation
        private readonly List<string> _navigationHistory = [];
        private int _currentHistoryIndex = -1;
        private bool _isNavigatingViaHistory = false;
        private bool _isNavigatingWithFavorite = false;

        // Search
        private readonly DispatcherTimer _searchTimer;
        private CancellationTokenSource? _searchCts;

        public FileExplorerControl(string initialPath = "C:\\")
        {
            InitializeComponent();

            _initialPath = initialPath;

            FileExplorerControl_Loaded();
            this.Unloaded += FileExplorerControl_Unloaded;

            PreviewPopup.Closed += PreviewPopup_Closed;

            // Popupのタイマー
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += Timer_Tick;

            _largePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _largePreviewTimer.Tick += LargePreviewTimer_Tick;

            // 検索時のタイマー
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += SearchTimer_Tick;
        }

        private async void FileExplorerControl_Loaded()
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
                    MessageBoxButton.OK
                );
            }
        }

        private async Task InitializeAsync()
        {
            if (FileExplorerSettings.Default.ShowSelectedFolderPath)
            {
                await UpdateTreeView(_initialPath);
                _currentDirectory = _initialPath;
                await LoadFilesAsync(_initialPath);
                AddHistory(_initialPath);
            }
            else
            {
                await LoadDrivesAsync();
                await SelectTreeViewItemByPathAsync(_initialPath);
                AddHistory(_initialPath);
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
            var icon = await ShellIcon.GetIconAsync(fullPath, true);

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

        private async Task UpdateTreeView(string path)
        {
            if (!Directory.Exists(path)) return;

           
            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.Parent == null)
            {
                await LoadDrivesAsync();
                await SelectTreeViewItemByPathAsync(path);
                return;
            }

            _isUpdatingTree = true;
            DirectoryTree.Items.Clear();

            if (dirInfo.Parent != null)
            {
                var parentItem = new TreeViewItem
                {
                    Header = "[...]",
                    Tag = dirInfo.Parent.FullName
                };
                DirectoryTree.Items.Add(parentItem);
            }

            var rootItem = new TreeViewItem
            {
                Header = await CreateTreeViewItemHeaderAsync(dirInfo.Name, dirInfo.FullName),
                Tag = dirInfo.FullName,
                IsExpanded = true
            };
            DirectoryTree.Items.Add(rootItem);

            try
            {
                var directories = await Task.Run(() =>
                    dirInfo.GetDirectories()
                           .Where(dir => FileExplorerSettings.Default.ShowHiddenFiles || !dir.Attributes.HasFlag(FileAttributes.Hidden))
                           .ToList());

                foreach (var dir in directories)
                {
                    var subItem = new TreeViewItem
                    {
                        Header = await CreateTreeViewItemHeaderAsync(dir.Name, dir.FullName),
                        Tag = dir.FullName
                    };

                    try
                    {
                        if (Directory.EnumerateDirectories(dir.FullName).Any())
                        {
                            subItem.Items.Add(null);
                        }
                    }
                    catch (UnauthorizedAccessException) { /* アクセスできないフォルダは無視 */ }

                    subItem.Expanded += Folder_Expanded;
                    rootItem.Items.Add(subItem);
                }
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine($"アクセスが許可されていないフォルダ: {path}");
            }

            _isUpdatingTree = false;
        }

        private async void DirectoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isUpdatingTree || DirectoryTree.SelectedItem == null) return;
            if (_isNavigatingWithFavorite) return;

            if (DirectoryTree.SelectedItem is TreeViewItem item && item.Tag is string path)
            {
                if (item.Header as string == "[...]")
                {
                    if (FileExplorerSettings.Default.ShowSelectedFolderPath)
                    {
                        await UpdateTreeView(path);
                        await HandlePathSelection(path, false);
                    }
                    else
                    {
                        await LoadDrivesAsync();
                        await SelectTreeViewItemByPathAsync(path);
                    }
                    return;
                }

                if (FileExplorerSettings.Default.ShowSelectedFolderPath)
                {
                    await HandlePathSelection(path);
                }
                else
                {
                    if (_isNavigatingViaHistory) return;
                    await HandlePathSelection(path);
                }
            }
        }

        private async void DirectoryTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileExplorerSettings.Default.ShowSelectedFolderPath)
            {
                if (DirectoryTree.SelectedItem is TreeViewItem item && item.Tag is string path)
                {
                    await UpdateTreeView(path);

                    e.Handled = true;
                }
            }
        }

        private async Task HandlePathSelection(string path, bool addToHistory = true)
        {
            if (!Directory.Exists(path)) return;

            _watcher?.Dispose();
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
            if (addToHistory && !_isNavigatingViaHistory)
            {
                AddHistory(path);
            }
        }

        private async Task LoadFilesAsync(string path)
        {
            var fileCollection = new ObservableCollection<FileItem>();

            DetailsListView.ItemsSource = fileCollection; // 詳細ビュー
            IconListView.ItemsSource = fileCollection;  // アイコンビュー

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

                        var icon = await ShellIcon.GetIconAsync(dir, true);
                        var fileItem = new FileItem
                        {
                            Name = info.Name,
                            FullPath = info.FullName,
                            Type = "フォルダー",
                            LastWriteString = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm"),
                            LastWriteTime = info.LastWriteTime,
                            Icon = icon,
                            Thumbnail = icon,
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

                        var icon = await ShellIcon.GetIconAsync(file, false);
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
                            Thumbnail = icon,
                            IsDirectory = false,
                        };

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            fileCollection.Add(fileItem);
                        });

                        _ = LoadThumbnailForItemAsync(fileItem);
                    }
                });

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ファイルの読み込みに失敗: {ex.Message}");
            }
        }


        private static async Task LoadThumbnailForItemAsync(FileItem item)
        {
            try
            {
                if (item.IsDirectory || string.IsNullOrEmpty(item.FullPath))
                    return;

                var thumbnail = await ShellThumbnail.LoadCroppedThumbnailAsync(item.FullPath, thumbSize: 128);

                if (thumbnail != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        item.Thumbnail = thumbnail;
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サムネイル取得失敗: {item.Name} - {ex.Message}");
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
            catch (Exception ex)
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
                    if (sender is not ListView listView) return;

                    var selectedItems = listView.SelectedItems;
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
                            DragDrop.DoDragDrop(listView, data, DragDropEffects.Copy);
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
            ICollectionView dataView = CollectionViewSource.GetDefaultView(DetailsListView.ItemsSource);
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

            foreach (var column in ((GridView)DetailsListView.View).Columns)
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
            if (sender is not ListView listView) return;

            if (listView.SelectedItem is not FileItem selectedItem || string.IsNullOrEmpty(selectedItem.FullPath))
                return;

            if (selectedItem.IsDirectory)
            {
                if (FileExplorerSettings.Default.ShowSelectedFolderPath)
                {
                    await UpdateTreeView(selectedItem.FullPath);
                    await HandlePathSelection(selectedItem.FullPath);
                }
                else
                {
                    await SelectTreeViewItemByPathAsync(selectedItem.FullPath);
                }
            }
            else
            {
                _timer.Stop();
                if (PreviewContent.Content is Grid oldGrid)
                {
                    var mediaToStop = oldGrid.Children.OfType<MediaElement>().FirstOrDefault();
                    mediaToStop?.Close();
                }
                else if (PreviewContent.Content is MediaElement oldMedia)
                {
                    oldMedia.Close();
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
                            var videoSlider = new Slider { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5) };
                            var videoMedia = new MediaElement
                            {
                                Source = new Uri(fullPath),
                                Volume = FileExplorerSettings.Default.PreviewVolumePercentage / 100d,
                                Stretch = Stretch.Uniform,
                                LoadedBehavior = MediaState.Manual,
                                UnloadedBehavior = MediaState.Manual,

                                MaxWidth = 1280,
                                MaxHeight = 1280,
                            };

                            videoMedia.MediaFailed += (s, args) =>
                            {
                                var textBlock = new TextBlock
                                {
                                    Text = "再生に失敗しました。\nコーデックが標準のものではない可能性があります。",
                                    TextWrapping = TextWrapping.Wrap,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    Margin = new Thickness(10)
                                };
                                PreviewContent.Content = textBlock;
                            };

                            videoMedia.MediaOpened += (s, args) =>
                            {
                                if (videoMedia.NaturalDuration.HasTimeSpan)
                                {
                                    videoSlider.Maximum = videoMedia.NaturalDuration.TimeSpan.TotalSeconds;
                                }
                            };

                            videoSlider.PreviewMouseDown += Slider_PreviewMouseDown;
                            videoSlider.PreviewMouseUp += Slider_PreviewMouseUp;

                            var videoGrid = new Grid();
                            videoGrid.Children.Add(videoMedia);
                            videoGrid.Children.Add(videoSlider);

                            PreviewContent.Content = videoGrid;
                            videoMedia.Play();
                            _timer.Start();
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

                            audioMedia.MediaFailed += (s, args) =>
                            {
                                var textBlock = new TextBlock
                                {
                                    Text = "再生に失敗しました。\nコーデックが標準のものではない可能性があります。",
                                    TextWrapping = TextWrapping.Wrap,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    Margin = new Thickness(10)
                                };
                                PreviewContent.Content = textBlock;
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
            if (PreviewContent.Content is Grid grid)
            {
                var slider = grid.Children.OfType<Slider>().FirstOrDefault();
                var media = grid.Children.OfType<MediaElement>().FirstOrDefault();

                if (slider != null && media != null && !slider.IsMouseCaptured)
                {
                    slider.Value = media.Position.TotalSeconds;
                }
            }
        }

        private void PreviewPopup_Closed(object? sender, EventArgs e)
        {
            _timer.Stop();

            if (PreviewContent.Content is Grid grid)
            {
                var mediaToStop = grid.Children.OfType<MediaElement>().FirstOrDefault();
                mediaToStop?.Close();
            }
            else if (PreviewContent.Content is MediaElement media)
            {
                media.Close();
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
            if (sender is Slider slider && PreviewContent.Content is Grid grid)
            {
                var media = grid.Children.OfType<MediaElement>().FirstOrDefault();
                if (media != null)
                {
                    media.Position = TimeSpan.FromSeconds(slider.Value);
                    _timer.Start();
                }
            }
        }

        // プレビューウィンドウ
        private async void ShowLargePreview_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.DataContext is not FileItem selectedItem)
            {
                return;
            }

            _largePreviewTimer.Stop();
            if (PreviewContentHost.Content is Grid grid && grid.Children.OfType<MediaElement>().FirstOrDefault() is MediaElement oldMedia)
            {
                oldMedia.Close();
            }
            PreviewContentHost.Content = null;

            PreviewName.Text = selectedItem.Name;
            PreviewColumn.Width = new GridLength(300);
            LargePreviewSplitter.Visibility = Visibility.Visible;
            LargePreviewPane.Visibility = Visibility.Visible;

            if (selectedItem.IsDirectory || string.IsNullOrEmpty(selectedItem.FullPath))
            {
                PreviewContentHost.Content = new Image { Source = selectedItem.Thumbnail, Stretch = Stretch.Uniform };
                return;
            }

            _largePreviewMediaElement = null;
            MediaControlsPanel.Visibility = Visibility.Collapsed;
            PlayButton.Visibility = Visibility.Visible;
            PauseButton.Visibility = Visibility.Collapsed;

            var existingSlider = MediaControlsPanel.Children.OfType<Slider>().FirstOrDefault();
            if (existingSlider != null)
            {
                MediaControlsPanel.Children.Remove(existingSlider);
            }

            var extension = Path.GetExtension(selectedItem.FullPath).ToLowerInvariant();
            switch (extension)
            {
                // --- 画像ファイル ---
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".gif":
                    var image = new Image { Source = selectedItem.Thumbnail, Stretch = Stretch.Uniform };
                    PreviewContentHost.Content = image;

                    const int largeThumbSize = 512;
                    var largeThumbnail = await ShellThumbnail.LoadCroppedThumbnailAsync(selectedItem.FullPath, thumbSize: largeThumbSize);
                    if (largeThumbnail != null)
                    {
                        image.Source = largeThumbnail;
                    }
                    break;

                // --- 動画ファイル ---
                case ".mp4":
                case ".wmv":
                case ".avi":
                case ".mov":
                    var videoSlider = new Slider { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) };
                    var videoMedia = new MediaElement
                    {
                        Source = new Uri(selectedItem.FullPath),
                        Volume = FileExplorerSettings.Default.PreviewVolumePercentage / 100d,
                        Stretch = Stretch.Uniform,
                        LoadedBehavior = MediaState.Manual,
                        UnloadedBehavior = MediaState.Manual,
                    };

                    _largePreviewMediaElement = videoMedia;
                    MediaControlsPanel.Visibility = Visibility.Visible;
                    PlayButton.Visibility = Visibility.Collapsed;
                    PauseButton.Visibility = Visibility.Visible;
                    videoMedia.MediaEnded += (s, args) =>
                    {
                        PlayButton.Visibility = Visibility.Visible;
                        PauseButton.Visibility = Visibility.Collapsed;
                        _largePreviewMediaElement?.Stop();
                    };

                    videoMedia.MediaFailed += (s, args) =>
                    {
                        var textBlock = new TextBlock
                        {
                            Text = "再生に失敗しました。\nコーデックが標準のものではない可能性があります。",
                            TextWrapping = TextWrapping.Wrap,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(10)
                        };
                        PreviewContentHost.Content = textBlock;
                    };

                    videoMedia.MediaOpened += (s, args) =>
                    {
                        if (videoMedia.NaturalDuration.HasTimeSpan)
                        {
                            videoSlider.Maximum = videoMedia.NaturalDuration.TimeSpan.TotalSeconds;
                        }
                    };

                    videoSlider.PreviewMouseDown += LargePreviewSlider_PreviewMouseDown;
                    videoSlider.PreviewMouseUp += LargePreviewSlider_PreviewMouseUp;

                    Grid.SetColumn(videoSlider, 1);
                    MediaControlsPanel.Children.Add(videoSlider);

                    var videoGrid = new Grid();
                    videoGrid.Children.Add(videoMedia);
                    PreviewContentHost.Content = videoGrid;

                    videoMedia.Play();
                    _largePreviewTimer.Start();
                    break;

                // --- 音声ファイル ---
                case ".mp3":
                case ".wav":
                    var audioSlider = new Slider { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) };
                    var audioMedia = new MediaElement
                    {
                        Source = new Uri(selectedItem.FullPath),
                        Volume = FileExplorerSettings.Default.PreviewVolumePercentage / 100d,
                        LoadedBehavior = MediaState.Manual,
                        UnloadedBehavior = MediaState.Manual,
                    };

                    var iconImage = new Image
                    {
                        Source = selectedItem.Icon,
                        Width = 64,
                        Height = 64,
                        Stretch = Stretch.Uniform,
                        Opacity = 0.3
                    };

                    _largePreviewMediaElement = audioMedia;
                    MediaControlsPanel.Visibility = Visibility.Visible;
                    PlayButton.Visibility = Visibility.Collapsed;
                    PauseButton.Visibility = Visibility.Visible;
                    audioMedia.MediaEnded += (s, args) =>
                    {
                        PlayButton.Visibility = Visibility.Visible;
                        PauseButton.Visibility = Visibility.Collapsed;
                        _largePreviewMediaElement?.Stop();
                    };

                    audioMedia.MediaFailed += (s, args) =>
                    {
                        var textBlock = new TextBlock
                        {
                            Text = "再生に失敗しました。\nコーデックが標準のものではない可能性があります。",
                            TextWrapping = TextWrapping.Wrap,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(10)
                        };
                        PreviewContentHost.Content = textBlock;
                    };

                    audioMedia.MediaOpened += (s, args) =>
                    {
                        if (audioMedia.NaturalDuration.HasTimeSpan)
                        {
                            audioSlider.Maximum = audioMedia.NaturalDuration.TimeSpan.TotalSeconds;
                        }
                    };

                    audioSlider.PreviewMouseDown += LargePreviewSlider_PreviewMouseDown;
                    audioSlider.PreviewMouseUp += LargePreviewSlider_PreviewMouseUp;

                    Grid.SetColumn(audioSlider, 1);
                    MediaControlsPanel.Children.Add(audioSlider);

                    var audioGrid = new Grid();
                    audioGrid.Children.Add(iconImage);
                    audioGrid.Children.Add(audioMedia);
                    PreviewContentHost.Content = audioGrid;

                    audioMedia.Play();
                    _largePreviewTimer.Start();
                    break;

                // --- それ以外のファイル ---
                default:
                    PreviewContentHost.Content = new Image { Source = selectedItem.Icon, Stretch = Stretch.Uniform };
                    break;
            }
        }

        private void ClosePreview_Click(object sender, RoutedEventArgs e)
        {
            _largePreviewTimer.Stop();

            if (_largePreviewMediaElement != null)
            {
                _largePreviewMediaElement.Close();
                _largePreviewMediaElement = null;
            }

            var existingSlider = MediaControlsPanel.Children.OfType<Slider>().FirstOrDefault();
            if (existingSlider != null)
            {
                MediaControlsPanel.Children.Remove(existingSlider);
            }

            PreviewContentHost.Content = null;

            LargePreviewPane.Visibility = Visibility.Collapsed;
            LargePreviewSplitter.Visibility = Visibility.Collapsed;
            PreviewColumn.Width = new GridLength(0, GridUnitType.Auto);
        }

        private void LargePreviewTimer_Tick(object? sender, EventArgs e)
        {
            var slider = MediaControlsPanel.Children.OfType<Slider>().FirstOrDefault();
            if (_largePreviewMediaElement != null && slider != null && !slider.IsMouseCaptured)
            {
                slider.Value = _largePreviewMediaElement.Position.TotalSeconds;
            }
        }

        private void LargePreviewSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _largePreviewTimer.Stop();
        }

        private void LargePreviewSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider && _largePreviewMediaElement != null)
            {
                _largePreviewMediaElement.Position = TimeSpan.FromSeconds(slider.Value);
                _largePreviewTimer.Start();
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_largePreviewMediaElement != null)
            {
                _largePreviewMediaElement.Play();
                PlayButton.Visibility = Visibility.Collapsed;
                PauseButton.Visibility = Visibility.Visible;
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_largePreviewMediaElement != null)
            {
                _largePreviewMediaElement.Pause();
                PlayButton.Visibility = Visibility.Visible;
                PauseButton.Visibility = Visibility.Collapsed;
            }
        }
        #endregion

        #region 検索ロジック

        private async Task PerformSearchAsync(CancellationToken token)
        {
            this.Cursor = Cursors.Wait;

            string searchTerm = SearchTextBox.Text;
            bool searchSubdirectories = SearchSubdirectoriesCheckBox.IsChecked == true;

            if (Directory.Exists(searchTerm))
            {
                await LoadFilesAsync(searchTerm);
                this.Cursor = Cursors.Arrow;
                return;
            }

            if (File.Exists(searchTerm))
            {
                string? parentDirectory = Path.GetDirectoryName(searchTerm);
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    await LoadFilesAsync(parentDirectory);

                    var fileItemToSelect = (DetailsListView.ItemsSource as IEnumerable<FileItem>)
                              ?.FirstOrDefault(f => f.FullPath == searchTerm);

                    if (fileItemToSelect != null)
                    {
                        DetailsListView.SelectedItem = fileItemToSelect;
                        DetailsListView.ScrollIntoView(fileItemToSelect);

                        IconListView.SelectedItem = fileItemToSelect;
                        IconListView.ScrollIntoView(fileItemToSelect);
                    }
                }
                this.Cursor = Cursors.Arrow;
                return;
            }

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
            DetailsListView.ItemsSource = fileCollection; // 詳細ビュー
            IconListView.ItemsSource = fileCollection;  // アイコンビュー

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

                            var icon = await ShellIcon.GetIconAsync(file, false);
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
                MessageBox.Show($"検索中に予期せぬエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK);
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

                    var icon = await ShellIcon.GetIconAsync(file, false);
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

            if (FileExplorerSettings.Default.ShowSelectedFolderPath)
            {
                await UpdateTreeView(pathToNavigate);
            }
            else
            {
                await SelectTreeViewItemByPathAsync(pathToNavigate);
            }

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

        #region モードの変更
        private void DetailsView_Click(object sender, RoutedEventArgs e)
        {
            DetailsListView.Visibility = Visibility.Visible;
            IconListView.Visibility = Visibility.Collapsed;
        }

        private void IconView_Click(object sender, RoutedEventArgs e)
        {
            DetailsListView.Visibility = Visibility.Collapsed;
            IconListView.Visibility = Visibility.Visible;
        }

        private bool _isSelectionChanging = false;

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSelectionChanging) return;

            _isSelectionChanging = true;

            var sourceListView = sender as ListView;
            var targetListView = (sourceListView == DetailsListView) ? IconListView : DetailsListView;

            targetListView.SelectedItems.Clear();
            foreach (var item in sourceListView.SelectedItems)
            {
                targetListView.SelectedItems.Add(item);
            }

            if (sourceListView.SelectedItem != null)
            {
                targetListView.ScrollIntoView(sourceListView.SelectedItem);
            }

            _isSelectionChanging = false;
        }
        #endregion

        #region お気に入り
        private async void FavoritesComboBox_ValueChanged(object sender, EventArgs e)
        {
            if (FavoritesComboBox.Value is string path && !string.IsNullOrEmpty(path))
            {
                _isNavigatingWithFavorite = true;

                if (Directory.Exists(path))
                {
                    if (FileExplorerSettings.Default.ShowSelectedFolderPath)
                    {
                        await UpdateTreeView(path);
                        await HandlePathSelection(path);
                    }
                    else
                    {
                        await SelectTreeViewItemByPathAsync(path);
                    }
                }
                else
                {
                    MessageBox.Show("このフォルダは存在しません。");
                }

                _isNavigatingWithFavorite = false;
            }
        }

        private void DirectoryTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (DirectoryTree.SelectedItem is not TreeViewItem selectedItem || selectedItem.Tag is not string path)
            {
                e.Handled = true;
                return;
            }

            bool isFavorite = FileExplorerSettings.Default.Favorites.Any(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));

            AddFavoriteMenuItem.Visibility = isFavorite ? Visibility.Collapsed : Visibility.Visible;
            RemoveFavoriteMenuItem.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (DirectoryTree.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string path)
            {
                var dirInfo = new DirectoryInfo(path);
                var newFavorite = new FavoriteItem { Name = dirInfo.Name, FullPath = path };

                FileExplorerSettings.Default.Favorites.Add(newFavorite);
                FileExplorerSettings.Default.Save();
            }
        }

        private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (DirectoryTree.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string path)
            {
                var favoriteToRemove = FileExplorerSettings.Default.Favorites
                    .FirstOrDefault(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));

                if (favoriteToRemove != null)
                {
                    FileExplorerSettings.Default.Favorites.Remove(favoriteToRemove);
                    FileExplorerSettings.Default.Save();
                }
            }
        }
        #endregion
    }
}
