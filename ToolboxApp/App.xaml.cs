using System;
using System.Threading;
using System.Windows;

namespace ToolboxApp
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        private static EventWaitHandle _showEvent;
        private Thread _waitThread;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "ToolboxApp_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                // 通知已运行的实例显示窗口
                try
                {
                    var evt = EventWaitHandle.OpenExisting("ToolboxApp_ShowWindow");
                    evt.Set();
                }
                catch { }
                Shutdown();
                return;
            }

            // 创建事件，等待其他实例的唤起信号
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "ToolboxApp_ShowWindow");
            _waitThread = new Thread(() =>
            {
                while (_showEvent.WaitOne())
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var win = MainWindow;
                        if (win != null)
                        {
                            win.Show();
                            win.WindowState = WindowState.Normal;
                            win.Activate();
                        }
                    }));
                }
            });
            _waitThread.IsBackground = true;
            _waitThread.Start();

            base.OnStartup(e);

            var mainWin = new MainWindow();
            MainWindow = mainWin;

            if (Array.IndexOf(e.Args, "--minimized") < 0)
                mainWin.Show();

            // 系统菜单延迟初始化：BeginInvoke(Background) 使其在首帧渲染后才执行，
            // 消除 Show() 后同步阻塞 UI 线程（创建 WinForms 窗口 + 安装键鼠 Hook）导致的启动卡顿
            if (SysMenu.SysMenuService.LoadEnabledState())
            {
                mainWin.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(SysMenu.SysMenuService.Start));
            }
        }
    }
}
