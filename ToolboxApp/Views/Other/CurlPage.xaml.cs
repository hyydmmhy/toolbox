using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Newtonsoft.Json.Linq;
using ToolboxApp.Utils;

namespace ToolboxApp.Views.Other
{
    public partial class CurlPage : UserControl
    {
        // 二进制响应字节缓存：保存为文件按钮用
        private byte[] _lastBinaryBody;
        private string _lastBinaryCtype;

        public CurlPage()
        {
            InitializeComponent();
            // 粘贴后保持滚动条在左上，避免长 cURL 命令粘进来后跳到最右最下
            DataObject.AddPastingHandler(TxtCommand, OnCommandPaste);
        }

        private void OnCommandPaste(object sender, DataObjectPastingEventArgs e)
        {
            // 取消 WPF 默认粘贴，自己同步插入 + 立刻 ScrollToHome，
            // 全程在一个 dispatcher tick 内完成，渲染时只看到最终状态，无闪烁。
            string text = null;
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
                text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            else if (e.DataObject.GetDataPresent(DataFormats.Text))
                text = e.DataObject.GetData(DataFormats.Text) as string;
            if (text == null) return;

            e.CancelCommand();
            TxtCommand.SelectedText = text;
            // 关键：把光标移回 0，否则 WPF 在本次 tick 结束后会自动把光标（在文本末尾）滚入视图，覆盖 ScrollToHome
            TxtCommand.CaretIndex = 0;
            TxtCommand.ScrollToHome();
        }

        private async void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            var command = TxtCommand.Text.Trim();
            if (string.IsNullOrEmpty(command))
            {
                MessageBox.Show("请输入 cURL 命令", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LblStatus.Text = "解析中...";
            TxtResponse.Text = "";
            _lastBinaryBody = null;
            BtnSaveBody.IsEnabled = false;

            CurlParser.Parsed parsed;
            try
            {
                parsed = CurlParser.Parse(command);
            }
            catch (Exception ex)
            {
                LblStatus.Text = "失败";
                TxtResponse.Text = "解析失败: " + ex.Message;
                return;
            }

            if (ChkInsecureOverride.IsChecked == true) parsed.Insecure = true;

            BtnExecute.IsEnabled = false;
            LblStatus.Text = "执行中...";

            CurlExecutor.Result result;
            try
            {
                result = await CurlExecutor.ExecuteAsync(parsed, CancellationToken.None);
            }
            finally
            {
                BtnExecute.IsEnabled = true;
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                LblStatus.Text = "失败";
                TxtResponse.Text = result.Error;
                return;
            }

            // 组装输出
            bool formatOn = ChkFormat.IsChecked == true;
            bool isText = CurlExecutor.IsTextContentType(result.ContentType);

            var sb = new StringBuilder();
            sb.Append("> ").Append(parsed.Method).Append(' ').AppendLine(parsed.Url);

            if (parsed.Body != null)
            {
                string reqBody = TryFormatJson(parsed.Body, formatOn);
                if (formatOn)
                {
                    sb.AppendLine();
                    sb.AppendLine("请求正文（格式化）：");
                    sb.AppendLine(reqBody);
                }
            }

            sb.AppendLine();
            sb.Append("HTTP ").Append(result.StatusCode).Append(' ').Append(result.StatusText)
              .Append("  (").Append(result.ElapsedMs).AppendLine("ms)");
            foreach (var h in result.ResponseHeaders)
                sb.Append(h.Key).Append(": ").AppendLine(h.Value);
            sb.AppendLine();

            string body;
            if (isText)
            {
                body = (result.ResponseBody?.Length > 0)
                    ? DecodeAsText(result.ResponseBody, result.ContentType)
                    : "";
                if (formatOn)
                {
                    sb.AppendLine("响应（格式化）：");
                }
                body = TryFormatJson(body, formatOn);
            }
            else
            {
                int size = result.ResponseBody?.Length ?? 0;
                body = $"[二进制响应内容已省略：Content-Type={result.ContentType ?? "unknown"}，大小={size} 字节]";
                _lastBinaryBody = result.ResponseBody;
                _lastBinaryCtype = result.ContentType;
                BtnSaveBody.IsEnabled = size > 0;
            }
            sb.Append(body);

            TxtResponse.Text = sb.ToString();
            LblStatus.Text = $"完成 ({result.ElapsedMs}ms)";
        }

        private static string TryFormatJson(string s, bool enable)
        {
            if (!enable || string.IsNullOrEmpty(s)) return s;
            var t = s.TrimStart();
            if (!(t.StartsWith("{") || t.StartsWith("["))) return s;
            try
            {
                return JToken.Parse(s).ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch
            {
                return s;
            }
        }

        private static string DecodeAsText(byte[] bytes, string contentType)
        {
            // 优先按 Content-Type charset 解码，缺省 UTF-8
            try
            {
                if (!string.IsNullOrEmpty(contentType))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        contentType, @"charset\s*=\s*([^;\s""']+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var enc = Encoding.GetEncoding(m.Groups[1].Value);
                        return enc.GetString(bytes);
                    }
                }
            }
            catch { }
            return Encoding.UTF8.GetString(bytes);
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtCommand.Text = "";
            TxtResponse.Text = "";
            LblStatus.Text = "";
            _lastBinaryBody = null;
            BtnSaveBody.IsEnabled = false;
        }

        private async void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtResponse.Text))
            {
                MessageBox.Show("没有可复制内容", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!await ClipboardHelper.SetTextAsync(TxtResponse.Text))
            {
                LblStatus.Text = "复制失败：剪贴板被其他程序占用，请稍后重试";
                return;
            }
            LblStatus.Text = "已复制到剪贴板";
        }

        private void BtnSaveBody_Click(object sender, RoutedEventArgs e)
        {
            if (_lastBinaryBody == null || _lastBinaryBody.Length == 0)
            {
                MessageBox.Show("没有可保存的响应体", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存响应体",
                FileName = "response" + GuessExtension(_lastBinaryCtype),
                Filter = "所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                File.WriteAllBytes(dlg.FileName, _lastBinaryBody);
                LblStatus.Text = "已保存: " + dlg.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GuessExtension(string ctype)
        {
            if (string.IsNullOrEmpty(ctype)) return ".bin";
            var c = ctype.ToLowerInvariant();
            if (c.StartsWith("image/png")) return ".png";
            if (c.StartsWith("image/jpeg") || c.StartsWith("image/jpg")) return ".jpg";
            if (c.StartsWith("image/gif")) return ".gif";
            if (c.StartsWith("image/webp")) return ".webp";
            if (c.StartsWith("image/bmp")) return ".bmp";
            if (c.StartsWith("image/")) return ".img";
            if (c.StartsWith("application/pdf")) return ".pdf";
            if (c.StartsWith("application/zip")) return ".zip";
            if (c.StartsWith("application/octet-stream")) return ".bin";
            if (c.StartsWith("audio/mpeg")) return ".mp3";
            if (c.StartsWith("video/mp4")) return ".mp4";
            return ".bin";
        }
    }
}
