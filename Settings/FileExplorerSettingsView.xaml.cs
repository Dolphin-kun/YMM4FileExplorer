using System.Reflection;
using System.Windows.Controls;

namespace YMM4FileExplorer.Settings
{
    public partial class FileExplorerSettingsView : UserControl
    {
        public FileExplorerSettingsView()
        {
            InitializeComponent();

            try
            {
                VersionTextBlock.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "取得エラー";
            }
            catch
            {
                VersionTextBlock.Text = "取得エラー";
            }
        }
    }
}
