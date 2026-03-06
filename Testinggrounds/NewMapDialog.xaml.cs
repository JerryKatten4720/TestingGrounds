using System.Windows;

namespace IsometricWPF.Dialogs
{
    public partial class NewMapDialog : Window
    {
        public int MapCols { get; private set; }
        public int MapRows { get; private set; }

        public NewMapDialog() => InitializeComponent();

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ColsBox.Text, out int cols) || cols < 4 || cols > 300 ||
                !int.TryParse(RowsBox.Text, out int rows) || rows < 4 || rows > 300)
            {
                ErrorLabel.Text       = "Enter integers between 4 and 300.";
                ErrorLabel.Visibility = Visibility.Visible;
                return;
            }
            MapCols = cols; MapRows = rows; DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
