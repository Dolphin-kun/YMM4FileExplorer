using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YMM4FileExplorer.ViewModel
{
    public class FileExplorerTabControlViewModel : INotifyPropertyChanged
    {
        public string Id { get; }
        private string _header;
        public string Header
        {
            get => _header;
            set
            {
                if (_header != value)
                {
                    _header = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _path;
        public string Path
        {
            get => _path;
            set
            {
                if (_path != value)
                {
                    _path = value;
                    OnPropertyChanged();
                }
            }
        }

        public object Content { get; }

        public FileExplorerTabControlViewModel(string header, string path, object content, string? id = null)
        {
            Id = id ?? Guid.NewGuid().ToString();
            _header = header;
            _path = path;
            Content = content;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
