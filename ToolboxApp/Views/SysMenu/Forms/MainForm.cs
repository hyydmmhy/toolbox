using System;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using SysMenu.Settings;
using SysMenu.Utils;
using SysMenu.Extensions;
using SysMenu.Hooks;
using SysMenu.Native;
using SysMenu.Native.Enums;

namespace SysMenu.Forms
{
    public partial class MainForm : Form
    {
        private static User32.WinEventDelegate _winEventProc;

        private ApplicationSettings _settings;
        private KeyboardHook _keyboardHook;
        private MouseHook _mouseHook;
        private IntPtr _hWinEventHookDestroy;
        private readonly ContextMenuStrip _menu;
        private readonly IDictionary<IntPtr, Window> _windows;

        public MainForm(ApplicationSettings settings, params Window[] windows)
        {
            InitializeComponent();
            _settings = settings;
            _menu = new ContextMenuStrip();
            _windows = new Dictionary<IntPtr, Window>();
            foreach (var window in windows)
            {
                _windows.Add(window.Handle, window);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            Visible = false;
            ShowInTaskbar = false;
            Opacity = 0;

            base.OnLoad(e);

            using var process = Process.GetCurrentProcess();
            using var mainModule = process.MainModule;

            _keyboardHook = new KeyboardHook(_settings, mainModule.ModuleName);
            _keyboardHook.MenuItemHooked += MenuItemHooked;
            _keyboardHook.WindowSizeMenuItemHooked += WindowSizeMenuItemHooked;
            _keyboardHook.EscKeyHooked += EscKeyHooked;
            _keyboardHook.Start();

            _mouseHook = new MouseHook(_settings.Key1, _settings.Key2, _settings.Key3, _settings.Key4, _settings.MouseButton, mainModule.ModuleName);
            _mouseHook.Hooked += MouseHooked;
            _mouseHook.ClickHooked += ClickHooked;
            _mouseHook.Start();

            _winEventProc = new User32.WinEventDelegate(WinEventProc);
            _hWinEventHookDestroy = User32.SetWinEventHook(Constants.EVENT_OBJECT_DESTROY, Constants.EVENT_OBJECT_DESTROY, IntPtr.Zero, _winEventProc, 0, 0, Constants.WINEVENT_OUTOFCONTEXT);

            BringToFront();
            Activate();
        }

        protected override void OnClosed(EventArgs e)
        {
            User32.UnhookWinEvent(_hWinEventHookDestroy);

            foreach (Window window in _windows.Values)
            {
                window.Dispose();
            }

            ContextMenuManager.Release(_menu, MenuItemClick);
            _keyboardHook?.Dispose();
            _mouseHook?.Dispose();

            base.OnClosed(e);
        }

        private void MenuItemHooked(object sender, KeyboardEventArgs e) => Invoke((MethodInvoker)delegate
        {
            var handle = User32.GetForegroundWindow();
            var parentHandle = WindowUtils.GetParentWindow(handle);
            if (parentHandle == IntPtr.Zero || WindowUtils.IsDesktopWindow(parentHandle))
            {
                return;
            }

            ContextMenuManager.Release(_menu, MenuItemClick);
            var manager = new LanguageManager(_settings.LanguageName);
            var window = _windows.ContainsKey(parentHandle) ? _windows[parentHandle] : new Window(parentHandle, manager);
            MenuItemClick(window, e.MenuItem);
            e.Succeeded = true;
        });

        private void WindowSizeMenuItemHooked(object sender, KeyboardEventArgs e) => Invoke((MethodInvoker)delegate
        {
            var handle = User32.GetForegroundWindow();
            var parentHandle = WindowUtils.GetParentWindow(handle);
            if (parentHandle == IntPtr.Zero || WindowUtils.IsDesktopWindow(parentHandle))
            {
                return;
            }

            ContextMenuManager.Release(_menu, MenuItemClick);
            var manager = new LanguageManager(_settings.LanguageName);
            var window = _windows.ContainsKey(parentHandle) ? _windows[parentHandle] : new Window(parentHandle, manager);
            MenuItemClick(window, e.WindowSizeMenuItem);
            e.Succeeded = true;
        });

        private void EscKeyHooked(object sender, KeyboardEventArgs e) => Invoke((MethodInvoker)delegate
        {
            if (_menu.Visible)
            {
                ContextMenuManager.Release(_menu, MenuItemClick);
                e.Succeeded = true;
            }
        });

        private void MouseHooked(object sender, EventArgs e) => BeginInvoke((MethodInvoker)delegate
        {
            var cursorPosition = Cursor.Position;
            var handle = User32.WindowFromPoint(new Native.Structs.Point(cursorPosition.X, cursorPosition.Y));
            var parentHandle = WindowUtils.GetParentWindow(handle);
            if (parentHandle == IntPtr.Zero || handle == _menu.Handle || WindowUtils.IsDesktopWindow(parentHandle))
            {
                return;
            }

            ContextMenuManager.Release(_menu, MenuItemClick);
            var manager = new LanguageManager(_settings.LanguageName);
            var window = _windows.ContainsKey(parentHandle) ? _windows[parentHandle] : new Window(parentHandle, manager);
            ContextMenuManager.Build(_menu, _settings, window, IntPtr.Zero, MenuItemClick);
            User32.SetForegroundWindow(new HandleRef(_menu, _menu.Handle));
            _menu.Show(cursorPosition);
        });

        private void ClickHooked(object sender, EventArgs e) => BeginInvoke((MethodInvoker)delegate
        {
            if (_menu.Visible)
            {
                var cursorPosition = Cursor.Position;
                var isCursorOverMenu = _menu.RectangleToScreen(_menu.ClientRectangle).Contains(cursorPosition);
                var isCursorOverSubMenu = false;
                if (!isCursorOverMenu)
                {
                    foreach (ToolStripItem item in _menu.Items)
                    {
                        if (item is ToolStripMenuItem toolStripMenuItem)
                        {
                            foreach (ToolStripItem subItem in toolStripMenuItem.DropDownItems)
                            {
                                if (subItem.Owner is ToolStripDropDownMenu subMenu && subMenu.Visible)
                                {
                                    isCursorOverSubMenu = subMenu.RectangleToScreen(subMenu.ClientRectangle).Contains(cursorPosition);
                                    if (isCursorOverSubMenu)
                                    {
                                        break;
                                    }
                                }
                            }

                            if (isCursorOverSubMenu)
                            {
                                break;
                            }
                        }
                    }
                }

                if (!isCursorOverMenu && !isCursorOverSubMenu)
                {
                    ContextMenuManager.Release(_menu, MenuItemClick);
                }
            }
        });

        private void MenuItemClick(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem toolStripMenuItem && toolStripMenuItem.Tag is ContextMenuItemValue itemValue)
            {
                if (itemValue.MenuItem != null)
                {
                    MenuItemClick(itemValue.Window, itemValue.MenuItem);
                }

                if (itemValue.WindowSizeMenuItem != null)
                {
                    MenuItemClick(itemValue.Window, itemValue.WindowSizeMenuItem);
                }
            }
        }

