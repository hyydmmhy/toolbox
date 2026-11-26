using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using Microsoft.Win32;
using ToolboxApp.Utils;

namespace ToolboxApp.Views.Other
{
    public partial class OcrPage : UserControl
    {
        private CancellationTokenSource _cts;
        private PpOcrEngine _engine;
        private readonly object _engineLock = new object();
        private int _lastStatusTick;

        public OcrPage()
        {
            InitializeComponent();
        }

        // ───────────────── 输入 ─────────────────

        private async void BtnFromClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (!System.Windows.Clipboard.ContainsImage())
            {
                MessageBox.Show("剪贴板中没有图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var image = System.Windows.Clipboard.GetImage();
            if (image == null) return;

            try
            {
                TxtResult.Text = await RecognizeAsync(image);
            }
            catch (Exception ex)
            {
                LblStatus.Text = "识别失败: " + ex.Message;
            }
        }

        private async void BtnFromFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp",
                Title = "选择图片"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                TxtResult.Text = await RecognizeAsync(dlg.FileName);
            }
            catch (Exception ex)
            {
                LblStatus.Text = "识别失败: " + ex.Message;
            }
        }

        // ───────────────── 识别 ─────────────────

        /// <summary>识别剪贴板里的 BitmapSource。</summary>
        private async Task<string> RecognizeAsync(System.Windows.Media.Imaging.BitmapSource source)
        {
            _cts = new CancellationTokenSource();
            SetBusy(true);
            LblStatus.Text = "准备中…";
            TxtResult.Text = string.Empty;

            try
            {
                using (var bmp = BitmapSourceToGdi(source))
                {
                    await EnsureModel(_cts.Token);
                    return await RunOnBackground(bmp, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                LblStatus.Text = "已取消";
                return string.Empty;
            }
            finally
            {
                SetBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>识别文件（.jpg / .png / .bmp / .tiff）。</summary>
        private async Task<string> RecognizeAsync(string path)
        {
            _cts = new CancellationTokenSource();
            SetBusy(true);
            LblStatus.Text = "准备中…";
            TxtResult.Text = string.Empty;

            try
            {
                using (var bmp = new Bitmap(path))
                {
                    ApplyExifOrientation(bmp);
                    await EnsureModel(_cts.Token);
                    return await RunOnBackground(bmp, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                LblStatus.Text = "已取消";
                return string.Empty;
            }
            finally
            {
                SetBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>按 EXIF 方向旋转位图（手机竖拍照片，tag 0x0112）。</summary>
        private static void ApplyExifOrientation(Bitmap bmp)
        {
            try
            {
                var tagId = 0x0112;
                var props = bmp.PropertyIdList;
                bool has = false;
                foreach (var p in props) if (p == tagId) { has = true; break; }
                if (!has) return;

                var item = bmp.GetPropertyItem(tagId);
                int orientation = (item?.Value != null && item.Value.Length > 0) ? item.Value[0] : 1;
                switch (orientation)
                {
                    case 2: bmp.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                    case 3: bmp.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                    case 4: bmp.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                    case 5: bmp.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                    case 6: bmp.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                    case 7: bmp.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                    case 8: bmp.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
                }
            }
            catch { /* EXIF 缺失或损坏时忽略 */ }
        }

        private async Task<string> RunOnBackground(Bitmap bitmap, CancellationToken token)
        {
            LblStatus.Text = "正在识别…";
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var engine = GetEngine();
                var lines = engine.Recognize(bitmap);
                token.ThrowIfCancellationRequested();

                var sb = new System.Text.StringBuilder();
                foreach (var line in lines)
                    sb.AppendLine(line.Text);
                return sb.ToString().TrimEnd() + Environment.NewLine;
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// 确保模型已下载。缺失时在后台线程下载并等待，UI 线程不阻塞
        /// （旧实现用 GetResult() 同步等待，网络卡住时整个窗口会卡死 10 分钟）。
        /// </summary>
        private async Task EnsureModel(CancellationToken token)
        {
            if (OcrModelManager.IsReady) return;

            token.ThrowIfCancellationRequested();
            SetStatus("首次使用，正在下载 OCR 模型（约 14 MB）…");

            try
            {
                await OcrModelManager.EnsureModelsAsync(OnModelProgress, token).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                SetStatus("模型下载失败: " + ex.Message);
                throw;
            }

            SetStatus("模型已就绪");
        }

        /// <summary>把下载进度刷到界面，节流 120 ms 一次，避免下载回调刷屏把界面刷卡。</summary>
        private void OnModelProgress(string message, int percent)
        {
            var text = percent >= 0 && percent < 100 ? $"{message}（{percent}%）" : message;

            int now = Environment.TickCount;
            int last = _lastStatusTick;
            if (now - last < 120) return;
            _lastStatusTick = Interlocked.Exchange(ref _lastStatusTick, now);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
            dispatcher.BeginInvoke(new Action(() => LblStatus.Text = text));
        }

        /// <summary>写状态文本（可从后台线程调用，自动切回 UI 线程）。</summary>
        private void SetStatus(string text)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
            dispatcher.BeginInvoke(new Action(() => LblStatus.Text = text));
        }

        private PpOcrEngine GetEngine()
        {
            lock (_engineLock)
            {
                _engine ??= new PpOcrEngine();
                return _engine;
            }
        }

        /// <summary>把 WPF 的 BitmapSource 转成 GDI+ Bitmap（24bpp，白底）。支持 alpha 铺白底，避免透明区域导致识别失败。</summary>
        private static Bitmap BitmapSourceToGdi(BitmapSource src)
        {
            using (var ms = new MemoryStream())
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                encoder.Frames.Add(BitmapFrame.Create(src));
                encoder.Save(ms);

                using (var bmp = new Bitmap(ms))
                {
                    var flat = new Bitmap(bmp.Width, bmp.Height,
                        System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(flat))
                    {
                        g.Clear(System.Drawing.Color.White);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                    }
                    return flat;
                }
            }
        }

        // ───────────────── 状态 / 复制 / 清除 ─────────────────

        private void SetBusy(bool busy)
        {
            BtnFromClipboard.IsEnabled = !busy;
            BtnFromFile.IsEnabled = !busy;
            BtnCancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            BtnCancel.IsEnabled = busy;
        }

        /// <summary>取消当前识别/下载（下载、解压、推理全程可取消，不用等超时）。</summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
            LblStatus.Text = "正在取消…";
        }

        private async void BtnCopyResult_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtResult.Text))
            {
                if (await ClipboardHelper.SetTextAsync(TxtResult.Text))
                    LblStatus.Text = "已复制到剪贴板";
                else
                    LblStatus.Text = "复制失败：剪贴板被其他程序占用";
            }
            else
            {
                LblStatus.Text = "没有可复制内容";
            }
        }

        private void BtnClearModel_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                LblStatus.Text = "正在下载或识别中，请先取消当前任务";
                return;
            }

            var result = MessageBox.Show("确认删除本地 OCR 模型缓存？", "提示",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            OcrModelManager.ClearCache();
            lock (_engineLock)
            {
                _engine?.Dispose();
                _engine = null;
            }
            LblStatus.Text = "已清除模型缓存，下次识别时会重新下载";
        }

        /// <summary>在文件资源管理器中打开模型缓存目录。</summary>
        private void BtnOpenModelDir_Click(object sender, RoutedEventArgs e)
        {
            var dir = OcrModelManager.ModelDirectory;
            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
                var cached = OcrModelManager.IsReady
                    ? $"（模型已就绪，占用 {OcrModelManager.CacheSizeMb:0.0} MB）"
                    : "（模型尚未下载，首次识别时会自动下载约 14 MB）";
                LblStatus.Text = "已打开模型目录 " + dir + " " + cached;
            }
            catch (Exception ex)
            {
                LblStatus.Text = "打开目录失败: " + ex.Message;
            }
        }
    }
}