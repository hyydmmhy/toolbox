using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ToolboxApp.Utils;
using ToolboxApp.Views;
using ToolboxApp.Views.Common;
using ToolboxApp.Views.Other;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace ToolboxApp.Cli
{
    internal static class CliHandler
    {
        // ─────────────────────────── 入口 ───────────────────────────

        public static int Run(string[] args)
        {
            // args[0] == "cli"
            string command = args.Length >= 2 ? args[1].ToLowerInvariant() : "help";
            var opts = ParseOpts(args, 2);

            switch (command)
            {
                case "help":              return RunHelp();
                case "file-sign-check":   return RunFileSignCheck(opts);
                case "timestamp":         return RunTimestamp(opts);
                case "unicode":           return RunUnicode(opts);
                case "file-info":         return RunFileInfo(opts);
                case "file-attr":         return RunFileAttr(opts);
                case "dir-size":          return RunDirSize(opts);

                case "json-format":       return RunJsonFormat(opts);
                case "qrcode":            return RunQrCode(opts);
                case "translate":         return RunTranslate(opts);
                case "ocr":               return RunOcr(opts);
                case "image-info":        return RunImageInfo(opts);
                case "firewall":          return RunFirewall(opts);
                case "systool":           return RunSysTool(opts);
                default:
                    Console.WriteLine(FailJson($"未知命令: {command}，使用 'cli help' 查看帮助"));
                    return 1;
            }
        }

        // ──────────────────────────── help ────────────────────────────

        private static int RunHelp()
        {
            Console.WriteLine(@"工具箱 CLI 模式

用法：Toolbox.exe cli <命令> [参数]

命令列表：
  file-sign-check  检查文件数字签名
    --path <路径>           目录或单个文件
    --pe-only               仅扫描 PE 文件（exe/dll/sys 等）
    --unsigned-only         仅输出未签名文件

  timestamp        时间戳与时间互转
    --ts <时间戳>            Unix 时间戳（秒或毫秒）→ 本地时间
    --datetime <时间字符串>  本地时间 → Unix 秒时间戳
                            格式: ""yyyy-MM-dd HH:mm:ss""

  unicode          Unicode / URL 编解码
    --text <内容>
    --direction encode        中文 → \uXXXX
    --direction decode        \uXXXX / &#DDDD; → 中文
    --direction url-encode    URL 编码
    --direction url-decode    URL 解码

  file-info        扫描目录内文件版本信息
    --path <目录>

  file-attr        扫描目录内文件属性与 MD5
    --path <目录>

  dir-size         统计目录各子文件夹大小
    --path <目录>

  json-format      JSON 格式化 / 压缩
    --input <JSON字符串或文件路径>
    --compress               输出压缩格式（默认格式化缩进）

  qrcode           二维码生成 / 识别
    --text <内容> --output <输出文件.png>
    --size <像素>            图片大小（默认 512）
    --decode --path <图片路径>

  translate        翻译文本（微软翻译，免密钥签名通道）
    --text <内容>
    --to <语言代码>          目标语言（如 zh-Hans, en, ja）
    --from <语言代码>        源语言（默认 auto）

  ocr              图片文字识别（本地 PP-OCRv4，OnnxRuntime 推理，无网络依赖）
    --path <图片路径>

  image-info       扫描目录内图片信息（尺寸/大小）
    --path <目录>

  systool          系统工具快捷启动
    --list                   列出所有可用工具名称
    --name <工具名>          启动指定工具（如 cmd, regedit, taskmgr）

  help             显示本帮助

输出格式（JSON）：
  成功：{""success"":true,""data"":...（具体数据）}
  失败：{""success"":false,""error"":""错误信息""}
");
            return 0;
        }

        // ──────────────────────── file-sign-check ─────────────────────

        private static int RunFileSignCheck(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("path", out var path) || string.IsNullOrEmpty(path))
                return Fail("缺少参数 --path");

            bool peOnly       = opts.ContainsKey("pe-only");
            bool unsignedOnly = opts.ContainsKey("unsigned-only");

            try
            {
                List<string> files;
                if (File.Exists(path))
                {
                    files = new List<string> { path };
                }
                else if (Directory.Exists(path))
                {
                    files = SignatureCheckerPage.EnumerateFilesSafe(path);
                    if (peOnly)
                    {
                        var peExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ".exe", ".dll", ".sys", ".ocx", ".scr", ".cpl", ".drv", ".efi",
                            ".mui", ".msix", ".appx", ".tpi", ".tsp", ".vxd"
                        };
                        files = files.Where(f => peExts.Contains(Path.GetExtension(f))).ToList();
                    }
                }
                else
                {
                    return Fail($"路径不存在: {path}");
                }

                var results = files
                    .Select(f => SignatureCheckerPage.CheckSignature(f))
                    .Where(i => !unsignedOnly || !i.IsSigned)
                    .OrderBy(i => i.FileName)
                    .Select(i => new
                    {
                        file_name = i.FileName,
                        file_path = i.FilePath,
                        is_signed = i.IsSigned,
                        signature = i.Signature,
                        signer    = i.Signer,
                        sign_date = i.SignDate
                    })
                    .ToList<object>();

                return Ok(results);
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // ──────────────────────────── timestamp ───────────────────────

        private static int RunTimestamp(Dictionary<string, string> opts)
        {
            try
            {
                if (opts.TryGetValue("ts", out var tsStr))
                {
                    if (!long.TryParse(tsStr, out long ts))
                        return Fail("无效的时间戳，请传入整数");

                    long ms = tsStr.Length <= 10 ? ts * 1000 : ts;
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
                    return Ok(new
                    {
                        input_timestamp = ts,
                        datetime = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                        is_milliseconds = tsStr.Length > 10
                    });
                }

                if (opts.TryGetValue("datetime", out var dtStr))
                {
                    if (!DateTime.TryParseExact(dtStr, "yyyy-MM-dd HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime dt))
                    {
                        return Fail("日期格式错误，需要 yyyy-MM-dd HH:mm:ss");
                    }
                    long unixSec = new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt)).ToUnixTimeSeconds();
                    return Ok(new { input_datetime = dtStr, unix_seconds = unixSec });
                }

                return Fail("需要 --ts <时间戳> 或 --datetime <yyyy-MM-dd HH:mm:ss>");
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // ──────────────────────────── unicode ─────────────────────────

        private static int RunUnicode(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("text", out var text))
                return Fail("缺少参数 --text");
            if (!opts.TryGetValue("direction", out var direction))
                return Fail("缺少参数 --direction (encode | decode | url-encode | url-decode)");

            try
            {
                string result;
                switch (direction.ToLowerInvariant())
                {
                    case "encode":
                        result = UnicodePage.ChineseToUnicode(text);
                        break;
                    case "decode":
                        result = UnicodePage.UnicodeToChinese(text);
                        break;
                    case "url-encode":
                        result = Uri.EscapeDataString(text);
                        break;
                    case "url-decode":
                        result = Uri.UnescapeDataString(text.Replace("+", " "));
                        break;
                    default:
                        return Fail("--direction 取值范围：encode | decode | url-encode | url-decode");
                }
                return Ok(new { input = text, direction, result });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static int RunFileInfo(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("path", out var path) || string.IsNullOrEmpty(path))
                return Fail("缺少参数 --path");
            if (!Directory.Exists(path))
                return Fail($"目录不存在: {path}");

            try
            {
                var items = FileInfoPage.ScanFiles(path);
                var data = items.Select(i => new
                {
                    file_name       = i.FileName,
                    size            = i.RawSize,
                    size_text       = i.SizeText,
                    file_version    = i.FileVersion,
                    product_version = i.ProductVersion,
                    product_name    = i.ProductName,
                    file_desc       = i.FileDescription,
                    company         = i.CompanyName
                }).ToList<object>();
                return Ok(data);
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // ──────────────────────────── file-attr ───────────────────────

        private static int RunFileAttr(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("path", out var path) || string.IsNullOrEmpty(path))
                return Fail("缺少参数 --path");
            if (!Directory.Exists(path))
                return Fail($"目录不存在: {path}");

            try
            {
                var items = FileAttrsPage.ScanFiles(path);
                var data = items.Select(i => new
                {
                    file_name = i.FileName,
                    file_path = i.FilePath,
                    size      = i.Size,
                    md5       = i.MD5
                }).ToList<object>();
                return Ok(data);
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // ──────────────────────────── dir-size ────────────────────────

        private static int RunDirSize(Dictionary<string, string> opts)
        {
            bool builtin = opts.ContainsKey("builtin");

            List<string> dirsToScan;

            if (builtin)
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var builtinDirs = new (Environment.SpecialFolder? folder, string fallback)[]
                {
                    (Environment.SpecialFolder.Desktop,    "Desktop"),
                    (null,                                  "Downloads"),
                    (Environment.SpecialFolder.MyPictures, "Pictures"),
                    (Environment.SpecialFolder.MyDocuments,"Documents"),
                    (Environment.SpecialFolder.MyMusic,    "Music"),
                    (Environment.SpecialFolder.MyVideos,   "Videos"),
                    (Environment.SpecialFolder.Favorites,  "Favorites"),
                    (null, "Links"),
                    (null, "Saved Games"),
                    (null, "Searches"),
                    (null, "Contacts"),
                };
                dirsToScan = new List<string>();
                foreach (var (folder, fallback) in builtinDirs)
                {
                    string p = folder.HasValue ? Environment.GetFolderPath(folder.Value) : null;
                    if (string.IsNullOrEmpty(p))
                        p = Path.Combine(userProfile, fallback);
                    if (Directory.Exists(p))
                        dirsToScan.Add(p);
                }
            }
            else
            {
                if (!opts.TryGetValue("path", out var path) || string.IsNullOrEmpty(path))
                    return Fail("缺少参数 --path（或使用 --builtin 扫描系统内置目录）");
                if (!Directory.Exists(path))
                    return Fail($"目录不存在: {path}");
                if (DirectorySizePage.IsDriveRoot(path))
                    return Fail("不允许扫描驱动器根目录");
                dirsToScan = Directory.GetDirectories(path).ToList();
            }

            try
            {
                var items = dirsToScan
                    .Select(d => DirectorySizePage.ScanDirectory(d))
                    .OrderByDescending(i => i.RawSize)
                    .ToList();

                long totalBytes = items.Sum(i => i.RawSize);

                var data = new
                {
                    total_size_bytes = totalBytes,
                    total_size_text  = DirectorySizePage.FormatSize(totalBytes),
                    dir_count        = items.Count,
                    dirs = items.Select(i => new
                    {
                        name       = i.Name,
                        path       = i.Path,
                        size_bytes = i.RawSize,
                        size_text  = i.Size,
                        file_count = i.FileCount
                    }).ToList<object>()
                };
                return Ok(data);
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // ──────────────────────────── 文本输入解析 ─────────────────────────

        /// <summary>
        /// 优先从 --file 读取文本，其次从 --text 取值。
        /// 返回 null 表示出错，错误信息写入 error。
        /// </summary>
        private static string ResolveTextInput(Dictionary<string, string> opts, out string error)
        {
            error = null;
            if (opts.TryGetValue("file", out var filePath) && !string.IsNullOrEmpty(filePath))
            {
                if (!File.Exists(filePath))
                {
                    error = $"文件不存在: {filePath}";
                    return null;
                }
                try { return File.ReadAllText(filePath, new UTF8Encoding(false)); }
                catch (Exception ex) { error = "读取文件失败: " + ex.Message; return null; }
            }

            if (opts.TryGetValue("text", out var text) && text != null)
                return text;

            error = "缺少参数 --text <内容> 或 --file <文件路径>";
            return null;
        }

        // ──────────────────────────── json-format ─────────────────────

        private static int RunJsonFormat(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("input", out var input) || string.IsNullOrEmpty(input))
                return Fail("缺少参数 --input");

            bool compress = opts.ContainsKey("compress");

            string text = input;
            if (File.Exists(input))
            {
                try { text = File.ReadAllText(input, new UTF8Encoding(false)); }
                catch (Exception ex) { return Fail("读取文件失败: " + ex.Message); }
            }

            string formatted;
            try
            {
                var token = JsonFormatterPage.SmartParseCore(text, out _);
                formatted = compress
                    ? token.ToString(Newtonsoft.Json.Formatting.None)
                    : token.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                return Fail("JSON 解析失败: " + ex.Message);
            }

            // 写入文件
            if (opts.TryGetValue("output", out var outPath) && !string.IsNullOrEmpty(outPath))
            {
                try
                {
                    File.WriteAllText(outPath, formatted, new UTF8Encoding(false));
                    Console.WriteLine(JsonConvert.SerializeObject(
                        new { success = true, data = new { output_file = outPath, length = formatted.Length } },
                        Newtonsoft.Json.Formatting.None));
                    return 0;
                }
                catch (Exception ex) { return Fail("写入文件失败: " + ex.Message); }
            }

            // 直接输出格式化结果，不套 JSON envelope
            Console.WriteLine(formatted);
            return 0;
        }

        // ──────────────────────────── qrcode ──────────────────────────

        private static int RunQrCode(Dictionary<string, string> opts)
        {
            if (opts.ContainsKey("decode"))
            {
                if (!opts.TryGetValue("path", out var imgPath) || !File.Exists(imgPath))
                    return Fail("缺少有效的 --path 图片路径");

                try
                {
                    using (var bmp = new System.Drawing.Bitmap(imgPath))
                    {
                        var reader = new ZXing.BarcodeReader();
                        var result = reader.Decode(bmp);
                        if (result != null)
                            return Ok(result.Text);
                        return Fail("未识别到二维码");
                    }
                }
                catch (Exception ex)
                {
                    return Fail("识别失败: " + ex.Message);
                }
            }

            if (!opts.TryGetValue("text", out var content) || string.IsNullOrEmpty(content))
                return Fail("缺少参数 --text");
            if (!opts.TryGetValue("output", out var outputPath) || string.IsNullOrEmpty(outputPath))
                return Fail("缺少参数 --output");

            int size = 512;
            if (opts.TryGetValue("size", out var sizeStr) && int.TryParse(sizeStr, out int parsedSize) && parsedSize >= 50 && parsedSize <= 4096)
                size = parsedSize;

            try
            {
                var writer = new BarcodeWriterPixelData
                {
                    Format = BarcodeFormat.QR_CODE,
                    Options = new QrCodeEncodingOptions
                    {
                        Width = size,
                        Height = size,
                        ErrorCorrection = ErrorCorrectionLevel.M,
                        CharacterSet = "UTF-8",
                        Margin = 1
                    }
                };

                var pixelData = writer.Write(content);

                using (var bmp = new System.Drawing.Bitmap(pixelData.Width, pixelData.Height,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    var bmpData = bmp.LockBits(
                        new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    Marshal.Copy(pixelData.Pixels, 0, bmpData.Scan0, pixelData.Pixels.Length);
                    bmp.UnlockBits(bmpData);

                    string dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    bmp.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                return Ok(new { output = outputPath, size, content });
            }
            catch (Exception ex)
            {
                return Fail("生成失败: " + ex.Message);
            }
        }

        private static int RunTranslate(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("text", out var text) || string.IsNullOrEmpty(text))
                return Fail("缺少参数 --text");
            if (!opts.TryGetValue("to", out var toCode) || string.IsNullOrEmpty(toCode))
                return Fail("缺少参数 --to <语言代码>");

            var fromCode = opts.TryGetValue("from", out var f) ? f : "auto";

            try
            {
                using (var service = new TranslateService())
                {
                    var options = new TranslateOptions
                    {
                        Engine = TranslateEngine.Microsoft,
                        SourceLanguage = fromCode,
                        TargetLanguage = toCode
                    };

                    var result = service.TranslateAsync(text, options).GetAwaiter().GetResult();

                    return Ok(new
                    {
                        text    = result.Text,
                        engine  = TranslateService.GetEngineDisplayName(result.UsedEngine),
                        engine_code = "microsoft",
                        segments = result.SegmentCount
                    });
                }
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static int RunOcr(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("path", out var imgPath) || !File.Exists(imgPath))
                return Fail("缺少有效的 --path 图片路径");

            try
            {
                // 1. 确保模型已下载（首次约 14 MB，之后直接复用缓存）
                if (!OcrModelManager.IsReady)
                {
                    Console.Error.WriteLine("首次使用，正在下载 OCR 模型（约 14 MB）…");
                    OcrModelManager.EnsureModelsAsync((msg, pct) =>
                    {
                        Console.Error.Write("\r" + msg + (pct >= 0 && pct < 100 ? $" {pct}%" : "") + "   ");
                    }).GetAwaiter().GetResult();
                    Console.Error.WriteLine();
                }

                // 2. 载入图片并跑一次本地 PP-OCRv4 推理
                using (var bitmap = new System.Drawing.Bitmap(imgPath))
                {
                    using (var engine = new PpOcrEngine())
                    {
                        var lines = engine.Recognize(bitmap);
                        var sb = new StringBuilder();
                        foreach (var line in lines)
                            sb.AppendLine(line.Text);

                        var text = sb.ToString().TrimEnd() + Environment.NewLine;
                        return Ok(new
                        {
                            path     = imgPath,
                            lines    = lines.Count,
                            chars    = text.Length,
                            text     = text
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // ──────────────────────────── image-info ──────────────────────

        private static int RunImageInfo(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("path", out var path) || string.IsNullOrEmpty(path))
                return Fail("缺少参数 --path");
            if (!Directory.Exists(path))
                return Fail($"目录不存在: {path}");

            try
            {
                var items = ImageDecodeMemoryPage.ScanDir(path);
                var data = items.Select(i => new
                {
                    file_name     = i.FileName,
                    file_path     = i.FilePath,
                    format        = i.Format,
                    resolution    = i.Resolution,
                    decode_memory = i.DecodeMemory,
                    size          = i.FileSize,
                    modified_time = i.ModifiedTime
                }).ToList<object>();
                return Ok(data);
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // ──────────────────────────── firewall ────────────────────────

        private static int RunFirewall(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("port", out var port) || string.IsNullOrEmpty(port))
                return Fail("缺少参数 --port <端口号>");
            if (!opts.TryGetValue("action", out var action) || string.IsNullOrEmpty(action))
                return Fail("缺少参数 --action <view|add|enable|disable|delete>，add 时可附加 --profile <all|domain|public|private>");

            string netshCmd;
            switch (action.ToLowerInvariant())
            {
                case "view":
                    netshCmd = $"advfirewall firewall show rule name=\"Open Port {port}\"";
                    break;
                case "add":
                    var profile = opts.TryGetValue("profile", out var p) ? p : "any";
                    netshCmd = $"advfirewall firewall add rule name=\"Open Port {port}\" dir=in action=allow protocol=tcp localport={port} profile={profile}";
                    break;
                case "enable":
                    netshCmd = $"advfirewall firewall set rule name=\"Open Port {port}\" new enable=yes";
                    break;
                case "disable":
                    netshCmd = $"advfirewall firewall set rule name=\"Open Port {port}\" new enable=no";
                    break;
                case "delete":
                    netshCmd = $"advfirewall firewall delete rule name=\"Open Port {port}\"";
                    break;
                default:
                    return Fail("--action 取值：view | add | enable | disable | delete");
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = netshCmd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                string output, error;
                using (var proc = Process.Start(psi))
                {
                    output = proc.StandardOutput.ReadToEnd();
                    error  = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                }
                var combined = (output + error).Trim();

                // 直接输出文本，不套 JSON envelope
                Console.WriteLine(combined);
                return 0;
            }
            catch (Exception ex)
            {
                return Fail("执行失败: " + ex.Message);
            }
        }

        // ──────────────────────────── systool ────────────────────────

        private static readonly Dictionary<string, (string program, string args, string mode)> SysTools =
            new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // 常用工具
            { "cmd",           ("cmd.exe",                        "/k cd /d %userprofile%",                   "default") },
            { "powershell",    ("powershell.exe",                 "",                                          "default") },
            { "snipping",      ("SnippingTool.exe",               "",                                          "default") },
            { "regedit",       ("regedit.exe",                    "",                                          "default") },
            { "services",      ("services.msc",                   "/s",                                        "default") },
            { "taskschd",      ("taskschd.msc",                   "/s",                                        "default") },
            { "appwiz",        ("control.exe",                    "appwiz.cpl",                                "default") },
            { "envvars",       ("SystemPropertiesAdvanced.exe",   "",                                          "default") },
            { "reset_explorer",("reset_explorer",                 "",                                          "special") },
            { "kill_explorer", ("kill_explorer",                  "",                                          "special") },
            { "uac",           ("UserAccountControlSettings.exe", "",                                          "default") },
            { "perfmon",       ("perfmon.exe",                    "",                                          "default") },
            { "resmon",        ("resmon.exe",                     "",                                          "default") },
            // 系统管理
            { "calc",          ("calc.exe",                       "",                                          "default") },
            { "taskmgr",       ("taskmgr.exe",                    "",                                          "default") },
            { "pcname",        ("SystemPropertiesComputerName.exe","",                                         "default") },
            { "reboot",        ("reboot",                         "",                                          "special") },
            { "poweroff",      ("poweroff",                       "",                                          "special") },
            { "systeminfo",    ("cmd.exe",                        "/c chcp 936 >nul 2>&1 & systeminfo",       "capture") },
            { "winver",        ("winver.exe",                     "",                                          "default") },
            { "devmgmt",       ("devmgmt.msc",                    "",                                          "default") },
            { "diskmgmt",      ("diskmgmt.msc",                   "",                                          "default") },
            { "datetime",      ("control.exe",                    "date/time",                                 "default") },
            { "wallpaper",     ("ms-settings:personalization-background", "",                                  "mssettings") },
            { "deskicon",      ("rundll32.exe",                   "shell32.dll,Control_RunDLL desk.cpl,,0",   "default") },
            // 网络工具
            { "credential",    ("cmd.exe",                        "/c control /name Microsoft.CredentialManager", "hidden") },
            { "lusrmgr",       ("lusrmgr.msc",                    "",                                          "default") },
            { "eventvwr",      ("eventvwr.msc",                   "",                                          "default") },
            { "gpedit",        ("gpedit.msc",                     "",                                          "default") },
            { "secpol",        ("secpol.msc",                     "",                                          "default") },
            { "perfsettings",  ("SystemPropertiesPerformance.exe","",                                          "default") },
            { "powercfg",      ("control.exe",                    "powercfg.cpl,,3",                           "default") },
            { "certmgr",       ("certmgr.msc",                    "",                                          "default") },
            { "firewall_cpl",  ("control.exe",                    "firewall.cpl",                              "default") },
            { "proxy",         ("rundll32.exe",                   "shell32.dll,Control_RunDLL inetcpl.cpl,,4","default") },
            { "ncpa",          ("control.exe",                    "ncpa.cpl",                                  "default") },
            { "hosts",         ("notepad.exe",                    @"C:\Windows\System32\drivers\etc\hosts",   "default") },
            { "ipv4",          ("cmd.exe",                        "/c chcp 936 >nul 2>&1 & ipconfig|findstr /i ipv4", "capture") },
            { "ipv6",          ("cmd.exe",                        "/c chcp 936 >nul 2>&1 & ipconfig|findstr /i ipv6", "capture") },
            { "route",         ("cmd.exe",                        "/c chcp 936 >nul 2>&1 & route print",     "capture") },
            { "fsmgmt",        ("fsmgmt.msc",                     "",                                          "default") },
        };

        private static int RunSysTool(Dictionary<string, string> opts)
        {
            if (opts.ContainsKey("list"))
            {
                var list = SysTools.Select(kv => new { name = kv.Key, mode = kv.Value.mode }).ToList<object>();
                return Ok(list);
            }

            if (!opts.TryGetValue("name", out var name) || string.IsNullOrEmpty(name))
                return Fail("缺少参数 --name <工具名>，或使用 --list 查看所有工具");

            if (!SysTools.TryGetValue(name, out var tool))
                return Fail($"未知工具: {name}，使用 --list 查看所有工具");

            var (program, args, mode) = tool;

            try
            {
                switch (mode)
                {
                    case "special":
                        switch (program)
                        {
                            case "reset_explorer":
                                RunCmdHidden("taskkill /f /im explorer.exe");
                                System.Threading.Thread.Sleep(500);
                                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                                return Ok(new { tool = name, action = "reset_explorer" });
                            case "kill_explorer":
                                RunCmdHidden("taskkill /f /im explorer.exe");
                                return Ok(new { tool = name, action = "kill_explorer" });
                            case "reboot":
                                RunCmdHidden("shutdown /r /t 0");
                                return Ok(new { tool = name, action = "reboot" });
                            case "poweroff":
                                RunCmdHidden("shutdown /s /t 0");
                                return Ok(new { tool = name, action = "poweroff" });
                        }
                        break;

                    case "capture":
                        var psi = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = args,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.GetEncoding(936)
                        };
                        string output;
                        using (var p = Process.Start(psi))
                        {
                            output = p.StandardOutput.ReadToEnd();
                            p.WaitForExit();
                        }
                        Console.WriteLine(output.Trim());
                        return 0;

                    case "hidden":
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = program,
                            Arguments = args,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            UseShellExecute = true,
                        });
                        return Ok(new { tool = name, launched = true });

                    case "mssettings":
                        Process.Start(program);
                        return Ok(new { tool = name, launched = true });

                    default: // "default"
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = program,
                            UseShellExecute = true,
                        };
                        if (!string.IsNullOrEmpty(args))
                            startInfo.Arguments = args;
                        Process.Start(startInfo);
                        return Ok(new { tool = name, launched = true });
                }
            }
            catch (Exception ex)
            {
                return Fail($"启动失败: {ex.Message}");
            }

            return Fail($"未处理的工具: {name}");
        }

        private static void RunCmdHidden(string command)
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
            });
            p?.WaitForExit();
        }

        private static string RunCmdSync(string arguments, Encoding enc)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c chcp 936 >nul 2>&1 & " + arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = enc,
                StandardErrorEncoding = enc
            };
            using (var p = Process.Start(psi))
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return output;
            }
        }

        // ──────────────────────────── 工具方法 ────────────────────────

        private static (string path, string md5, bool match) DownloadFile(string url, string dirName, string expectedMd5)
        {
            var downloadDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", dirName);
            Directory.CreateDirectory(downloadDir);

            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(fileName)) fileName = "downloaded_file";
            var savePath = Path.Combine(downloadDir, fileName);

            using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            using (var resp = client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                resp.EnsureSuccessStatusCode();
                using (var stream = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var fs = File.Create(savePath))
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        fs.Write(buffer, 0, read);
                }
            }

            using (var md5 = MD5.Create())
            using (var fs = File.OpenRead(savePath))
            {
                var hash = md5.ComputeHash(fs);
                var sb = new StringBuilder(32);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                var fileMd5 = sb.ToString();
                bool match = string.Equals(fileMd5, expectedMd5, StringComparison.OrdinalIgnoreCase);
                return (savePath, fileMd5, match);
            }
        }

        /// <summary>解析 --key value 形式的参数，不带值的 --flag 设为 "true"</summary>
        private static Dictionary<string, string> ParseOpts(string[] args, int startIndex)
        {
            var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = startIndex; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) continue;
                var key = args[i].Substring(2);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    opts[key] = args[++i];
                else
                    opts[key] = "true";
            }
            return opts;
        }

        private static int Ok(object data)
        {
            Console.WriteLine(JsonConvert.SerializeObject(
                new { success = true, data },
                Newtonsoft.Json.Formatting.None));
            return 0;
        }

        private static int Fail(string message)
        {
            Console.WriteLine(FailJson(message));
            return 1;
        }

        public static string FailJson(string message)
        {
            return JsonConvert.SerializeObject(
                new { success = false, error = message },
                Newtonsoft.Json.Formatting.None);
        }

    }
}
