using System.Windows.Media;
using YukkuriMovieMaker.Commons;

namespace YMM4FileExplorer.Model
{
    public class FileItem : Bindable
    {
        public string? Name { get; set; }
        public string? FullPath { get; set; }
        public string? Type { get; set; }
        public string? Size { get; set; }
        public long SizeInBytes { get; set; }
        public string? LastWriteString { get; set; }
        public DateTime LastWriteTime { get; set; }
        public ImageSource? Icon { get; set; }

        private ImageSource? thumbnail;
        public ImageSource? Thumbnail { get => thumbnail;set=>Set(ref  thumbnail, value); }

        public bool IsDirectory { get; set; }
    }
}
