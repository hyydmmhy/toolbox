using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ToolboxApp.Views.Common
{
    public partial class TimestampPage : UserControl
    {
        public TimestampPage()
        {
            InitializeComponent();
            // 默认显示当前时间和时间戳
            FillCurrentTime();
            FillCurrentTimestamp();
        }

        private void FillCurrentTimestamp()
        {
            long seconds = DateTimeOffset.Now.ToUnixTimeSeconds();
            TxtTimestampInput.Text = seconds.ToString();
        }

        private void FillCurrentTime()
        {
            var now = DateTime.Now;
            TxtYear.Text = now.Year.ToString();
            TxtMonth.Text = now.Month.ToString();
            TxtDay.Text = now.Day.ToString();
            TxtHour.Text = now.Hour.ToString();
            TxtMinute.Text = now.Minute.ToString();
            TxtSecond.Text = now.Second.ToString();
            TxtMillisecond.Text = now.Millisecond.ToString();
        }

        // 时间戳 → 时间
        private void BtnToDateTime_Click(object sender, RoutedEventArgs e)
        {
            string input = TxtTimestampInput.Text.Trim();
            if (string.IsNullOrEmpty(input))
            {
                TxtDateTimeOutput.Text = "请输入时间戳";
                return;
            }

            if (!long.TryParse(input, out long ts))
            {
                TxtDateTimeOutput.Text = "无效的时间戳";
                return;
            }

            try
            {
                long ms = input.Length <= 10 ? ts * 1000 : ts;
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
                TxtDateTimeOutput.Text = dt.ToString("yyyy-MM-dd HH:mm:ss");
                TxtDateTimeMsOutput.Text = dt.Millisecond.ToString();
            }
            catch
            {
                TxtDateTimeOutput.Text = "转换失败，时间戳超出范围";
            }
        }

        private void BtnCurrentTimestamp_Click(object sender, RoutedEventArgs e)
        {
            FillCurrentTimestamp();
        }

        // 时间 → 时间戳
        private void BtnToTimestamp_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtYear.Text, out int year) ||
                !int.TryParse(TxtMonth.Text, out int month) ||
                !int.TryParse(TxtDay.Text, out int day) ||
                !int.TryParse(TxtHour.Text, out int hour) ||
                !int.TryParse(TxtMinute.Text, out int minute) ||
                !int.TryParse(TxtSecond.Text, out int second))
            {
                TxtTimestampOutput.Text = "请填写完整的日期时间";
                return;
            }
            int.TryParse(TxtMillisecond.Text, out int millisecond);

            try
            {
                var dt = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Local);
                var dto = new DateTimeOffset(dt);
                long ts = dto.ToUnixTimeSeconds();
                long tsMs = dto.ToUnixTimeMilliseconds();
                TxtTimestampOutput.Text = ts.ToString();
                TxtTimestampMsOutput.Text = tsMs.ToString();
            }
            catch
            {
                TxtTimestampOutput.Text = "无效的日期时间";
            }
        }

        private void BtnCurrentTime_Click(object sender, RoutedEventArgs e)
        {
            FillCurrentTime();
        }

        // 仅允许输入数字
        private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        // 带范围限制的输入拦截：预判输入后的值是否超过最大值
        private void RangePreviewTextInput(object sender, TextCompositionEventArgs e, int max)
        {
            // 先拦截非数字
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }

            // 预判输入后的文本值
            var tb = (TextBox)sender;
            string newText = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
            if (int.TryParse(newText, out int val) && val > max)
            {
                e.Handled = true;
            }
        }

        private void TxtMonth_PreviewTextInput(object sender, TextCompositionEventArgs e) => RangePreviewTextInput(sender, e, 12);
        private void TxtDay_PreviewTextInput(object sender, TextCompositionEventArgs e) => RangePreviewTextInput(sender, e, 31);
        private void TxtHour_PreviewTextInput(object sender, TextCompositionEventArgs e) => RangePreviewTextInput(sender, e, 23);
        private void TxtMinute_PreviewTextInput(object sender, TextCompositionEventArgs e) => RangePreviewTextInput(sender, e, 59);
        private void TxtSecond_PreviewTextInput(object sender, TextCompositionEventArgs e) => RangePreviewTextInput(sender, e, 59);
        private void TxtMillisecond_PreviewTextInput(object sender, TextCompositionEventArgs e) => RangePreviewTextInput(sender, e, 999);

        // 失焦时修正最小值（防止空或0）
        private void ClampTextBox(TextBox tb, int min, int max)
        {
            if (!int.TryParse(tb.Text, out int val) || val < min)
                tb.Text = min.ToString();
            else if (val > max)
                tb.Text = max.ToString();
        }

        private void TxtYear_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtYear, 1970, 2100);
        private void TxtMonth_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtMonth, 1, 12);
        private void TxtDay_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtDay, 1, 31);
        private void TxtHour_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtHour, 0, 23);
        private void TxtMinute_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtMinute, 0, 59);
        private void TxtSecond_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtSecond, 0, 59);
        private void TxtMillisecond_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtMillisecond, 0, 999);
    }
}
