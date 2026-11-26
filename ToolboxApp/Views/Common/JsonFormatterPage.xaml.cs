using System;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ToolboxApp.Utils;

// NOTE: The csproj needs: <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />

using System.Threading.Tasks;

namespace ToolboxApp.Views.Common
{
    public partial class JsonFormatterPage : UserControl
    {
        private int _lastSearchIndex = -1;
        private string _tailData = string.Empty;

        public JsonFormatterPage()
        {
            InitializeComponent();
        }

        private JToken SmartParse(string text, out string tail) => SmartParseCore(text, out tail);

        internal static JToken SmartParseCore(string text, out string tail)
        {
            tail = string.Empty;

            // 1. Try direct parse
            try
            {
                return JToken.Parse(text);
            }
            catch { }

            // 2. Find first { or [ and do bracket matching
            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '{' || text[i] == '[')
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
                throw new FormatException("未找到有效的 JSON 数据");

            char open = text[start];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped) { escaped = false; continue; }
                if (c == '\\' && inString) { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == open) depth++;
                else if (c == close) depth--;

                if (depth == 0)
                {
                    string candidate = text.Substring(start, i - start + 1);
                    tail = text.Substring(i + 1);
                    return JToken.Parse(candidate);
                }
            }

            throw new FormatException("JSON 括号不匹配");
        }

        private void Format_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtInput.Text)) return;
            try
            {
                var token = SmartParse(TxtInput.Text, out _tailData);
                var formatted = token.ToString(Formatting.Indented);
                TxtOutput.Text = string.IsNullOrEmpty(_tailData) ? formatted : formatted + "\n--- 尾部数据 ---\n" + _tailData;
            }
            catch { }
        }

        private void Compress_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtInput.Text)) return;
            try
            {
                var token = SmartParse(TxtInput.Text, out _tailData);
                var compressed = token.ToString(Formatting.None);
                TxtOutput.Text = string.IsNullOrEmpty(_tailData) ? compressed : compressed + _tailData;
            }
            catch { }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            TxtInput.Text = string.Empty;
            TxtOutput.Text = string.Empty;
            TxtSearch.Text = string.Empty;
            LblStatus.Content = string.Empty;
            _tailData = string.Empty;
            _lastSearchIndex = -1;
        }

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            SearchInOutput(forward: true);
        }

        private void FindPrev_Click(object sender, RoutedEventArgs e)
        {
            SearchInOutput(forward: false);
        }

        private void SearchInOutput(bool forward)
        {
            var keyword = TxtSearch.Text;
            if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(TxtOutput.Text))
                return;

            var text = TxtOutput.Text;
            int startPos = forward ? _lastSearchIndex + 1 : Math.Max(_lastSearchIndex - 1, 0);

            int index;
            if (forward)
            {
                index = text.IndexOf(keyword, startPos, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    index = text.IndexOf(keyword, 0, StringComparison.OrdinalIgnoreCase); // wrap
            }
            else
            {
                index = text.LastIndexOf(keyword, Math.Min(startPos, text.Length - 1), StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    index = text.LastIndexOf(keyword, text.Length - 1, StringComparison.OrdinalIgnoreCase); // wrap
            }

            if (index >= 0)
            {
                _lastSearchIndex = index;
                TxtOutput.Focus();
                TxtOutput.Select(index, keyword.Length);
            }
            else
            {
            }
        }

        private async void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtOutput.Text))
            {
                if (!await ClipboardHelper.SetTextAsync(TxtOutput.Text))
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
    }
}
