using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
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

        private async void FileExplorerTabControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadTabsStateAsync();
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private async void FileExplorerTabControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveTabsStateAsync();
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
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
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                var savedTabs = await JsonSerializer.DeserializeAsync<List<TabState>>(
                    stream,
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

            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, tabsToSave, _jsonOptions);
            string json = Encoding.UTF8.GetString(stream.ToArray());
            FileExplorerSettings.Default.SavedTabsJson = json;
        }

        #endregion

        #region UIイベントハンドラ

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab($"新しいタブ {Tabs.Count + 1}", "C:\\");
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
            catch (System.Exception ex)
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
            catch (System.Exception ex)
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

        private void AddNewTab(string header, string path, string? id = null)
        {
            var tabContent = new FileExplorerControl(path);
            var newTabViewModel = new FileExplorerTabControlViewModel(header, path, tabContent, id);

            tabContent.PathChanged += async (newPath) =>
            {
                newTabViewModel.Path = newPath;
                await SaveTabsStateAsync();
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
                return dialog.ResponseText ?? "";
            return defaultValue;
        }

    }
}
