using System;
using System.Threading;
using System.Threading.Tasks;

namespace ToolboxApp.Utils
{
    internal static class ClipboardHelper
    {
        // 剪贴板 API 需要 STA 线程：Task.Run 用的是 MTA 线程池线程，
        // 直接调用 Clipboard.SetText 必然抛异常，因此必须在专用 STA 线程上执行。
        // 远程软件（如 UU 远程）持有剪贴板时 OpenClipboard 会失败；
        // 在后台 STA 线程尝试，失败立即返回 false，不阻塞也不卡死 UI 线程
        public static Task<bool> SetTextAsync(string text)
        {
            var tcs = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    tcs.TrySetResult(true);
                }
                catch
                {
                    tcs.TrySetResult(false);
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }
    }
}
