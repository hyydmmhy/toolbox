using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace ToolboxApp
{
    public partial class MainWindow : Window
    {
        // 页面工厂：按需创建页面
        private readonly Dictionary<string, Func<UserControl>> _pageFactories = new Dictionary<string, Func<UserControl>>();
        // 已创建的页面缓存
        private readonly Dictionary<string, UserControl> _pages = new Dictionary<string, UserControl>();

        // 三个分类的工具列表
        private readonly List<string> _commonTools = new List<string>();
        private readonly List<string> _otherTools = new List<string>();
        private readonly List<string> _systemTools = new List<string>();

        // 记住每个分类的选中索引
        private int _commonLastIndex;
        private int _otherLastIndex;
        private int _systemLastIndex;

        private string _currentCategory = "common";

        // 托盘图标
        private WinForms.NotifyIcon _notifyIcon;
        private WinForms.ToolStripMenuItem _autoStartMenuItem;

        private const string AutoStartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Toolbox";

        private bool _needCenterOnFirstShow = false;

        // 需要在启动后空闲时提前预热的页面（首次打开有明显延迟）
        private static readonly string[] _preheatPages = { };

        public MainWindow()
        {
            InitializeComponent();
            RegisterPages();
            LoadCategory("common");

            // 防止首次最大化时黑色闪烁
            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var hBrush = CreateSolidBrush(0x00F5F5F5); // 与窗口背景色一致
                SetClassLongPtr(hwnd, -10 /* GCL_HBRBACKGROUND */, hBrush);
            };

            // 开机自启时标记首次打开需居中
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--minimized") >= 0)
                _needCenterOnFirstShow = true;

            // 托盘图标 + 预热页面均延迟到 Background 优先级执行：
            // Background < Render，所以 WPF 首帧渲染完成后才运行这里，窗口先流畅显示，
            // 再初始化 WinForms 托盘图标（含 Form.Show/Hide）和预热耗时页面。
            // --minimized 时没有 Show()，Render 队列为空，Background 仍会及时执行，托盘图标正常出现。
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => { InitTrayIcon(); PreheatPages(); }));
        }

        private void PreheatPages()
        {
            foreach (var name in _preheatPages)
            {
                if (!_pages.ContainsKey(name) && _pageFactories.TryGetValue(name, out var factory))
                {
                    try { _pages[name] = factory(); }
                    catch { }
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int crColor);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
        private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private WinForms.ContextMenuStrip _trayMenuStrip;

        /// <summary>
        /// 从程序集内嵌资源加载应用图标（csproj 里通过 <Resource Include="Resources\icons\**" /> 编进 g.resources）。
        /// 兜底：如果内嵌资源找不到（例如图标文件被移除），返回默认应用图标，保证托盘上仍有占位图形。
        /// </summary>
        private static System.Drawing.Icon LoadAppIcon()
        {
            // WPF 把 <Resource> 项编进 g.resources 里的条目「根命名空间小写 + 相对路径」，
            // 例如 Toolbox.Resources.icons.Toolbox.ico 在 g.resources 里实际是 resources/icons/toolbox.ico。
            // GetManifestResourceStream 拿不到这些条目，必须用 ResourceReader 从 g.resources 里读。
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();

                // 先试直接的资源名（万一走了 LogicalName 的路线）
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.IndexOf(".ico", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    using (var stream = asm.GetManifestResourceStream(name))
                    {
                        if (stream == null) continue;
                        try { return new System.Drawing.Icon(stream); } catch { }
                    }
                }

                // 再从每个 .resources 里按 key 找 icon
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (!res.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var rs = asm.GetManifestResourceStream(res))
                    using (var reader = new System.Resources.ResourceReader(rs))
                    {
                        var enumerator = reader.GetEnumerator();
                        while (enumerator.MoveNext())
                        {
                            var key = enumerator.Key as string;
                            if (key == null) continue;
                            if (key.IndexOf(".ico", StringComparison.OrdinalIgnoreCase) < 0) continue;

                            var value = enumerator.Value;
                            Stream source = null;
                            if (value is byte[] bytes)
                                source = new MemoryStream(bytes, false);
                            else if (value is Stream s)
                                source = s;
                            if (source == null) continue;

                            try
                            {
                                // 复制一份脱离 ResourceReader 生命周期
                                using (var ms = new MemoryStream())
                                {
                                    source.CopyTo(ms);
                                    ms.Position = 0;
                                    try { return new System.Drawing.Icon(ms); } catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { /* 内嵌资源读取失败时走兜底 */ }

            try { return System.Drawing.SystemIcons.Application; }
            catch { return null; }
        }

        private void InitTrayIcon()
        {
            _notifyIcon = new WinForms.NotifyIcon();

            // 加载图标：图标已内嵌为程序集资源（csproj 里是 <Resource>），
            // 单文件/多文件发布后输出目录里不再有散文件，只能从程序集里读。
            _notifyIcon.Icon = LoadAppIcon();

            _notifyIcon.Text = "工具箱";
            _notifyIcon.Visible = true;

            // 双击托盘图标显示窗口
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

            // 右键菜单
            _trayMenuStrip = new RoundedContextMenuStrip();
            _trayMenuStrip.BackColor = System.Drawing.Color.FromArgb(252, 252, 252);
            _trayMenuStrip.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            _trayMenuStrip.ShowImageMargin = false;
            _trayMenuStrip.ShowCheckMargin = false;
            _trayMenuStrip.Padding = new WinForms.Padding(2, 6, 2, 6);
            _trayMenuStrip.Renderer = new TrayMenuRenderer();

            var itemPad = new WinForms.Padding(12, 6, 28, 6);
            var showMenuItem = new WinForms.ToolStripMenuItem("       打开界面") { Padding = itemPad };
            showMenuItem.Click += (s, e) => ShowMainWindow();

            _autoStartMenuItem = new WinForms.ToolStripMenuItem() { Padding = itemPad };
            _autoStartMenuItem.Tag = IsAutoStartEnabled();
            UpdateAutoStartText();
            _autoStartMenuItem.Click += OnAutoStartClick;

            var separator = new WinForms.ToolStripSeparator();

            var exitMenuItem = new WinForms.ToolStripMenuItem("       退出程序") { Padding = itemPad };
            exitMenuItem.Click += OnExitClick;

            _trayMenuStrip.Items.Add(showMenuItem);
            _trayMenuStrip.Items.Add(_autoStartMenuItem);
            _trayMenuStrip.Items.Add(separator);
            _trayMenuStrip.Items.Add(exitMenuItem);

            // 隐藏窗口用于托盘菜单焦点管理（解决：位置、自动关闭、任务栏、不拉起主窗口）
            var helperForm = new WinForms.Form
            {
                FormBorderStyle = WinForms.FormBorderStyle.None,
                ShowInTaskbar = false,
                Size = System.Drawing.Size.Empty,
                Opacity = 0
            };
            helperForm.Show();
            helperForm.Hide();

            _notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Right)
                {
                    SetForegroundWindow(helperForm.Handle);
                    var pos = WinForms.Cursor.Position;
                    _trayMenuStrip.Show(pos, WinForms.ToolStripDropDownDirection.AboveRight);
                }
            };
        }

        private void ShowMainWindow()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            if (_needCenterOnFirstShow)
            {
                _needCenterOnFirstShow = false;
                Left = (SystemParameters.WorkArea.Width - Width) / 2;
                Top = (SystemParameters.WorkArea.Height - Height) / 2;
            }
            Activate();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void OnAutoStartClick(object sender, EventArgs e)
        {
            if (IsAutoStartEnabled())
            {
                RemoveAutoStart();
                _autoStartMenuItem.Tag = false;
            }
            else
            {
                SetAutoStart();
                _autoStartMenuItem.Tag = true;
            }
            UpdateAutoStartText();
        }

        private void UpdateAutoStartText()
        {
            bool on = _autoStartMenuItem.Tag is true;
            _autoStartMenuItem.Text = on ? "  ✓   开机自启" : "       开机自启";
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            SysMenu.SysMenuService.Stop();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, false))
                {
                    return key?.GetValue(AppName) != null;
                }
            }
            catch { return false; }
        }

        private void SetAutoStart()
        {
            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                using (var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true))
                {
                    key?.SetValue(AppName, $"\"{exePath}\" --minimized");
                }
            }
            catch { }
        }

        private void RemoveAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true))
                {
                    key?.DeleteValue(AppName, false);
                }
            }
            catch { }
        }

        private void RegisterPages()
        {
            // 定义所有工具页面
            var allTools = new List<(string name, Func<UserControl> factory)>
            {
                ("文件签名检查", () => new Views.Common.SignatureCheckerPage()),
                ("时间戳转换", () => new Views.Common.TimestampPage()),
                ("Unicode 编码转换", () => new Views.Common.UnicodePage()),
                ("查看文件信息", () => new Views.Common.FileInfoPage()),
                ("查看文件属性", () => new Views.Common.FileAttrsPage()),
                ("查看目录大小", () => new Views.Common.DirectorySizePage()),
                ("JSON 格式化", () => new Views.Common.JsonFormatterPage()),
                ("二维码生成器", () => new Views.Common.QrCodePage()),
                ("翻译工具", () => new Views.Other.TranslatePage()),
                ("网络/IP", () => new Views.Other.NetworkPage()),
                ("cURL", () => new Views.Other.CurlPage()),
                ("截图工具", () => new Views.Other.ScreenshotPage()),
                ("OCR识别", () => new Views.Other.OcrPage()),
                ("图片信息查看", () => new Views.Other.ImageDecodeMemoryPage()),
                ("系统菜单", () => new Views.Other.ContextMenuPage()),
                ("日历计算", () => new Views.Other.CalendarPage()),
                ("内存优化", () => new Views.Other.MemoryOptimizePage()),
                ("系统工具", () => new Views.SysTools.SystemToolsPage()),
            };

            // 常用工具列表（与 Python 版一致）
            var commonSet = new HashSet<string>
            {
                "文件签名检查", "时间戳转换", "Unicode 编码转换",
                "查看文件信息", "查看文件属性",
                "查看目录大小", "JSON 格式化", "二维码生成器"
            };

            foreach (var (name, factory) in allTools)
            {
                _pageFactories[name] = factory;

                if (name == "系统工具")
                    _systemTools.Add(name);
                else if (commonSet.Contains(name))
                    _commonTools.Add(name);
                else
                    _otherTools.Add(name);
            }
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string category)
            {
                LoadCategory(category);
            }
        }

        private void LoadCategory(string category)
        {
            _currentCategory = category;
            List<string> tools;
            int lastIndex;

            switch (category)
            {
                case "common":
                    tools = _commonTools;
                    lastIndex = _commonLastIndex;
                    break;
                case "other":
                    tools = _otherTools;
                    lastIndex = _otherLastIndex;
                    break;
                case "system":
                    tools = _systemTools;
                    lastIndex = _systemLastIndex;
                    break;
                default:
                    return;
            }

            NavList.SelectionChanged -= NavList_SelectionChanged;
            NavList.Items.Clear();
            foreach (var name in tools)
            {
                NavList.Items.Add(name);
            }
            NavList.SelectionChanged += NavList_SelectionChanged;

            if (tools.Count > 0)
            {
                NavList.SelectedIndex = Math.Min(lastIndex, tools.Count - 1);
            }
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is string pageName)
            {
                // 记住当前分类的选中索引
                switch (_currentCategory)
                {
                    case "common": _commonLastIndex = NavList.SelectedIndex; break;
                    case "other": _otherLastIndex = NavList.SelectedIndex; break;
                    case "system": _systemLastIndex = NavList.SelectedIndex; break;
                }

                ShowPage(pageName);
            }
        }

        private void ShowPage(string pageName)
        {
            if (!_pages.TryGetValue(pageName, out var page))
            {
                if (_pageFactories.TryGetValue(pageName, out var factory))
                {
                    try
                    {
                        page = factory();
                        _pages[pageName] = page;
                    }
                    catch (Exception ex)
                    {
                        page = new Views.PlaceholderPage($"{pageName} - 加载失败: {ex.Message}");
                    }
                }
            }

            if (page != null)
            {
                ContentArea.Content = page;
            }
        }

        private void BtnTopmost_Click(object sender, RoutedEventArgs e)
        {
            Topmost = BtnTopmost.IsChecked == true;
        }
    }

    /// <summary>
    /// 圆角右键菜单（去掉 WinForms 默认白色直角边框，添加阴影）
    /// </summary>
    internal class RoundedContextMenuStrip : WinForms.ContextMenuStrip
    {
        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int left, right, top, bottom; }

        private const int CS_DROPSHADOW = 0x00020000;
        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMNCRP_ENABLED = 2;

        protected override WinForms.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            var rgn = CreateRoundRectRgn(0, 0, Width, Height, 12, 12);
            SetWindowRgn(Handle, rgn, true);

            // DWM 阴影（四边均匀）
            int policy = DWMNCRP_ENABLED;
            DwmSetWindowAttribute(Handle, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            var margins = new MARGINS { left = 1, right = 1, top = 1, bottom = 1 };
            DwmExtendFrameIntoClientArea(Handle, ref margins);
        }
    }

    /// <summary>
    /// 托盘菜单自定义渲染器 — Win11 圆角风格
    /// </summary>
    internal class TrayMenuRenderer : WinForms.ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new TrayMenuColorTable()) { }

        protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                var rc = new System.Drawing.Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using (var path = RoundedRect(rc, 4))
                using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(240, 240, 240)))
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(brush, path);
                }
            }
        }

        protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
        {
            // 不绘制默认勾选框
        }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            base.OnRenderItemText(e);

            // 如果是勾选的菜单项，在文字左侧绘制 ✓
            if (e.Item is WinForms.ToolStripMenuItem mi && mi.Checked)
            {
                int checkW = 10, checkH = 8;
                int x = 14;
                int y = (e.Item.Height - checkH) / 2;
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0, 120, 212), 1.8f))
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new System.Drawing.PointF(x, y + checkH * 0.5f),
                        new System.Drawing.PointF(x + checkW * 0.35f, y + checkH),
                        new System.Drawing.PointF(x + checkW, y),
                    });
                }
            }
        }

        protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(230, 230, 230)))
                e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
        }

        protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e)
        {
            // 绘制一圈浅灰细边框增加层次感
            var rc = new System.Drawing.Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(210, 210, 210)))
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawRectangle(pen, rc);
            }
        }

        protected override void OnRenderToolStripBackground(WinForms.ToolStripRenderEventArgs e)
        {
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(252, 252, 252)))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(System.Drawing.Rectangle rc, int r)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = r * 2;
            path.AddArc(rc.X, rc.Y, d, d, 180, 90);
            path.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
            path.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            path.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal class TrayMenuColorTable : WinForms.ProfessionalColorTable
    {
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.Transparent;
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.Transparent;
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.Transparent;
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(252, 252, 252);
        public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(252, 252, 252);
        public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(252, 252, 252);
        public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(252, 252, 252);
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(230, 230, 230);
        public override System.Drawing.Color SeparatorLight => System.Drawing.Color.Transparent;
        public override System.Drawing.Color CheckBackground => System.Drawing.Color.Transparent;
        public override System.Drawing.Color CheckSelectedBackground => System.Drawing.Color.Transparent;
        public override System.Drawing.Color CheckPressedBackground => System.Drawing.Color.Transparent;
    }
}
