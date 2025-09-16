using YukkuriMovieMaker.Commons;

namespace YMM4FileExplorer.ViewModel
{
    public class FileExplorerTabControlViewModel : Bindable
    {
        public string Id { get; }

        private string _header;
        public string Header { get => _header; set => Set(ref _header, value); }

        private string _path;
        public string Path { get => _path; set => Set(ref _path, value); }

        public object Content { get; }

        public FileExplorerTabControlViewModel(string header, string path, object content, string? id = null)
        {
            Id = id ?? Guid.NewGuid().ToString();
            _header = header;
            _path = path;
            Content = content;
        }
    }
}
