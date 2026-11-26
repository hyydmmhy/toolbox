using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SysMenu;

namespace ToolboxApp.Views.Other
{
    public partial class ContextMenuPage : UserControl
    {
        public ContextMenuPage()
        {
            InitializeComponent();
            UpdateUI();
        }

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            if (SysMenuService.IsRunning)
            {
                SysMenuService.Stop();
                SysMenuService.SaveEnabledState(false);
            }
            else
            {
                SysMenuService.Start();
                SysMenuService.SaveEnabledState(true);
            }
            UpdateUI();
        }

        private void UpdateUI()
        {
            var running = SysMenuService.IsRunning;
            BtnToggle.Content = running ? "停用" : "启用";
            TxtStatus.Text = running ? "运行中" : "已停止";
            StatusIndicator.Fill = running
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));
        }
    }
}
