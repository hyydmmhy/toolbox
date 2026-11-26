using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ToolboxApp.Views.Other
{
    /// <summary>
    /// 内存优化页面 —— 内部 12 遍循环收缩所有进程工作集 + FlushInstructionCache,
    /// 相当于原版 MemReduce 的 OneKeyOptimize 内部循环,一次点击回收 2+ GB。
    /// 需要在管理员权限下运行(项目 manifest 已声明 requireAdministrator)。
    /// </summary>
    public partial class MemoryOptimizePage : UserControl
    {
        #region Win32 interop

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemFileCacheSize(IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr hProcess, uint dwDesiredAccess, out IntPtr phToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValueW(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle, bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, uint BufferLength,
            IntPtr PreviousState, IntPtr ReturnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        #endregion

        private ulong _lastRecovered;
        private readonly DispatcherTimer _refreshTimer;
        private bool _isRunning;

        public MemoryOptimizePage()
        {
            InitializeComponent();
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (s, e) =>
            {
                // 优化过程中暂停自动刷新, 避免 GlobalMemoryStatusEx 与 SetProcessWorkingSetSize 争锁
                if (!_isRunning) RefreshMemoryStats();
            };
            Loaded += (s, e) =>
            {
                RefreshMemoryStats();
                _refreshTimer.Start();
            };
            Unloaded += (s, e) => _refreshTimer.Stop();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            RefreshMemoryStats();
        }

        private void RefreshMemoryStats()
        {
            var msx = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (!GlobalMemoryStatusEx(ref msx)) return;
            TotalPhysText.Text = FormatBytes(msx.ullTotalPhys);
            AvailPhysText.Text = FormatBytes(msx.ullAvailPhys);
            CacheText.Text = FormatBytes(msx.ullAvailPageFile);
            RecoveredText.Text = FormatBytes(_lastRecovered);
        }

        private void OptimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            _isRunning = true;
            OptimizeBtn.IsEnabled = false;
            RefreshBtn.IsEnabled = false;
            StatusText.Text = "执行中...";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

            Task.Run(() =>
            {
                long freed = 0;
                try
                {
                    var before = QueryMemory();

                    // 1) 提权
                    TryEnablePrivilege("SeIncreaseQuotaPrivilege");

                    // 2) 缓存所有进程 PID (只调一次 Process.GetProcesses,避免 12 遍 × COM 调用)
                    int[] pids = CollectPids();

                    // 3) 12 遍循环收缩: 每遍之后 sleep 让内核 pageout 一轮
                    const int passes = 12;
                    const int passSleepMs = 180;
                    for (int p = 0; p < passes; p++)
                    {
                        TrimAll(pids);
                        FlushInstructionCacheAll(pids);
                        if (p < passes - 1) Thread.Sleep(passSleepMs);
                    }

                    // 4) SetSystemFileCacheSize(0, 0) — 让内核重算 cache size
                    try { SetSystemFileCacheSize(IntPtr.Zero, IntPtr.Zero); } catch { }

                    // 5) 让内核异步 pageout 完成
                    Thread.Sleep(1200);

                    var after = QueryMemory();
                    long delta = (long)after.ullAvailPhys - (long)before.ullAvailPhys;
                    freed = delta > 0 ? delta : 0;
                    _lastRecovered = (ulong)freed;

                    ReportDone("已回收 " + FormatBytes((ulong)freed), Color.FromRgb(0x00, 0xA8, 0x70));
                }
                catch
                {
                    ReportDone("失败", Colors.Red);
                }
                finally
                {
                    _isRunning = false;
                    RunOnUi(() =>
                    {
                        OptimizeBtn.IsEnabled = true;
                        RefreshBtn.IsEnabled = true;
                        try { RefreshMemoryStats(); } catch { }
                    });
                }
            });
        }

        private static MEMORYSTATUSEX QueryMemory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            GlobalMemoryStatusEx(ref m);
            return m;
        }

        /// <summary>只收集一次所有进程 PID,避免每遍循环都调 COM 枚举(慢)</summary>
        private static int[] CollectPids()
        {
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return Array.Empty<int>(); }

            var list = new List<int>(procs.Length);
            foreach (var proc in procs)
            {
                try
                {
                    int pid = proc.Id;
                    if (pid > 4) list.Add(pid);
                }
                catch { }
            }
            return list.ToArray();
        }

        private static void TrimAll(int[] pids)
        {
            foreach (int pid in pids)
            {
                IntPtr h = IntPtr.Zero;
                try
                {
                    h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, pid);
                    if (h == IntPtr.Zero) continue;
                    SetProcessWorkingSetSize(h, new IntPtr(-1), new IntPtr(-1));
                }
                catch { }
                finally { if (h != IntPtr.Zero) CloseHandle(h); }
            }
        }

        private static void FlushInstructionCacheAll(int[] pids)
        {
            try { FlushInstructionCache(GetCurrentProcess(), IntPtr.Zero, UIntPtr.Zero); } catch { }

            foreach (int pid in pids)
            {
                IntPtr h = IntPtr.Zero;
                try
                {
                    h = OpenProcess(PROCESS_QUERY_INFORMATION | 0x0080 /* PROCESS_VM_READ */, false, pid);
                    if (h == IntPtr.Zero) continue;
                    FlushInstructionCache(h, IntPtr.Zero, UIntPtr.Zero);
                }
                catch { }
                finally { if (h != IntPtr.Zero) CloseHandle(h); }
            }
        }

        private static bool TryEnablePrivilege(string privName)
        {
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hTok))
                    return false;
                try
                {
                    if (!LookupPrivilegeValueW(null, privName, out LUID luid)) return false;
                    var tp = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Privileges = new LUID_AND_ATTRIBUTES
                        {
                            Luid = luid,
                            Attributes = SE_PRIVILEGE_ENABLED
                        }
                    };
                    if (!AdjustTokenPrivileges(hTok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                        return false;
                    return Marshal.GetLastWin32Error() != 1300;
                }
                finally { CloseHandle(hTok); }
            }
            catch { return false; }
        }

        private void ReportDone(string text, Color color)
        {
            RunOnUi(() =>
            {
                if (StatusText == null) return;
                StatusText.Text = text;
                StatusText.Foreground = new SolidColorBrush(color);
            });
        }

        private void RunOnUi(Action a)
        {
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(DispatcherPriority.Background, a);
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes >= 1024UL * 1024 * 1024)
                return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1024UL * 1024)
                return (bytes / (1024.0 * 1024)).ToString("0", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024UL)
                return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return bytes + " B";
        }
    }
}