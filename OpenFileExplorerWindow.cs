using YukkuriMovieMaker.Plugin;

namespace YMM4FileExplorer
{
    public class OpenFileExplorerWindow : IToolPlugin
    {
        public string Name => "YMM4エクスプローラー";

        public Type ViewModelType => typeof(ViewModelBase);

        public Type ViewType => typeof(FileExplorerTabControl);
    }
}