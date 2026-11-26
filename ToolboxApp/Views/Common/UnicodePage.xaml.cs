using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace ToolboxApp.Views.Common
{
    public partial class UnicodePage : UserControl
    {
        public UnicodePage()
        {
            InitializeComponent();
            LoadReferenceTable();
        }

        private void LoadReferenceTable()
        {
            RefTable.ItemsSource = new[]
            {
                new { C1 = "空格", E1 = "%20", C2 = "!", E2 = "%21", C3 = "\"", E3 = "%22" },
                new { C1 = "#", E1 = "%23", C2 = "$", E2 = "%24", C3 = "%", E3 = "%25" },
                new { C1 = "&", E1 = "%26", C2 = "'", E2 = "%27", C3 = "(", E3 = "%28" },
                new { C1 = ")", E1 = "%29", C2 = "*", E2 = "%2A", C3 = "+", E3 = "%2B" },
                new { C1 = ",", E1 = "%2C", C2 = "/", E2 = "%2F", C3 = ":", E3 = "%3A" },
                new { C1 = ";", E1 = "%3B", C2 = "=", E2 = "%3D", C3 = "?", E3 = "%3F" },
                new { C1 = "@", E1 = "%40", C2 = "[", E2 = "%5B", C3 = "]", E3 = "%5D" },
            };
        }

        private void UnicodeToChinese_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtInput.Text = UnicodeToChinese(TxtInput.Text);
            }
            catch (Exception ex)
            {
                TxtInput.Text = "错误: " + ex.Message;
            }
        }

        private void ChineseToUnicode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtInput.Text = ChineseToUnicode(TxtInput.Text);
            }
            catch (Exception ex)
            {
                TxtInput.Text = "错误: " + ex.Message;
            }
        }

        private void UrlDecode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = TxtInput.Text.Replace("+", " ");
                TxtInput.Text = Uri.UnescapeDataString(text);
            }
            catch (Exception ex)
            {
                TxtInput.Text = "错误: " + ex.Message;
            }
        }

        private void UrlEncode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtInput.Text = Uri.EscapeDataString(TxtInput.Text);
            }
            catch (Exception ex)
            {
                TxtInput.Text = "错误: " + ex.Message;
            }
        }

        internal static string UnicodeToChinese(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // (a) hex words: "4e2d 6587"
            if (Regex.IsMatch(input.Trim(), @"^[0-9a-fA-F]{4}(\s+[0-9a-fA-F]{4})*$"))
            {
                var words = input.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var sb = new StringBuilder();
                foreach (var w in words)
                    sb.Append(char.ConvertFromUtf32(Convert.ToInt32(w, 16)));
                return sb.ToString();
            }

            var result = input;

            // (b) \uXXXX and \u{XXXXXX}
            result = Regex.Replace(result, @"\\u\{([0-9a-fA-F]+)\}", m =>
                char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));
            result = Regex.Replace(result, @"\\u([0-9a-fA-F]{4})", m =>
                char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));

            // (c) &#DDDD;
            result = Regex.Replace(result, @"&#(\d+);", m =>
                char.ConvertFromUtf32(int.Parse(m.Groups[1].Value)));

            // (d) &#xXXXX;
            result = Regex.Replace(result, @"&#x([0-9a-fA-F]+);", m =>
                char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));

            // (e) try JSON unescape as fallback if still contains backslash-u
            if (result.Contains("\\u"))
            {
                try
                {
                    result = Regex.Unescape(result);
                }
                catch { }
            }

            return result;
        }

        internal static string ChineseToUnicode(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                int cp = char.ConvertToUtf32(input, i);
                if (cp > 127)
                {
                    if (cp > 0xFFFF)
                    {
                        sb.AppendFormat("\\u{{{0:x}}}", cp);
                        i++; // skip surrogate pair
                    }
                    else
                    {
                        sb.AppendFormat("\\u{0:x4}", cp);
                    }
                }
                else
                {
                    sb.Append(input[i]);
                }
            }
            return sb.ToString();
        }
    }
}
