using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ToolboxApp.Utils;

namespace ToolboxApp.Views.Other
{
    public partial class NetworkPage : UserControl
    {
        public NetworkPage()
        {
            InitializeComponent();
        }

        private static readonly Encoding GBK = Encoding.GetEncoding(936);

        private static readonly string[] VirtualAdapterKeywords = {
            "VMware", "VirtualBox", "Hyper-V", "Docker", "WSL", "vEthernet"
        };

        // 只保留以太网和无线局域网的关键字
        private static readonly string[] AllowedAdapterKeywords = {
            "以太网", "Ethernet", "无线局域网", "Wi-Fi", "WLAN", "Wireless"
        };

        #region Helpers

        private async Task<string> RunCmd(string arguments)
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c chcp 936 >nul 2>&1 & " + arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = GBK,
                    StandardErrorEncoding = GBK
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return output;
                }
            });
        }

        private async Task<string> RunNetsh(string arguments)
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return string.IsNullOrEmpty(error) ? output : output + "\n" + error;
                }
            });
        }

        private string FilterToEthernetAndWifi(string raw)
        {
            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            // 收集各适配器段落
            var sections = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
            System.Collections.Generic.List<string> current = null;
            bool isAllowed = false;

            foreach (var line in lines)
            {
                bool isHeader = line.TrimEnd().EndsWith(":") && !line.StartsWith(" ");
                if (isHeader)
                {
                    isAllowed = AllowedAdapterKeywords.Any(k => line.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                                   && !VirtualAdapterKeywords.Any(k => line.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (isAllowed)
                    {
                        current = new System.Collections.Generic.List<string>();
                        sections.Add(current);
                        current.Add(line);
                    }
                    else
                    {
                        current = null;
                    }
                }
                else if (current != null)
                {
                    current.Add(line);
                }
            }

            // 只输出包含IPv4地址的段落
            var sb = new StringBuilder();
            foreach (var section in sections)
            {
                bool hasIPv4 = section.Any(l => l.Contains("IPv4") || l.Contains("IP Address"));
                if (hasIPv4)
                {
                    foreach (var l in section)
                        sb.AppendLine(l);
                    sb.AppendLine();
                }
            }
            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Tab1: IP配置

        private async void BtnIpConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtIpConfigOutput.Text = "正在获取...";
                var result = await RunCmd("ipconfig");
                TxtIpConfigOutput.Text = FilterToEthernetAndWifi(result);
            }
            catch (Exception ex)
            {
                TxtIpConfigOutput.Text = "错误: " + ex.Message;
            }
        }

        private async void BtnIpConfigAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtIpConfigOutput.Text = "正在获取...";
                var result = await RunCmd("ipconfig /all");
                TxtIpConfigOutput.Text = FilterToEthernetAndWifi(result);
            }
            catch (Exception ex)
            {
                TxtIpConfigOutput.Text = "错误: " + ex.Message;
            }
        }

        private async void BtnCopyIpConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtIpConfigOutput.Text))
            {
                if (!await ClipboardHelper.SetTextAsync(TxtIpConfigOutput.Text))
                {
                    MessageBox.Show("复制失败：剪贴板被其他程序占用，请稍后重试", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                MessageBox.Show("已复制到剪切板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("没有可复制内容", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Tab2: 刷新DNS

        private async void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtFlushDnsOutput.Text = "正在刷新...";
                var result = await RunCmd("ipconfig /flushdns");
                TxtFlushDnsOutput.Text = result;
            }
            catch (Exception ex)
            {
                TxtFlushDnsOutput.Text = "错误: " + ex.Message;
            }
        }

        #endregion

        #region Tab3: IPv4/Long转换

        private void BtnIPv4ToLong_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var parts = TxtIPv4.Text.Trim().Split('.');
                if (parts.Length != 4) { TxtLong.Text = "无效的IPv4地址"; return; }
                var bytes = parts.Select(p => byte.Parse(p)).ToArray();
                long val = ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
                TxtLong.Text = val.ToString();
            }
            catch (Exception ex)
            {
                TxtLong.Text = "错误: " + ex.Message;
            }
        }

        private void BtnLongToIPv4_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                long num = long.Parse(TxtLong.Text.Trim());
                string ip = $"{(num >> 24) & 0xFF}.{(num >> 16) & 0xFF}.{(num >> 8) & 0xFF}.{num & 0xFF}";
                TxtIPv4.Text = ip;
            }
            catch (Exception ex)
            {
                TxtIPv4.Text = "错误: " + ex.Message;
            }
        }

        #endregion

        #region Tab4: 域名转IP

        private async void BtnResolveDomain_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var domain = TxtDomain.Text.Trim();
                if (string.IsNullOrEmpty(domain)) { TxtDomainOutput.Text = "请输入域名"; return; }
                TxtDomainOutput.Text = "正在解析...";
                var addresses = await Task.Run(() => Dns.GetHostAddresses(domain));
                var sb = new StringBuilder();
                sb.AppendLine($"域名: {domain}");
                sb.AppendLine($"共 {addresses.Length} 个地址:");
                foreach (var addr in addresses)
                {
                    sb.AppendLine($"  {addr} ({addr.AddressFamily})");
                }
                TxtDomainOutput.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                TxtDomainOutput.Text = "错误: " + ex.Message;
            }
        }

        #endregion

        #region Tab5: Ping测试

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var host = TxtPingHost.Text.Trim();
                var count = TxtPingCount.Text.Trim();
                if (string.IsNullOrEmpty(host)) { TxtPingOutput.Text = "请输入主机地址"; return; }
                if (string.IsNullOrEmpty(count)) count = "4";

                TxtPingOutput.Text = "";
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c chcp 936 >nul 2>&1 & ping -n {count} {host}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = GBK,
                        StandardErrorEncoding = GBK
                    };
                    using (var p = Process.Start(psi))
                    {
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null)
                        {
                            var l = line;
                            Dispatcher.Invoke(() =>
                            {
                                TxtPingOutput.AppendText(l + Environment.NewLine);
                                TxtPingOutput.ScrollToEnd();
                            });
                        }
                        p.WaitForExit();
                    }
                });
            }
            catch (Exception ex)
            {
                TxtPingOutput.Text = "错误: " + ex.Message;
            }
        }

        #endregion

        #region Tab6: 端口查询

        private async void BtnQueryPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var input = TxtQueryPort.Text.Trim();
                if (string.IsNullOrEmpty(input)) { TxtPortOutput.Text = "请输入端口号"; return; }

                TxtPortOutput.Text = "正在查询...";
                var ports = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();

                var sb = new StringBuilder();
                foreach (var port in ports)
                {
                    sb.AppendLine($"========== 端口 {port} ==========");
                    var netstatResult = await RunCmd($"netstat -ano | findstr :{port}");
                    if (string.IsNullOrWhiteSpace(netstatResult))
                    {
                        sb.AppendLine("  未找到相关连接");
                        sb.AppendLine();
                        continue;
                    }

                    var lines = netstatResult.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 5) { sb.AppendLine("  " + line.Trim()); continue; }

                        var pid = parts[parts.Length - 1];
                        var protocol = parts[0];
                        var localAddr = parts[1];
                        var state = parts.Length >= 5 && parts[0] == "TCP" ? parts[3] : "";

                        string processName = "N/A";
                        try
                        {
                            var tasklistResult = await RunCmd($"tasklist /FI \"PID eq {pid}\" /FO CSV /NH");
                            var csvParts = tasklistResult.Trim().Split(',');
                            if (csvParts.Length > 0)
                                processName = csvParts[0].Trim('"', ' ');
                        }
                        catch { }

                        sb.AppendLine($"  {protocol}  {localAddr}  状态:{state}  PID:{pid}  进程:{processName}");
                    }
                    sb.AppendLine();
                }
                TxtPortOutput.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                TxtPortOutput.Text = "错误: " + ex.Message;
            }
        }

        private async void BtnCopyPortResult_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtPortOutput.Text))
            {
                if (!await ClipboardHelper.SetTextAsync(TxtPortOutput.Text))
                {
                    MessageBox.Show("复制失败：剪贴板被其他程序占用，请稍后重试", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                MessageBox.Show("已复制到剪切板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("没有可复制内容", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Tab7: 防火墙管理

        private string GetFirewallPort()
        {
            return TxtFirewallPort.Text.Trim();
        }

        private async Task RunFirewallCmd(string command)
        {
            try
            {
                var port = GetFirewallPort();
                if (string.IsNullOrEmpty(port)) { TxtFirewallOutput.Text = "请输入端口号"; return; }
                TxtFirewallOutput.Text = "正在执行...";
                var cmd = command.Replace("{port}", port);
                var result = await RunNetsh(cmd);
                TxtFirewallOutput.Text = result;
            }
            catch (Exception ex)
            {
                TxtFirewallOutput.Text = "错误: " + ex.Message;
            }
        }

        private async void BtnFwView_Click(object sender, RoutedEventArgs e)
        {
            await RunFirewallCmd("advfirewall firewall show rule name=\"Open Port {port}\"");
        }

        private async void BtnFwDisable_Click(object sender, RoutedEventArgs e)
        {
            await RunFirewallCmd("advfirewall firewall set rule name=\"Open Port {port}\" new enable=no");
        }

        private async void BtnFwEnable_Click(object sender, RoutedEventArgs e)
        {
            await RunFirewallCmd("advfirewall firewall set rule name=\"Open Port {port}\" new enable=yes");
        }

        private async void BtnFwDelete_Click(object sender, RoutedEventArgs e)
        {
            await RunFirewallCmd("advfirewall firewall delete rule name=\"Open Port {port}\"");
        }

        private async void BtnFwAddAll_Click(object sender, RoutedEventArgs e)
        {
            await RunFirewallCmd("advfirewall firewall add rule name=\"Open Port {port}\" dir=in action=allow protocol=tcp localport={port} profile=any");
        }

        private async void BtnFwAddDomain_Click(object sender, RoutedEventArgs e)
        {
            await RunFirewallCmd("advfirewall firewall add rule name=\"Open Port {port}\" dir=in action=allow protocol=tcp localport={port} profile=domain");
        }

        private async void BtnFwAddPublic_Click(object sender, RoutedEventArgs e)
        {
            await RunFirewallCmd("advfirewall firewall add rule name=\"Open Port {port}\" dir=in action=allow protocol=tcp localport={port} profile=public");
        }

        private async void BtnFwAddPrivate_Click(object sender, RoutedEventArgs e)
        {
            await RunFirewallCmd("advfirewall firewall add rule name=\"Open Port {port}\" dir=in action=allow protocol=tcp localport={port} profile=private");
        }

        #endregion
    }
}
