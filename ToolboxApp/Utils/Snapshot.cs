using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ToolboxApp.Utils
{
    public static class Snapshot
    {
        public static Bitmap CaptureRegionToClipboard()
        {
            var win = new WpfRegionSelectWindow();
            win.ShowDialog();

            if (!win.IsConfirmed)
                return null;

            var sel = win.SelectionPhysical;
            Bitmap cropped = CaptureRectangle(sel);
            SetClipboardImage(cropped);
            return cropped;
        }

        public static Bitmap CaptureAllScreensToClipboard()
        {
            Bitmap shot = CaptureRectangle(SystemInformation.VirtualScreen);
            SetClipboardImage(shot);
            return shot;
        }

        public static Bitmap CaptureRectangle(Rectangle virtualRect)
        {
            if (virtualRect.Width <= 0 || virtualRect.Height <= 0)
                throw new ArgumentException("Capture rectangle is empty.", "virtualRect");

            Bitmap result = new Bitmap(virtualRect.Width, virtualRect.Height, PixelFormat.Format32bppArgb);
            IntPtr desktopWnd = ScreenshotNativeMethods.GetDesktopWindow();
            IntPtr desktopDc = ScreenshotNativeMethods.GetDC(desktopWnd);
            if (desktopDc == IntPtr.Zero)
            { result.Dispose(); throw new InvalidOperationException("GetDC failed."); }
            try
            {
                using (Graphics g = Graphics.FromImage(result))
                {
                    IntPtr targetDc = g.GetHdc();
                    try
                    {
                        bool ok = ScreenshotNativeMethods.BitBlt(
                            targetDc, 0, 0, virtualRect.Width, virtualRect.Height,
                            desktopDc, virtualRect.X, virtualRect.Y,
                            ScreenshotNativeMethods.SRCCOPY | ScreenshotNativeMethods.CAPTUREBLT);
                        if (!ok) throw new InvalidOperationException("BitBlt failed.");
                    }
                    finally { g.ReleaseHdc(targetDc); }
                }
            }
            finally { ScreenshotNativeMethods.ReleaseDC(desktopWnd, desktopDc); }
            return result;
        }

        public static void SetClipboardImage(Bitmap bitmap)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { Clipboard.SetImage(bitmap); return; }
                catch { System.Threading.Thread.Sleep(80); }
            }
            Clipboard.SetImage(bitmap);
        }
    }
}
