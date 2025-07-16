using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    public partial class FileExplorerControl : UserControl
    {
        public class FileItem
        {
            public string? Name { get; set; }
            public string? Type { get; set; }
            public string? Size { get; set; }
            public long SizeInBytes { get; set; }
            public ImageSource? Icon { get; set; }
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

        public FileExplorerControl()
        {
            InitializeComponent();

            this.Loaded += FileExplorerControl_Loaded;
            this.Unloaded += FileExplorerControl_Unloaded;

            PreviewPopup.Closed += PreviewPopup_Closed;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += Timer_Tick;
        }

        private async void FileExplorerControl_Loaded(object sender, RoutedEventArgs e)
        {
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.Title = "YMM4 エクスプローラー";
                parentWindow.Topmost = FileExplorerSettings.Default.IsTopmost;
            }

            if (FileExplorerSettings.Default.IsCheckVersion && await GetVersion.CheckVersionAsync("YMM4エクスプローラー"))
            {
                string url = "https://ymm4-info.net/";
                var result = MessageBox.Show(
                    $"新しいバージョンがあります。\n\n最新バージョンを確認しますか？\n{url}",
                    "YMM4エクスプローラープラグイン",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information);

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

            LoadDrives();

            var lastPath = FileExplorerSettings.Default.LastOpenedDirectory;
            if (!string.IsNullOrEmpty(lastPath) && Directory.Exists(lastPath))
            {
                SelectTreeViewItemByPath(lastPath);
            }
        }

        private void FileExplorerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _watcher?.Dispose();
        }

        private void LoadDrives()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                var item = new TreeViewItem
                {
                    Header = CreateTreeViewItemHeader(drive.Name, drive.RootDirectory.FullName),
                    Tag = drive.RootDirectory.FullName
                };
                item.Items.Add(null);
                item.Expanded += Folder_Expanded;

                DirectoryTree.Items.Add(item);
            }
        }

        private void SelectTreeViewItemByPath(string path)
        {
            var pathParts = path.Split(Path.DirectorySeparatorChar).ToList();
            if (pathParts.Count > 1 && pathParts[0].EndsWith(':'))
            {
                pathParts[0] = pathParts[0] + Path.DirectorySeparatorChar;
            }

            ItemsControl? parent = DirectoryTree;
            TreeViewItem? finalItem = null;

            foreach (var part in pathParts)
            {
                if (parent == null) break;

                TreeViewItem? currentItem = null;
                foreach (TreeViewItem item in parent.Items)
                {
                    var itemPath = Path.GetFileName((string)item.Tag);
                    if (parent == DirectoryTree)
                    {
                        itemPath = (string)item.Tag;
                    }

                    if (itemPath == part)
                    {
                        currentItem = item;
                        break;
                    }
                }

                if (currentItem != null)
                {
                    currentItem.IsExpanded = true;
                    parent = currentItem;
                    finalItem = currentItem;
                }
                else
                {
                    break;
                }
            }

            if (finalItem != null)
            {
                finalItem.IsSelected = true;
            }
        }

        private void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewItem)sender;
            if (item.Items.Count == 1 && item.Items[0] == null)
            {
                item.Items.Clear();
                try
                {
                    var parentDirInfo = new DirectoryInfo((string)item.Tag);
                    foreach (var dir in parentDirInfo.GetDirectories())
                    {
                        if (!FileExplorerSettings.Default.ShowHiddenFiles && dir.Attributes.HasFlag(FileAttributes.Hidden))
                            continue;
                        
                        var subItem = new TreeViewItem
                        {
                            Header = CreateTreeViewItemHeader(dir.Name, dir.FullName),
                            Tag = dir.FullName
                        };
                        subItem.Items.Add(null);
                        subItem.Expanded += Folder_Expanded;
                        item.Items.Add(subItem);
                    }
                }
                catch { }
            }
        }

        private void DirectoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _watcher?.Dispose();
            if (DirectoryTree.SelectedItem is TreeViewItem item)
            {
                string? path = item.Tag as string;
                if (Directory.Exists(path))
                {
                    _currentDirectory = path;
                    LoadFiles(path);

                    _watcher = new FileSystemWatcher(path)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        EnableRaisingEvents = true
                    };

                    _watcher.Created += OnFileSystemChanged;
                    _watcher.Deleted += OnFileSystemChanged;
                    _watcher.Renamed += OnFileSystemChanged;

                    FileExplorerSettings.Default.LastOpenedDirectory = path;
                    FileExplorerSettings.Default.Save();
                }
            }
        }

        private void LoadFiles(string path)
        {
            var files = new ObservableCollection<FileItem>();
            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    var info = new FileInfo(file);

                    if (!FileExplorerSettings.Default.ShowHiddenFiles && info.Attributes.HasFlag(FileAttributes.Hidden))
                        continue;

                    files.Add(new FileItem
                    {
                        Name = info.Name,
                        Type = info.Extension,
                        Size = $"{info.Length / 1024} KB",
                        SizeInBytes = info.Length,
                        Icon = ShellIcon.GetSmallIcon(file, false)
                    });
                }
            }
            catch { }

            FileList.ItemsSource = files;
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_currentDirectory != null)
                {
                    LoadFiles(_currentDirectory);
                }
            });
        }

        private static StackPanel CreateTreeViewItemHeader(string name, string fullPath)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Image
                    {
                        Source = ShellIcon.GetSmallIcon(fullPath, true),
                        Width=16,
                        Height=16,
                        Margin = new Thickness(0,0,4,0)
                    },
                    new TextBlock
                    {
                        Text = name,
                    }
                }
            };
        }

        private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
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
                    if (FileList.SelectedItem is FileItem file &&
                        !string.IsNullOrEmpty(file.Name) &&
                        _currentDirectory != null)
                    {
                        string fullPath = Path.Combine(_currentDirectory, file.Name);
                        DataObject data = new(DataFormats.FileDrop, new string[] { fullPath });

                        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy);
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

        //Preview
        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _timer.Stop();
            if (PreviewContent.Content is MediaElement oldmedia)
            {
                oldmedia.Close();
            }
            else if (PreviewContent.Content is Grid grid &&
                     grid.Children.Count > 1 &&
                     grid.Children[1] is MediaElement audioMedia)
            {
                audioMedia.Close();
            }
            PreviewContent.Content = null;

            if (FileList.SelectedItem is not FileItem selectedFile || _currentDirectory is null || string.IsNullOrEmpty(selectedFile.Name))
                return;

            var fullPath = Path.Combine(_currentDirectory, selectedFile.Name);
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
                        var image = new Image
                        {
                            Source = new BitmapImage(new Uri(fullPath)),
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

                PreviewPopup.IsOpen = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プレビューの読み込みに失敗: {ex.Message}");
            }
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
            else if(PreviewContent.Content is Grid grid &&
             grid.Children.Count == 2 &&
             grid.Children[1] is MediaElement audioMedia)
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
    }
}
