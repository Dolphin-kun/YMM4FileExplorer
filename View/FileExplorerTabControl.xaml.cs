using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using YMM4FileExplorer.Model;
using YMM4FileExplorer.Settings;
using YMM4FileExplorer.View;
using YMM4FileExplorer.ViewModel;

namespace YMM4FileExplorer
{
    public partial class FileExplorerTabControl : UserControl
    {
        public ObservableCollection<FileExplorerTabControlViewModel> Tabs { get; set; }
        bool isLoaded = false;

        //v4.45.0.0
        static bool _isInitialContentLoaded = false;

        public FileExplorerTabControl()
        {
            InitializeComponent();

            Tabs = [];
            MainTabControl.ItemsSource = Tabs;

            FileExplorerTabControl_Loaded();
            this.Unloaded += FileExplorerTabControl_Unloaded;
        }

        #region 状態の保存と復元

        private async void FileExplorerTabControl_Loaded()
        {
            try
            {
                await LoadTabsStateAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private async void FileExplorerTabControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!isLoaded)
            {
                isLoaded = true;
            }
            else
            {
                try
                {
                    await SaveTabsStateAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }

        }

        private async Task LoadTabsStateAsync()
        {
            if (_isInitialContentLoaded)
                return;

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

            var savedTabs = FileExplorerSettings.Default.SavedTabs;
            if (savedTabs == null || savedTabs.Count == 0)
            {
                var newTab = Dispatcher.Invoke(() => AddNewTab("新しいタブ", "C:\\"));
                MainTabControl.SelectedItem = newTab;
                return ;
            }

            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var tabState in savedTabs)
                    {
                        AddNewTab(tabState.Header, tabState.Path, tabState.Id);
                    }

                    var lastSelectedId = FileExplorerSettings.Default.LastSelectedTabId;

                    var tabToSelect = string.IsNullOrEmpty(lastSelectedId)
                        ? Tabs.FirstOrDefault()
                        : Tabs.FirstOrDefault(vm => vm.Id == lastSelectedId);

                    MainTabControl.SelectedItem = tabToSelect ?? Tabs.FirstOrDefault();
                });
            }
            catch
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    Tabs.Clear();
                    AddNewTab("新しいタブ (復元失敗)", "C:\\");
                });
            }

            _isInitialContentLoaded = true;
        }

        private Task SaveTabsStateAsync()
        {
            var tabsToSave = Tabs.Select(vm => new TabState
            {
                Id = vm.Id,
                Header = vm.Header,
                Path = vm.Path,
            }).ToList();

            FileExplorerSettings.Default.SavedTabs = tabsToSave;

            if (MainTabControl.SelectedItem is FileExplorerTabControlViewModel selectedVm)
            {
                FileExplorerSettings.Default.LastSelectedTabId = selectedVm.Id;
            }

            return Task.CompletedTask;
        }

        #endregion

        #region UIイベントハンドラ

        private async void AddTab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newTab = AddNewTab($"新しいタブ {Tabs.Count + 1}", "C:\\");
                MainTabControl.SelectedItem = newTab;
                await SaveTabsStateAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"タブ追加中にエラーが発生しました: {ex.Message}");
            }
        }

        private async void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (
                    Tabs.Count > 1
                    && sender is FrameworkElement element
                    && element.DataContext is FileExplorerTabControlViewModel tab
                )
                {
                    Tabs.Remove(tab);
                    await SaveTabsStateAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"タブ削除中にエラーが発生しました: {ex.Message}");
            }
        }

        private async void RenameTab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (
                    sender is MenuItem menuItem
                    && menuItem.DataContext is FileExplorerTabControlViewModel tab
                )
                {
                    string newName = ShowInputDialog(tab.Header);
                    if (!string.IsNullOrEmpty(newName) && newName != tab.Header)
                    {
                        tab.Header = newName;
                        await SaveTabsStateAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"タブ名変更中にエラーが発生しました: {ex.Message}");
            }
        }

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        #endregion

        private FileExplorerTabControlViewModel AddNewTab(string header, string path, string? id = null)
        {
            var tabContent = new FileExplorerControl(path);
            var newTabViewModel = new FileExplorerTabControlViewModel(header, path, tabContent, id);

            tabContent.PathChanged += async (newPath) =>
            {
                newTabViewModel.Path = newPath;
                await SaveTabsStateAsync();
            };

            Tabs.Add(newTabViewModel);

            return newTabViewModel;
        }

        private string ShowInputDialog(string defaultValue)
        {
            var dialog = new InputDialog(defaultValue)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
                return dialog.ResponseText ?? "";
            return defaultValue;
        }

    }
}
