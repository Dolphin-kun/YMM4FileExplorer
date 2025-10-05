using System.Diagnostics;
using System.Reflection;
using System.Windows;
using YMM4FileExplorer.Settings;
using YukkuriMovieMaker.Plugin;

namespace YMM4FileExplorer
{
    [PluginDetails(AuthorName = "いるかぁぁ", ContentId = "nc433720")]
    public class OpenFileExplorerView : IToolPlugin
    {
        public string Name => "YMM4エクスプローラー";
        public Type ViewModelType => typeof(OpenFileExplorerViewModel);
        public Type ViewType => typeof(FileExplorerTabControl);

        public PluginDetailsAttribute Details => GetType().GetCustomAttribute<PluginDetailsAttribute>() ?? new();


        public OpenFileExplorerView()
        {
            CheckForUpdateAsync();
        }

        private static async void CheckForUpdateAsync()
        {
            try
            {
                if (FileExplorerSettings.Default.IsCheckVersion && await GetVersion.CheckVersionAsync("YMM4エクスプローラー"))
                {
                    string url = "https://ymm4-info.net/ymme/YMM4%E3%82%A8%E3%82%AF%E3%82%B9%E3%83%97%E3%83%AD%E3%83%BC%E3%83%A9%E3%83%BC%E3%83%97%E3%83%A9%E3%82%B0%E3%82%A4%E3%83%B3";
                    var result = MessageBox.Show(
                        $"YMM4エクスプローラープラグインに新しいバージョンがあります。\nプラグインポータルまたは配布サイトよりアップデートが可能です。\n\nOKを押すと配布サイトが開きます。\n{url}",
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] バージョンチェック中にエラーが発生しました: {ex.Message}");
            }
        }
    }
}