        private void MenuItemClick(Window window, Settings.MenuItem menuItem)
        {
            switch (menuItem.Name)
            {
                case MenuItemName.Information:
                    {
                        Task.Factory.StartNew(() =>
                        {
                            var windowDetails = window.GetWindowInfo();
                            BeginInvoke((MethodInvoker)delegate
                            {
                                var manager = new LanguageManager(_settings.LanguageName);
                                var infoForm = new InformationForm(manager, windowDetails);
                                infoForm.Show(window.Win32Window);
                            });
                        });
                    }
                    break;

                case MenuItemName.AlwaysOnTop:
                    {
                        window.MakeAlwaysOnTop(!window.AlwaysOnTop);
                    }
                    break;

                case MenuItemName.AeroGlass:
                    {
                        window.AeroGlass();
                        if (!_windows.ContainsKey(window.Handle))
                        {
                            _windows.Add(window.Handle, window);
                        }
                    }
                    break;

                case MenuItemName.ChangeTitle:
                    {
                        var manager = new LanguageManager(_settings.LanguageName);
                        var titleForm = new TitleForm(manager);
                        titleForm.Title = window.GetWindowText();
                        var result = titleForm.ShowDialog(window.Win32Window);

                        if (result == DialogResult.OK)
                        {
                            window.SetWindowText(titleForm.Title);
                        }
                    }
                    break;

                case MenuItemName.SaveScreenshot:
                    {
                        var manager = new LanguageManager(_settings.LanguageName);
                        var result = WindowUtils.PrintWindow(window.Handle, out var bitmap);
                        if (!result || !WindowUtils.IsCorrectScreenshot(window.Handle, bitmap))
                        {
                            WindowUtils.CaptureWindow(window.Handle, false, out bitmap);
                        }

                        var dialog = new SaveFileDialog
                        {
                            OverwritePrompt = true,
                            ValidateNames = true,
                            Title = manager.GetString("save_screenshot_title"),
                            FileName = manager.GetString("save_screenshot_filename"),
                            DefaultExt = manager.GetString("save_screenshot_default_ext"),
                            RestoreDirectory = false,
                            Filter = manager.GetString("save_screenshot_filter")
                        };

                        if (dialog.ShowDialog(window.Win32Window) == DialogResult.OK)
                        {
                            var fileExtension = Path.GetExtension(dialog.FileName).ToLower();
                            var imageFormat = fileExtension == ".bmp" ? ImageFormat.Bmp :
                                fileExtension == ".gif" ? ImageFormat.Gif :
                                fileExtension == ".jpeg" ? ImageFormat.Jpeg :
                                fileExtension == ".png" ? ImageFormat.Png :
                                fileExtension == ".tiff" ? ImageFormat.Tiff :
                                fileExtension == ".wmf" ? ImageFormat.Wmf : ImageFormat.Bmp;

                            Task.Factory.StartNew(() =>
                            {
                                bitmap.Save(dialog.FileName, imageFormat);
                            });
                        }
                    }
                    break;

                case MenuItemName.OpenFileInExplorer:
                    {
                        Task.Factory.StartNew(() =>
                        {
                            SystemUtils.RunAs("explorer.exe", "/select, " + window.Process.GetMainModuleFileName(), true);
                        });
                    }
                    break;

                case MenuItemName.SizeCustom:
                    {
                        var manager = new LanguageManager(_settings.LanguageName);
                        var sizeForm = new SizeForm(manager, window);
                        var result = sizeForm.ShowDialog(window.Win32Window);
                        if (result == DialogResult.OK)
                        {
                            window.ShowNormal();

                            if (_settings.Sizer == WindowSizerType.WindowWithMargins)
                            {
                                window.SetSize(sizeForm.WindowWidth, sizeForm.WindowHeight, sizeForm.WindowLeft, sizeForm.WindowTop);
                            }
                            else if (_settings.Sizer == WindowSizerType.WindowWithoutMargins)
                            {
                                var margins = window.GetSystemMargins();
                                window.SetSize(sizeForm.WindowWidth == null ? null : (sizeForm.WindowWidth + margins.Left + margins.Right),
                                               sizeForm.WindowHeight == null ? null : (sizeForm.WindowHeight + margins.Top + margins.Bottom),
                                               sizeForm.WindowLeft,
                                               sizeForm.WindowTop);
                            }
                            else
                            {
                                window.SetSize(sizeForm.WindowWidth == null ? null : (sizeForm.WindowWidth + (window.Size.Width - window.ClientSize.Width)),
                                               sizeForm.WindowHeight == null ? null : (sizeForm.WindowHeight + (window.Size.Height - window.ClientSize.Height)),
                                               sizeForm.WindowLeft,
                                               sizeForm.WindowTop);
                            }
                        }
                    }
                    break;

                case MenuItemName.DisableMinimizeButton:
                    {
                        window.DisableMinimizeButton(!window.IsDisabledMinimizeButton);
                    }
                    break;

                case MenuItemName.DisableMaximizeButton:
                    {
                        window.DisableMaximizeButton(!window.IsDisabledMaximizeButton);
                    }
                    break;

                case MenuItemName.DisableCloseButton:
                    {
                        window.DisableCloseButton(!window.IsDisabledCloseButton);
                    }
                    break;
            }
        }

