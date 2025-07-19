using System.Collections.ObjectModel;
using System.Text.Json;
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
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public ObservableCollection<FileExplorerTabControlViewModel> Tabs { get; set; }

        public FileExplorerTabControl()
        {
            InitializeComponent();

            Tabs = [];
            MainTabControl.ItemsSource = Tabs;

            this.Loaded += FileExplorerTabControl_Loaded;
            this.Unloaded += FileExplorerTabControl_Unloaded;
        }

        #region 状態の保存と復元

        private async async FileExplorerTabControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadTabsStateAsync();
        }

        private async void FileExplorerTabControl_Unloaded(object sender, RoutedEventArgs e)
        {
            await SaveTabsStateAsync();
        }

        private async Task LoadTabsStateAsync()
        {
            var json = FileExplorerSettings.Default.SavedTabsJson;
            if (string.IsNullOrEmpty(json))
            {
                Dispatcher.Invoke(() => AddNewTab("新しいタブ", "C:\\"));
                return;
            }

            try
            {
                var savedTabs = await JsonSerializer.DeserializeAsync<List<TabState>>(
                    json,
                    _jsonOptions
                );
                if (savedTabs == null || savedTabs.Count == 0)
                {
                    throw new InvalidOperationException("Failed to deserialize or list is empty.");
                }

                Dispatcher.Invoke(() =>
                {
                    foreach (var tabState in savedTabs)
                    {
                        AddNewTab(tabState.Header, tabState.Path, tabState.Id);
                    }
                });
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    Tabs.Clear();
                    AddNewTab("新しいタブ (復元失敗)", "C:\\");
                });
            }
        }

        private async Task SaveTabsStateAsync()
        {
            var tabsToSave = Tabs.Select(vm => new TabState
                {
                    Id = vm.Id,
                    Header = vm.Header,
                    Path = vm.Path,
                })
                .ToList();

            string json = await JsonSerializer.SerializeAsync(tabsToSave, _jsonOptions);
            FileExplorerSettings.Default.SavedTabsJson = json;
        }

        #endregion

        #region UIイベントハンドラ

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab($"新しいタブ {Tabs.Count + 1}", "C:\\");
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.Count > 1 && sender is FrameworkElement element && element.DataContext is FileExplorerTabControlViewModel tab)
            {
                Tabs.Remove(tab);
                SaveTabsState();
            }
        }

        private void RenameTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is FileExplorerTabControlViewModel tab)
            {
                string newName = ShowInputDialog(tab.Header);
                if (!string.IsNullOrEmpty(newName) && newName != tab.Header)
                {
                    tab.Header = newName;
                }
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

        private void AddNewTab(string header, string path, string? id = null)
        {
            var tabContent = new FileExplorerControl(path);
            var newTabViewModel = new FileExplorerTabControlViewModel(header, path, tabContent, id);

            tabContent.PathChanged += (newPath) =>
            {
                newTabViewModel.Path = newPath;
                SaveTabsState();
            };

            Tabs.Add(newTabViewModel);
            MainTabControl.SelectedItem = newTabViewModel;
        }

        private string ShowInputDialog(string defaultValue)
        {
            var dialog = new InputDialog(defaultValue)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
                return dialog.ResponseText;
            return defaultValue;
        }

    }
}
