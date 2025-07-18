using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

        private void ParentPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;

            while (source != null && source is not TextBox && source is not Slider)
            {
                source = VisualTreeHelper.GetParent(source);
            }

            if (source == null)
            {
                Keyboard.ClearFocus();
            }
        }
    }
}
