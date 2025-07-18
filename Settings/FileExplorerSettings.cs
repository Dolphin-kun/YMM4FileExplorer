using YukkuriMovieMaker.Plugin;

namespace YMM4FileExplorer.Settings
{
    internal class FileExplorerSettings: SettingsBase<FileExplorerSettings>
    {
        public override SettingsCategory Category => SettingsCategory.None;
        public override string Name => "YMM4エクスプローラー";

        public override bool HasSettingView => true;
        public override object? SettingView => new FileExplorerSettingsView();

        public bool IsCheckVersion { get => isCheckVersion; set => Set(ref isCheckVersion, value); }
        private bool isCheckVersion = true;

        public bool IsTopmost { get => isTopmost; set => Set(ref isTopmost, value); }
        private bool isTopmost = true;

        public bool ShowHiddenFiles { get => showHiddenFiles; set => Set(ref showHiddenFiles, value); }
        private bool showHiddenFiles = false;

        public double PreviewVolume { get => previewVolume; set => Set(ref previewVolume, value); }
        private double previewVolume = 0.5;

        public string SavedTabsJson { get => savedTabsJson; set => Set(ref savedTabsJson, value); }
        private string savedTabsJson = string.Empty;

        public override void Initialize()
        {
        }
    }
}
