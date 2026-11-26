using System.Windows.Controls;

namespace ToolboxApp.Views
{
    public partial class PlaceholderPage : UserControl
    {
        public PlaceholderPage(string title)
        {
            InitializeComponent();
            TitleText.Text = title;
        }
    }
}
