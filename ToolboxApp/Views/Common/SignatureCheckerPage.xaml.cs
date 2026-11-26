using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToolboxApp.Utils;

namespace ToolboxApp.Views.Common
{
    public class SignatureItem
    {
        public string FileName { get; set; }
        public string Signature { get; set; }
        public string Signer { get; set; }
        public string SignDate { get; set; }
        public string FilePath { get; set; }
        public bool IsSigned { get; set; }
    }

    public partial class SignatureCheckerPage : UserControl
    {
        private static readonly HashSet<string> PeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".ocx", ".scr", ".cpl", ".drv", ".efi",
            ".mui", ".msix", ".appx", ".tpi", ".tsp", ".vxd", ".node"
        };

        /// <summary>
        /// 按文件内容检测是否为 PE 文件：MZ 头 + Lfanew 偏移处的 PE\0\0 签名。
        /// 不依赖扩展名，能识别 .node、改名或扩展名缺失的 PE 文件
        /// </summary>
        private static bool IsPeFile(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 64) return false;

                    var header = new byte[64];
                    if (fs.Read(header, 0, 64) < 64) return false;

                    if (header[0] != 'M' || header[1] != 'Z') return false;

                    uint peOffset = BitConverter.ToUInt32(header, 0x3C);
                    if (peOffset == 0 || peOffset + 4 > fs.Length) return false;

                    fs.Seek(peOffset, SeekOrigin.Begin);
                    var peSig = new byte[4];
                    if (fs.Read(peSig, 0, 4) < 4) return false;

