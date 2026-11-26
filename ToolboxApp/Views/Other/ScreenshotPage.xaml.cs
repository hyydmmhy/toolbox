using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

using ToolboxApp.Utils;

namespace ToolboxApp.Views.Other
{
    public partial class ScreenshotPage : UserControl
    {
        private Bitmap _lastShot;

        public ScreenshotPage()
        {
            InitializeComponent();
            Unloaded += (s, e) => DisposeLastShot();
        }

        private async void BtnRegion_Click(object sender, RoutedEventArgs e)
        {
            await CaptureAsync(region: true);
        }

        private async void BtnFullScreen_Click(object sender, RoutedEventArgs e)
        {
            await CaptureAsync(region: false);
        }

        private async Task CaptureAsync(bool region)
        {
            SetBusy(true);
            LblStatus.Text = region ? "请框选截图区域..." : "正在截取全屏...";

            // 让 UI 先刷新状态文字和按钮禁用状态
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);

            Window mainWnd = Window.GetWindow(this);
            bool wasVisible = mainWnd != null && mainWnd.IsVisible;
            bool hide = ChkHideWindow.IsChecked == true && wasVisible;

            if (hide)
            {
                mainWnd.Hide();
                await Task.Delay(200);
            }

            Bitmap shot = null;
            try
            {
                shot = region
                    ? Snapshot.CaptureRegionToClipboard()
                    : Snapshot.CaptureAllScreensToClipboard();
            }
            catch (Exception ex)
            {
                LblStatus.Text = "截图失败: " + ex.Message;
            }
            finally
            {
                if (hide)
                {
                    mainWnd.Show();
                    // Activate() 会触发 Windows 专注助手铃铛，用置顶闪一下代替
                    mainWnd.Topmost = true;
                    mainWnd.Topmost = false;
                }
                SetBusy(false);
            }

            if (shot == null)
            {
                LblStatus.Text = "已取消截图";
                return;
            }

            DisposeLastShot();
            _lastShot = shot;

            // 把 Bitmap 转 BitmapSource 放在后台线程，完后再设预览
            BitmapSource source = null;
            await Task.Run(() => { source = BitmapToBitmapSource(shot); });
            ImgPreview.Source = source;

            LblStatus.Text = $"截图完成 ({shot.Width} × {shot.Height})，已复制到剪贴板";
            BtnCopy.IsEnabled = true;
            BtnSave.IsEnabled = true;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_lastShot == null) return;
            try
            {
                Snapshot.SetClipboardImage(_lastShot);
                LblStatus.Text = "已复制到剪贴板";
            }
            catch (Exception ex)
            {
                LblStatus.Text = "复制失败: " + ex.Message;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_lastShot == null) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存截图",
                FileName = "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp",
                DefaultExt = ".png"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ImageFormat fmt;
                switch (Path.GetExtension(dlg.FileName).ToLowerInvariant())
                {
                    case ".jpg":
                    case ".jpeg": fmt = ImageFormat.Jpeg; break;
                    case ".bmp": fmt = ImageFormat.Bmp; break;
                    default: fmt = ImageFormat.Png; break;
                }
                _lastShot.Save(dlg.FileName, fmt);
                LblStatus.Text = "已保存: " + dlg.FileName;
            }
            catch (Exception ex)
            {
                LblStatus.Text = "保存失败: " + ex.Message;
            }
        }

        private static BitmapSource BitmapToBitmapSource(Bitmap bmp)
        {
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int size = stride * bmp.Height;
                var pixels = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, size);

                var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96,
                    System.Windows.Media.PixelFormats.Bgr32, null, pixels, stride);
                src.Freeze();
                return src;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        private static extern bool NativeDeleteObject(IntPtr hObject);

        private void SetBusy(bool busy)
        {
            BtnRegion.IsEnabled = !busy;
            BtnFullScreen.IsEnabled = !busy;
        }

        private void DisposeLastShot()
        {
            if (_lastShot != null)
            {
                _lastShot.Dispose();
                _lastShot = null;
            }
        }
    }
}
