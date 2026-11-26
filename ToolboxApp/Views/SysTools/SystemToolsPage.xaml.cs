using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ToolboxApp.Views.SysTools
{
    public partial class SystemToolsPage : UserControl
    {
        public SystemToolsPage()
        {
            InitializeComponent();

            PopulatePanel(CommonToolsPanel, GetCommonTools());
            PopulatePanel(SystemManagePanel, GetSystemManageTools());
            PopulatePanel(NetworkToolsPanel, GetNetworkTools());
        }

        #region Tool Definitions

        private enum LaunchMode
        {
            Default,
            MsSettings,
            CmdHidden,
            Special
        }

        private class ToolDef
        {
            public string Name;
            public string Icon;
            public string Program;
            public string Args;
            public LaunchMode Mode;
        }

        private List<ToolDef> GetCommonTools() => new List<ToolDef>
        {
            new ToolDef { Name = "命令行", Icon = "cmd.ico", Program = "cmd.exe", Args = "/k cd /d %userprofile%" },
            new ToolDef { Name = "PowerShell", Icon = "powershell.ico", Program = "powershell.exe" },
            new ToolDef { Name = "截图工具", Icon = "snipping.ico", Program = "SnippingTool.exe" },
            new ToolDef { Name = "注册表", Icon = "regedit.ico", Program = "regedit.exe" },
            new ToolDef { Name = "服务", Icon = "services.ico", Program = "services.msc", Args = "/s" },
            new ToolDef { Name = "任务计划", Icon = "tasksch.ico", Program = "taskschd.msc", Args = "/s" },
            new ToolDef { Name = "程序", Icon = "program.ico", Program = "control.exe", Args = "appwiz.cpl" },
            new ToolDef { Name = "环境变量", Icon = "env.ico", Program = "SystemPropertiesAdvanced.exe" },
            new ToolDef { Name = "重启资源管理器", Icon = "resetexplorer.ico", Program = "reset_explorer", Mode = LaunchMode.Special },
            new ToolDef { Name = "结束资源管理器", Icon = "killexplorer.ico", Program = "kill_explorer", Mode = LaunchMode.Special },
            new ToolDef { Name = "UAC设置", Icon = "uac.ico", Program = "UserAccountControlSettings.exe" },
            new ToolDef { Name = "性能监视器", Icon = "performance.ico", Program = "perfmon.exe" },
            new ToolDef { Name = "资源监视器", Icon = "resmon.ico", Program = "resmon.exe" },
        };

        private List<ToolDef> GetSystemManageTools() => new List<ToolDef>
        {
            new ToolDef { Name = "计算器", Icon = "calc.ico", Program = "calc.exe" },
            new ToolDef { Name = "任务管理器", Icon = "taskmgr.ico", Program = "taskmgr.exe" },
            new ToolDef { Name = "计算机名", Icon = "pcname.ico", Program = "SystemPropertiesComputerName.exe" },
            new ToolDef { Name = "快速重启", Icon = "reboot.ico", Program = "reboot", Mode = LaunchMode.Special },
            new ToolDef { Name = "快速关机", Icon = "poweroff.ico", Program = "poweroff", Mode = LaunchMode.Special },
            new ToolDef { Name = "系统信息", Icon = "systeminfo.ico", Program = "cmd.exe", Args = "/k systeminfo" },
            new ToolDef { Name = "系统版本", Icon = "version.ico", Program = "winver.exe" },
            new ToolDef { Name = "设备管理器", Icon = "devmgr.ico", Program = "devmgmt.msc" },
            new ToolDef { Name = "磁盘管理", Icon = "disks.ico", Program = "diskmgmt.msc" },
            new ToolDef { Name = "日期时间", Icon = "datetime.ico", Program = "control.exe", Args = "date/time" },
            new ToolDef { Name = "桌面壁纸", Icon = "wallpaper.ico", Program = "ms-settings:personalization-background", Mode = LaunchMode.MsSettings },
            new ToolDef { Name = "桌面图标", Icon = "deskicon.ico", Program = "rundll32.exe", Args = "shell32.dll,Control_RunDLL desk.cpl,,0" },
        };

        private List<ToolDef> GetNetworkTools() => new List<ToolDef>
        {
            new ToolDef { Name = "凭据管理器", Icon = "credential.ico", Program = "cmd.exe", Args = "/c control /name Microsoft.CredentialManager", Mode = LaunchMode.CmdHidden },
            new ToolDef { Name = "用户组", Icon = "users.ico", Program = "lusrmgr.msc" },
            new ToolDef { Name = "事件查看器", Icon = "eventlog.ico", Program = "eventvwr.msc" },
            new ToolDef { Name = "组策略", Icon = "gpedit.ico", Program = "gpedit.msc" },
            new ToolDef { Name = "安全策略", Icon = "secpolicy.ico", Program = "secpol.msc" },
            new ToolDef { Name = "性能设置", Icon = "perfsettings.ico", Program = "SystemPropertiesPerformance.exe" },
            new ToolDef { Name = "电源设置", Icon = "powercfg.ico", Program = "control.exe", Args = "powercfg.cpl,,3" },
            new ToolDef { Name = "证书管理器", Icon = "certmgr.ico", Program = "certmgr.msc" },
            new ToolDef { Name = "防火墙", Icon = "firewall.ico", Program = "control.exe", Args = "firewall.cpl" },
            new ToolDef { Name = "代理设置", Icon = "proxy.ico", Program = "rundll32.exe", Args = "shell32.dll,Control_RunDLL inetcpl.cpl,,4" },
            new ToolDef { Name = "网络连接", Icon = "netconnections.ico", Program = "control.exe", Args = "ncpa.cpl" },
            new ToolDef { Name = "Hosts文件", Icon = "hosts.ico", Program = "notepad.exe", Args = @"C:\Windows\System32\drivers\etc\hosts" },
            new ToolDef { Name = "IPv4地址", Icon = "ipv4.ico", Program = "cmd.exe", Args = "/k ipconfig|findstr /i ipv4" },
            new ToolDef { Name = "IPv6地址", Icon = "ipv6.ico", Program = "cmd.exe", Args = "/k ipconfig|findstr /i ipv6" },
            new ToolDef { Name = "路由表", Icon = "route.ico", Program = "cmd.exe", Args = "/k route print" },
            new ToolDef { Name = "共享文件夹", Icon = "share.ico", Program = "fsmgmt.msc" },
        };

        #endregion

        #region UI Generation

        private void PopulatePanel(Panel panel, List<ToolDef> tools)
        {
            foreach (var tool in tools)
            {
                var btn = new Button
                {
                    Content = tool.Name,
                    MinWidth = 90,
                    Margin = new Thickness(4),
                    Tag = tool,
                };
                btn.Click += ToolButton_Click;
                panel.Children.Add(btn);
            }
        }

        #endregion

        #region Launch Logic

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            var tool = (ToolDef)((Button)sender).Tag;
            try
            {
                switch (tool.Mode)
                {
                    case LaunchMode.MsSettings:
                        Process.Start(tool.Program);
                        break;

                    case LaunchMode.CmdHidden:
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = tool.Program,
                            Arguments = tool.Args ?? "",
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            UseShellExecute = true,
                        });
                        break;

                    case LaunchMode.Special:
                        RunSpecial(tool.Program);
                        break;

                    default:
                        LaunchDefault(tool);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LaunchDefault(ToolDef tool)
        {
            var psi = new ProcessStartInfo
            {
                FileName = tool.Program,
                UseShellExecute = true,
            };
            if (!string.IsNullOrEmpty(tool.Args))
                psi.Arguments = tool.Args;
            Process.Start(psi);
        }

        private void RunSpecial(string command)
        {
            switch (command)
            {
                case "reset_explorer":
                    RunCmdWait("taskkill /f /im explorer.exe");
                    Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                    break;

                case "kill_explorer":
                    RunCmdWait("taskkill /f /im explorer.exe");
                    break;

                case "reboot":
                    if (MessageBox.Show("确定要立即重启计算机吗？", "确认重启",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        RunCmd("shutdown /r /t 0");
                    break;

                case "poweroff":
                    if (MessageBox.Show("确定要立即关闭计算机吗？", "确认关机",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        RunCmd("shutdown /s /t 0");
                    break;
            }
        }

        private void RunCmd(string command)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
            });
        }

        private void RunCmdWait(string command)
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
            });
            p?.WaitForExit();
        }

        #endregion
    }
}
