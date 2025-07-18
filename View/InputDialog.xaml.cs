using System.Windows;

namespace YMM4FileExplorer.View
{
    public partial class InputDialog : Window
    {
        public string? ResponseText { get; private set; }

        public InputDialog(string defaultValue = "")
        {
            InitializeComponent();

            ResponseTextBox.Text = defaultValue;
            ResponseTextBox.Focus();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ResponseText = ResponseTextBox.Text;
            this.DialogResult = true;
        }
    }
}
