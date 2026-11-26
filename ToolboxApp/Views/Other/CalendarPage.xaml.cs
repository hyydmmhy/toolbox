using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using tyme.culture;
using tyme.lunar;
using tyme.solar;

namespace ToolboxApp.Views.Other
{
    public partial class CalendarPage : UserControl
    {
        public CalendarPage()
        {
            InitializeComponent();

            var now = DateTime.Now;
            TxtYear.Text = now.Year.ToString();
            TxtMonth.Text = now.Month.ToString();
            TxtDay.Text = now.Day.ToString();
            TxtHour.Text = now.Hour.ToString();
        }

        private void AppendOutput(string text)
        {
            OutputBox.AppendText(text + Environment.NewLine);
            OutputBox.ScrollToEnd();
        }

        private void CalcNowBtn_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Now;
            TxtYear.Text = now.Year.ToString();
            TxtMonth.Text = now.Month.ToString();
            TxtDay.Text = now.Day.ToString();
            TxtHour.Text = now.Hour.ToString();
            CalcBtn_Click(sender, e);
        }

        private void CalcBtn_Click(object sender, RoutedEventArgs e)
        {
            OutputBox.Clear();

            if (!int.TryParse(TxtYear.Text, out int year) ||
                !int.TryParse(TxtMonth.Text, out int month) ||
                !int.TryParse(TxtDay.Text, out int day))
            {
                AppendOutput("错误: 请输入有效的日期");
                return;
            }

            SolarDay solarDay;
            try
            {
                solarDay = SolarDay.FromYmd(year, month, day);
            }
            catch
            {
                AppendOutput("错误: 无效的日期");
                return;
            }

            var lunarDay = solarDay.GetLunarDay();

            AppendOutput($"当前公历时间：{solarDay}");
            AppendOutput($"星期：星期{solarDay.Week.GetName()}");
            AppendOutput($"星座：{solarDay.Constellation.GetName()}座");

            var termDay = solarDay.TermDay;
            if (termDay.DayIndex == 0)
                AppendOutput($"节气：{termDay.SolarTerm}");
            else
                AppendOutput($"节气：无");

            string festival = GetFestival(solarDay, lunarDay);
            AppendOutput($"节日：{festival}");

            AppendOutput($"农历：{lunarDay}");
            AppendOutput($"藏历：{solarDay.GetRabByungDay()}");
            AppendOutput($"回历：{solarDay.GetHijriDay()}");

            string luck = GetDayLuck(lunarDay);
            AppendOutput($"日期吉凶：{luck}");

            var recommends = lunarDay.Recommends.Select(t => t.GetName()).ToList();
            var avoids = lunarDay.Avoids.Select(t => t.GetName()).ToList();
            AppendOutput($"宜：{string.Join("、", recommends)}");
            AppendOutput($"忌：{string.Join("、", avoids)}");

            // 干支年干支月干支日
            var yearCycle = lunarDay.LunarMonth.LunarYear.SixtyCycle;
            var yearZodiac = Zodiac.FromIndex(yearCycle.EarthBranch.Index).GetName();
            var monthCycle = lunarDay.LunarMonth.SixtyCycle;
            var dayCycle = lunarDay.SixtyCycle;
            AppendOutput($"天干地支：{yearCycle.GetName()}{yearZodiac}年 {monthCycle.GetName()}月 {dayCycle.GetName()}日");

            int hour;
            if (!int.TryParse(TxtHour.Text.Trim(), out hour))
                hour = DateTime.Now.Hour;
            hour = Math.Max(0, Math.Min(23, hour));
            string hourLuck = GetHourLuck(lunarDay, hour);
            AppendOutput($"日程时辰吉凶：{hourLuck}");
        }

        private string GetFestival(SolarDay solarDay, LunarDay lunarDay)
        {
            var legal = solarDay.LegalHoliday;
            if (legal != null) return legal.ToString();

            var solarFest = solarDay.Festival;
            if (solarFest != null) return solarFest.ToString();

            var lunarFest = lunarDay.Festival;
            if (lunarFest != null) return lunarFest.ToString();

            return "无";
        }

        private string GetDayLuck(LunarDay lunarDay)
        {
            var dutyName = lunarDay.Duty.GetName();
            string[] dutyGood = { "除", "定", "执", "危", "成", "开" };
            int score = dutyGood.Contains(dutyName) ? 1 : -1;

            var twelveStar = lunarDay.TwelveStar;
            var eclipticLuck = twelveStar.Ecliptic.Luck.GetName();
            score += eclipticLuck == "吉" ? 1 : -1;

            var twentyEightStar = lunarDay.TwentyEightStar;
            var starLuck = twentyEightStar.Luck.GetName();
            score += starLuck == "吉" ? 1 : -1;

            if (score > 0) return "吉";
            if (score < 0) return "凶";
            return "平";
        }

        private string GetHourLuck(LunarDay lunarDay, int hour)
        {
            try
            {
                var lunarHour = LunarHour.FromYmdHms(
                    lunarDay.Year, lunarDay.Month, lunarDay.Day,
                    hour, 0, 0);

                var renName = lunarHour.MinorRen.GetName();
                string[] good = { "大安", "速喜", "小吉" };
                return good.Contains(renName) ? "吉" : "凶";
            }
            catch
            {
                return "未知";
            }
        }

        private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
                if (!char.IsDigit(c)) { e.Handled = true; return; }
        }

        private void RangePreviewTextInput(object sender, TextCompositionEventArgs e, int max)
        {
            foreach (char c in e.Text)
                if (!char.IsDigit(c)) { e.Handled = true; return; }
            var tb = (TextBox)sender;
            string newText = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
            if (int.TryParse(newText, out int val) && val > max)
                e.Handled = true;
        }

        private void ClampTextBox(TextBox tb, int min, int max)
        {
            if (string.IsNullOrEmpty(tb.Text)) return;
            if (!int.TryParse(tb.Text, out int val) || val < min)
                tb.Text = min.ToString();
            else if (val > max)
                tb.Text = max.ToString();
        }

        private void TxtMonth_PreviewTextInput(object sender, TextCompositionEventArgs e) => RangePreviewTextInput(sender, e, 12);
        private void TxtMonth_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtMonth, 1, 12);
        private void TxtDay_PreviewTextInput(object sender, TextCompositionEventArgs e) => RangePreviewTextInput(sender, e, 31);
        private void TxtDay_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtDay, 1, 31);
        private void TxtYear_LostFocus(object sender, RoutedEventArgs e) => ClampTextBox(TxtYear, 1, 9999);
        private void TxtHour_PreviewTextInput(object sender, TextCompositionEventArgs e) => RangePreviewTextInput(sender, e, 23);
        private void TxtHour_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtHour.Text)) return;
            ClampTextBox(TxtHour, 0, 23);
        }
    }
}