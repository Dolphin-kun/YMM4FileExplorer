using YukkuriMovieMaker.Plugin;

namespace YMM4FileExplorer
{
    public class OpenFileExplorerWindow : IToolPlugin
    {
        public string Name => "YMM4エクスプローラー";

        public Type ViewModelType => typeof(OpenFileExplorerWindowViewModel);

        public Type ViewType => typeof(FileExplorerControl);
    }
}