                    return peSig[0] == 'P' && peSig[1] == 'E' && peSig[2] == 0 && peSig[3] == 0;
                }
            }
            catch { return false; }
        }

        // 时间戳相关 OID
        private static readonly Oid OidSigningTime = new Oid("1.2.840.113549.1.9.5");
        private static readonly Oid OidRfc3161Timestamp = new Oid("1.2.840.113549.1.9.16.2.14");
        private static readonly Oid OidMsTimestamp = new Oid("1.3.6.1.4.1.311.3.3.1");

        #region Native Interop

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpParameters;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

        // CryptQueryObject 用于从 PE 文件提取嵌入的 PKCS7 数据
        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptQueryObject(
            uint dwObjectType, IntPtr pvObject, uint dwExpectedContentTypeFlags,
            uint dwExpectedFormatTypeFlags, uint dwFlags,
            out uint pdwMsgAndCertEncodingType, out uint pdwContentType,
            out uint pdwFormatType, out IntPtr phCertStore, out IntPtr phMsg, out IntPtr ppvContext);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptMsgGetParam(IntPtr hCryptMsg, uint dwParamType, uint dwIndex, byte[] pvData, ref uint pcbData);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CertCloseStore(IntPtr hCertStore, uint dwFlags);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptMsgClose(IntPtr hCryptMsg);

        private const uint CERT_QUERY_OBJECT_FILE = 1;
        private const uint CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED = 1 << 10;
        private const uint CERT_QUERY_FORMAT_FLAG_BINARY = 1 << 1;
        private const uint CMSG_ENCODED_MESSAGE = 29;

        #endregion

        public SignatureCheckerPage()
        {
            InitializeComponent();
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    PathBox.Text = dialog.SelectedPath;
            }
        }

        private async void ScanBtn_Click(object sender, RoutedEventArgs e)
        {
            var path = PathBox.Text.Trim();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                StatusLabel.Content = "请输入有效的目录路径";
                return;
            }

            ScanBtn.IsEnabled = false;
            StatusLabel.Content = "正在扫描...";

            try
            {
                bool peOnly = PeOnlyCheck.IsChecked == true;
                bool unsignedOnly = UnsignedOnlyCheck.IsChecked == true;

                var items = await Task.Run(() =>
                {
                    var files = EnumerateFilesSafe(path);
                    if (peOnly)
                        files = files.Where(f => PeExtensions.Contains(Path.GetExtension(f)) || IsPeFile(f)).ToList();

                    var results = new List<SignatureItem>();
                    int total = files.Count;
                    int done = 0;

                    foreach (var file in files)
                    {
                        var item = CheckSignature(file);
                        if (!unsignedOnly || !item.IsSigned)
                            results.Add(item);
                        done++;
                        if (done % 50 == 0 || done == total)
                            Dispatcher.Invoke(() => StatusLabel.Content = $"正在扫描 {done}/{total}");
                    }

                    return results.OrderBy(i => i.FileName).ToList();
                });

                ResultGrid.ItemsSource = items;
                StatusLabel.Content = $"扫描完成，共 {items.Count} 个文件";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "扫描失败: " + ex.Message;
            }
            finally
            {
                ScanBtn.IsEnabled = true;
            }
        }

        internal static List<string> EnumerateFilesSafe(string root)
        {
            var result = new List<string>();
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var file in Directory.GetFiles(dir)) result.Add(file);
                    foreach (var sub in Directory.GetDirectories(dir)) stack.Push(sub);
                }
                catch { }
            }
            return result;
        }

        internal static SignatureItem CheckSignature(string filePath)
        {
            var item = new SignatureItem
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                IsSigned = false, Signature = "未签名", Signer = "", SignDate = ""
            };

            try
            {
                byte[] pkcs7Bytes = null;
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                if (ext == ".msix" || ext == ".appx")
                {
                    pkcs7Bytes = ExtractPkcs7FromAppx(filePath);
                }
                else if (IsPeFile(filePath))
                {
                    // 只对按内容判定为 PE 的文件调用 CryptQueryObject，避免对大量非 PE 文件做无谓的 Win32 调用
                    pkcs7Bytes = ExtractPkcs7FromPeViaCrypt32(filePath);
                }
                else
                {
                    pkcs7Bytes = null;
                }

                if (pkcs7Bytes == null || pkcs7Bytes.Length == 0)
                    return item;

                var signedCms = new SignedCms();
                signedCms.Decode(pkcs7Bytes);

                if (signedCms.SignerInfos.Count == 0)
                    return item;

                item.IsSigned = true;
                item.Signature = "已签名";

                var signerInfo = signedCms.SignerInfos[0];

                // 签名者名称
                var cert = signerInfo.Certificate;
                if (cert != null)
                {
                    var cn = ParseCN(cert.Subject);
                    if (!string.IsNullOrEmpty(cn))
                        item.Signer = cn;
                }

                // 签名时间戳
                var ts = ExtractTimestamp(signerInfo);
                if (ts.HasValue)
                    item.SignDate = ts.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { }

            return item;
        }

        /// <summary>
        /// 通过 Crypt32 API 从 PE 文件提取嵌入的 PKCS7 数据
        /// </summary>
        private static byte[] ExtractPkcs7FromPeViaCrypt32(string filePath)
        {
            IntPtr pFile = Marshal.StringToCoTaskMemUni(filePath);
            try
            {
                bool ok = CryptQueryObject(
                    CERT_QUERY_OBJECT_FILE, pFile,
                    CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED,
                    CERT_QUERY_FORMAT_FLAG_BINARY, 0,
                    out _, out _, out _, out IntPtr hStore, out IntPtr hMsg, out _);

                if (!ok) return null;

                try
                {
                    uint cbData = 0;
                    CryptMsgGetParam(hMsg, CMSG_ENCODED_MESSAGE, 0, null, ref cbData);
                    if (cbData == 0) return null;

                    var data = new byte[cbData];
                    if (!CryptMsgGetParam(hMsg, CMSG_ENCODED_MESSAGE, 0, data, ref cbData))
                        return null;

                    return data;
                }
                finally
                {
                    if (hStore != IntPtr.Zero) CertCloseStore(hStore, 0);
                    if (hMsg != IntPtr.Zero) CryptMsgClose(hMsg);
                }
            }
            catch { return null; }
            finally
            {
                Marshal.FreeCoTaskMem(pFile);
            }
        }

        /// <summary>
        /// 从 APPX/MSIX (ZIP) 文件中提取 AppxSignature.p7x 里的 PKCS7 数据
        /// </summary>
        private static byte[] ExtractPkcs7FromAppx(string filePath)
        {
            try
            {
                using (var zip = System.IO.Compression.ZipFile.OpenRead(filePath))
                {
                    var entry = zip.GetEntry("AppxSignature.p7x");
                    if (entry == null) return null;

                    using (var stream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length <= 4) return null;
                        var pkcs7 = new byte[data.Length - 4];
                        Array.Copy(data, 4, pkcs7, 0, pkcs7.Length);
                        return pkcs7;
                    }
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// 从 SignerInfo 提取时间戳
        /// </summary>
        private static DateTime? ExtractTimestamp(SignerInfo signerInfo)
        {
            // 1. 检查 CounterSignerInfos（传统 countersignature）
            foreach (SignerInfo cs in signerInfo.CounterSignerInfos)
            {
                foreach (CryptographicAttributeObject attr in cs.SignedAttributes)
                {
                    if (attr.Oid.Value == OidSigningTime.Value)
                    {
                        foreach (AsnEncodedData val in attr.Values)
                        {
                            var ts = DecodeSigningTime(val.RawData);
                            if (ts.HasValue) return ts;
                        }
                    }
                }
            }

            // 2. 检查 UnsignedAttributes 中的 RFC3161 和 MS 时间戳
            foreach (CryptographicAttributeObject attr in signerInfo.UnsignedAttributes)
            {
                if (attr.Oid.Value == OidRfc3161Timestamp.Value || attr.Oid.Value == OidMsTimestamp.Value)
                {
                    foreach (AsnEncodedData val in attr.Values)
                    {
                        try
                        {
                            var tsCms = new SignedCms();
                            tsCms.Decode(val.RawData);
                            // 从时间戳签名者的 SignedAttributes 中取 signingTime
                            foreach (SignerInfo tsSigner in tsCms.SignerInfos)
                            {
                                foreach (CryptographicAttributeObject tsAttr in tsSigner.SignedAttributes)
                                {
                                    if (tsAttr.Oid.Value == OidSigningTime.Value)
                                    {
                                        foreach (AsnEncodedData tsVal in tsAttr.Values)
                                        {
                                            var ts = DecodeSigningTime(tsVal.RawData);
                                            if (ts.HasValue) return ts;
                                        }
                                    }
                                }
                            }

                            // RFC3161: 从 TSTInfo 内容中提取 genTime
                            if (tsCms.ContentInfo.ContentType.Value == "1.2.840.113549.1.9.16.1.4")
                            {
                                var ts = DecodeTstInfoGenTime(tsCms.ContentInfo.Content);
                                if (ts.HasValue) return ts;
                            }
                        }
                        catch { }
                    }
                }
            }

            // 3. fallback: 签名者自身的 signingTime
            foreach (CryptographicAttributeObject attr in signerInfo.SignedAttributes)
            {
                if (attr.Oid.Value == OidSigningTime.Value)
                {
                    foreach (AsnEncodedData val in attr.Values)
                    {
                        var ts = DecodeSigningTime(val.RawData);
                        if (ts.HasValue) return ts;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 解码 ASN.1 UTCTime / GeneralizedTime (signingTime 属性)
        /// </summary>
        private static DateTime? DecodeSigningTime(byte[] rawData)
        {
            try
            {
                if (rawData == null || rawData.Length < 2) return null;
                int tag = rawData[0];
                int len = rawData[1];
                if (rawData.Length < 2 + len) return null;
                string timeStr = Encoding.ASCII.GetString(rawData, 2, len).TrimEnd('Z', 'z');

                if (tag == 0x17) // UTCTime
                {
                    if (DateTime.TryParseExact(timeStr, new[] { "yyMMddHHmmss", "yyMMddHHmm" },
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                        return dt;
                }
                else if (tag == 0x18) // GeneralizedTime
                {
                    var dotIdx = timeStr.IndexOf('.');
                    if (dotIdx > 0) timeStr = timeStr.Substring(0, dotIdx);
                    if (DateTime.TryParseExact(timeStr, new[] { "yyyyMMddHHmmss", "yyyyMMddHHmm" },
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                        return dt;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 从 TSTInfo (RFC 3161) 的 ASN.1 编码中提取 genTime
        /// </summary>
        private static DateTime? DecodeTstInfoGenTime(byte[] content)
        {
            try
            {
                if (content == null || content.Length < 10) return null;
                // TSTInfo 是一个 SEQUENCE，genTime 是 GeneralizedTime (tag 0x18)
                for (int i = 0; i < content.Length - 2; i++)
                {
                    if (content[i] == 0x18) // GeneralizedTime tag
                    {
                        int len = content[i + 1];
                        if (len > 0 && len < 30 && i + 2 + len <= content.Length)
                        {
                            string timeStr = Encoding.ASCII.GetString(content, i + 2, len).TrimEnd('Z', 'z');
                            var dotIdx = timeStr.IndexOf('.');
                            if (dotIdx > 0) timeStr = timeStr.Substring(0, dotIdx);
                            if (DateTime.TryParseExact(timeStr, new[] { "yyyyMMddHHmmss", "yyyyMMddHHmm" },
                                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                                return dt;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string ParseCN(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return subject;
            // .NET 格式: CN="LunarG, Inc." 或 CN=Microsoft Corporation
            // BouncyCastle 格式: CN=LunarG\, Inc. 或 CN=Microsoft Corporation
            var match = Regex.Match(subject, @"CN=""([^""]+)""");
            if (match.Success) return match.Groups[1].Value.Trim();
            match = Regex.Match(subject, @"CN=((?:[^,\\]|\\.)*)");
            if (!match.Success) return subject;
            return Regex.Replace(match.Groups[1].Value, @"\\(.)", "$1").Trim();
        }

        private void ResultGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<DataGridRow>((DependencyObject)e.OriginalSource) is DataGridRow row)
            {
                row.IsSelected = true;
                ResultGrid.SelectedItem = row.DataContext;
            }
        }

        private void ResultGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (ResultGrid.Items.Count == 0 ||
                !(FindParent<DataGridRow>((DependencyObject)e.OriginalSource)?.DataContext is SignatureItem))
            {
                e.Handled = true;
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && !(child is T))
                child = VisualTreeHelper.GetParent(child);
            return child as T;
        }

        private async void CopyFileName_Click(object sender, RoutedEventArgs e)
        {
            if (ResultGrid.SelectedItem is SignatureItem item)
            {
                if (!await ClipboardHelper.SetTextAsync(item.FileName))
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

        private void FileProperties_Click(object sender, RoutedEventArgs e)
        {
            if (ResultGrid.SelectedItem is SignatureItem item && File.Exists(item.FilePath))
            {
                var info = new SHELLEXECUTEINFO();
                info.cbSize = Marshal.SizeOf(info);
                info.lpVerb = "properties";
                info.lpFile = item.FilePath;
                info.fMask = SEE_MASK_INVOKEIDLIST;
                info.nShow = 1;
                ShellExecuteEx(ref info);
            }
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (ResultGrid.SelectedItem is SignatureItem item && File.Exists(item.FilePath))
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
        }
    }
}
