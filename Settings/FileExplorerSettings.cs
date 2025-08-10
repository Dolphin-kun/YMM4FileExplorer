using YMM4FileExplorer.Model;
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

        public double PreviewVolumePercentage { get => previewVolumePercentage; set => Set(ref previewVolumePercentage, value); }
        private double previewVolumePercentage = 50;

        public double PreviewSizeMultiplier { get => previewSizeMultiplier; set => Set(ref previewSizeMultiplier, value); }
        private double previewSizeMultiplier = 100;

        public List<TabState> SavedTabs { get => savedTabs; set => Set(ref savedTabs, value); }
        private List<TabState> savedTabs = [];

        public string? LastSelectedTabId { get => lastSelectedTabId; set => Set(ref lastSelectedTabId, value); }
        private string? lastSelectedTabId;

        public override void Initialize()
        {
        }
    }
}
