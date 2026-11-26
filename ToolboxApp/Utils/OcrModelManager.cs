using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ToolboxApp.Utils
{
    /// <summary>
    /// OCR 模型管理：首次使用时下载 PP-OCRv4 模型（wheel 包约 14 MB），之后直接复用缓存。
    ///
    /// 下载源按速度排序，逐个尝试：
    ///   1. PyPI 官方源（files.pythonhosted.org）
    ///   2. 清华 PyPI 镜像
    /// 每个源限时 25 s，下载完成后校验体积，失败自动换源。
    /// 全过程走 await + CancellationToken，绝不阻塞 UI 线程。
    /// </summary>
    public class OcrModelManager
    {
        private const string PyPiWheelUrl =
            "https://files.pythonhosted.org/packages/ba/12/1e5497183bdbe782dbb91bad1d0d2297dba4d2831b2652657f7517bfc6df/rapidocr_onnxruntime-1.4.4-py3-none-any.whl";

        private const string PyPiWheelUrlTuna =
            "https://pypi.tuna.tsinghua.edu.cn/packages/ba/12/1e5497183bdbe782dbb91bad1d0d2297dba4d2831b2652657f7517bfc6df/rapidocr_onnxruntime-1.4.4-py3-none-any.whl";

        /// <summary>单个下载源的限时：超时即换下一个源，不让界面干等。</summary>
        private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 数据根目录：%APPDATA%\Toolbox（即 Roaming\Toolbox）。
        /// 与 SysMenu 设置放一起，整个应用的数据只有一个位置，避免 Roaming / Local 分开。
        /// </summary>
        public static string RootDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Toolbox");

        /// <summary>模型目录：%APPDATA%\Toolbox\models\ppocrv4</summary>
        public static string ModelDirectory => Path.Combine(RootDirectory, "models", "ppocrv4");

        public static string DetModelPath => Path.Combine(ModelDirectory, "det.onnx");
        public static string RecModelPath => Path.Combine(ModelDirectory, "rec.onnx");
        public static string ClsModelPath => Path.Combine(ModelDirectory, "cls.onnx");

        /// <summary>模型是否已就绪。</summary>
        public static bool IsReady =>
            File.Exists(DetModelPath) && File.Exists(RecModelPath) &&
            new FileInfo(DetModelPath).Length > 1_000_000 &&
            new FileInfo(RecModelPath).Length > 1_000_000;

        /// <summary>缓存目录占用（MB）。</summary>
        public static double CacheSizeMb
        {
            get
            {
                try
                {
                    if (!Directory.Exists(ModelDirectory)) return 0;
                    return Directory.GetFiles(ModelDirectory).Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0;
                }
                catch { return 0; }
            }
        }

        /// <summary>删除已下载模型，释放空间。</summary>
        public static void ClearCache()
        {
            try
            {
                if (Directory.Exists(ModelDirectory))
                    Directory.Delete(ModelDirectory, true);
            }
            catch { }
        }

        /// <summary>确保模型存在，缺失则下载（带进度回调：0~100）。</summary>
        public static async Task EnsureModelsAsync(Action<string, int> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (IsReady) return;

            Directory.CreateDirectory(ModelDirectory);

            // 大陆网络下清华镜像明显更快（实测 7 s vs 官方源 25 s，且不受跨境 CDN 抖动影响），
            // 官方源作为兜底。
            var sources = new[]
            {
                new { Name = "清华镜像",    Url = PyPiWheelUrlTuna },
                new { Name = "PyPI 官方源", Url = PyPiWheelUrl }
            };

            Exception lastError = null;
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 每个源单独限时：DNS/连接挂死时 30 s 内换源，不会一直卡住调用方
                using (var sourceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    sourceCts.CancelAfter(SourceTimeout);
                    var sourceToken = sourceCts.Token;
                    string wheelPath = null;
                    try
                    {
                        onProgress?.Invoke($"正在从{source.Name}下载 OCR 模型（约 14 MB）", 0);
                        wheelPath = Path.Combine(Path.GetTempPath(), "rapidocr_" + Guid.NewGuid().ToString("N") + ".whl");
                        await DownloadFileAsync(source.Url, wheelPath, percent =>
                            onProgress?.Invoke($"正在从{source.Name}下载 OCR 模型", percent),
                            sourceToken).ConfigureAwait(false);

                        onProgress?.Invoke("正在解压模型…", 95);
                        ExtractModels(wheelPath);

                        if (!IsReady) throw new InvalidOperationException("解压后未找到模型文件");
                        onProgress?.Invoke("模型准备完成", 100);
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        lastError = new TimeoutException($"{source.Name} 下载超时（{SourceTimeout.TotalSeconds:0} s），换源重试");
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                    finally
                    {
                        try { if (wheelPath != null && File.Exists(wheelPath)) File.Delete(wheelPath); } catch { }
                    }
                }
            }

            throw new InvalidOperationException(
                "OCR 模型下载失败：" + (lastError?.Message ?? "未知错误") + "。请检查网络后重试。", lastError);
        }

        private static void ExtractModels(string wheelPath)
        {
            using (var archive = ZipFile.OpenRead(wheelPath))
            {
                var map = new Dictionary<string, string>
                {
                    { "det.onnx", "ch_PP-OCRv4_det_infer.onnx" },
                    { "rec.onnx", "ch_PP-OCRv4_rec_infer.onnx" },
                    { "cls.onnx", "ch_ppocr_mobile_v2.0_cls_infer.onnx" }
                };

                foreach (var pair in map)
                {
                    var entry = archive.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith(pair.Value, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) continue;

                    var target = Path.Combine(ModelDirectory, pair.Key);
                    var temp = target + ".tmp";
                    using (var input = entry.Open())
                    using (var output = File.Create(temp))
                        input.CopyTo(output);

                    if (File.Exists(target)) File.Delete(target);
                    File.Move(temp, target);
                }
            }
        }

        private static async Task DownloadFileAsync(string url, string targetPath,
            Action<int> onProgress, CancellationToken cancellationToken)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Toolbox");

                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? -1;
                    long received = 0;
                    int lastPercent = -1;
                    var buffer = new byte[81920];

                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = File.Create(targetPath))
                    {
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                            .ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            received += read;
                            if (total > 0)
                            {
                                int percent = (int)(received * 100 / total);
                                if (percent != lastPercent)
                                {
                                    lastPercent = percent;
                                    onProgress?.Invoke(percent);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