        private void MenuItemClick(Window window, WindowSizeMenuItem menuItem)
        {
            window.ShowNormal();
            if (_settings.Sizer == WindowSizerType.WindowWithMargins)
            {
                window.SetSize(menuItem.Width, menuItem.Height, menuItem.Left, menuItem.Top);
            }
            else if (_settings.Sizer == WindowSizerType.WindowWithoutMargins)
            {
                var margins = window.GetSystemMargins();
                window.SetSize(menuItem.Width == null ? null : (menuItem.Width + margins.Left + margins.Right),
                               menuItem.Height == null ? null : (menuItem.Height + margins.Top + margins.Bottom),
                               menuItem.Left,
                               menuItem.Top);
            }
            else
            {
                window.SetSize(menuItem.Width == null ? null : (menuItem.Width + (window.Size.Width - window.ClientSize.Width)),
                               menuItem.Height == null ? null : (menuItem.Height + (window.Size.Height - window.ClientSize.Height)),
                               menuItem.Left,
                               menuItem.Top);
            }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == Constants.EVENT_OBJECT_DESTROY && idObject == Constants.OBJID_WINDOW)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (_windows.TryGetValue(hwnd, out var window))
                    {
                        window.Dispose();
                        _windows.Remove(hwnd);
                    }
                });
            }
        }
    }
}
