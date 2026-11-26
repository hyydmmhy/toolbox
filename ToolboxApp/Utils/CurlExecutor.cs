using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ToolboxApp.Utils
{
    /// <summary>
    /// 执行已解析的 cURL 请求，返回响应数据 + 关键元信息。
    /// 桌面环境直接用 HttpClient，无需任何代理。
    /// </summary>
    public static class CurlExecutor
    {
        public class Result
        {
            public int StatusCode;
            public string StatusText;
            public List<KeyValuePair<string, string>> ResponseHeaders = new List<KeyValuePair<string, string>>();
            public byte[] ResponseBody;
            public string ContentType;
            public long ElapsedMs;
            public string Error;     // 网络层错误（非 HTTP 错误状态）
        }

        // 缓存两个 HttpClient：默认 + 跳过 SSL，避免每次都重建 socket 池
        private static HttpClient _normalClient;
        private static HttpClient _insecureClient;
        private static readonly object _lock = new object();

        private static HttpClient GetClient(bool insecure)
        {
            lock (_lock)
            {
                if (insecure)
                {
                    if (_insecureClient == null)
                    {
                        var handler = new HttpClientHandler
                        {
                            AllowAutoRedirect = true,
                            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                            ServerCertificateCustomValidationCallback = (m, c, ch, e) => true,
                        };
                        _insecureClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
                    }
                    return _insecureClient;
                }
                if (_normalClient == null)
                {
                    var handler = new HttpClientHandler
                    {
                        AllowAutoRedirect = true,
                        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                    };
                    _normalClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
                }
                return _normalClient;
            }
        }

        public static async Task<Result> ExecuteAsync(CurlParser.Parsed parsed, CancellationToken ct = default)
        {
            var result = new Result();
            HttpRequestMessage req = null;
            try
            {
                req = new HttpRequestMessage(new HttpMethod(parsed.Method), parsed.Url);

                if (parsed.FormParts != null && parsed.FormParts.Count > 0)
                {
                    var (bodyBytes, contentType) = BuildMultipartBody(parsed.FormParts);
                    var byteContent = new ByteArrayContent(bodyBytes);
                    byteContent.Headers.TryAddWithoutValidation("Content-Type", contentType);
                    req.Content = byteContent;
                }
                else if (parsed.Body != null)
                {
                    var content = new StringContent(parsed.Body, Encoding.UTF8);
                    // Content-Type 必须设在 Content.Headers 上
                    if (parsed.Headers.TryGetValue("Content-Type", out var ctype)
                        || parsed.Headers.TryGetValue("content-type", out ctype))
                    {
                        try { content.Headers.ContentType = MediaTypeHeaderValue.Parse(ctype); }
                        catch { content.Headers.TryAddWithoutValidation("Content-Type", ctype); }
                    }
                    req.Content = content;
                }

                // 其余请求头
                foreach (var kv in parsed.Headers)
                {
                    var kl = kv.Key.ToLowerInvariant();
                    if (kl == "content-type") continue;  // 已设到 Content
                    if (kl == "content-length") continue;
                    // multipart 时不能手动覆盖 Content-Type（会破坏 boundary）
                    if (kl == "content-type" && req.Content is MultipartFormDataContent) continue;
                    // TryAddWithoutValidation 跳过 .NET 对 User-Agent / Referer 等的严格校验
                    if (!req.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                    {
                        req.Content?.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }

                var client = GetClient(parsed.Insecure);
                var sw = Stopwatch.StartNew();
                using (var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct))
                {
                    sw.Stop();
                    result.ElapsedMs = sw.ElapsedMilliseconds;
                    result.StatusCode = (int)resp.StatusCode;
                    result.StatusText = resp.ReasonPhrase ?? "";
                    result.ResponseBody = await resp.Content.ReadAsByteArrayAsync();

                    foreach (var h in resp.Headers)
                        foreach (var v in h.Value)
                            result.ResponseHeaders.Add(new KeyValuePair<string, string>(h.Key, v));
                    foreach (var h in resp.Content.Headers)
                        foreach (var v in h.Value)
                            result.ResponseHeaders.Add(new KeyValuePair<string, string>(h.Key, v));

                    result.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "";
                }
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                result.Error = "请求超时: " + ex.Message;
            }
            catch (Exception ex)
            {
                result.Error = "请求失败: " + ex.Message;
                if (ex.InnerException != null) result.Error += " (" + ex.InnerException.Message + ")";
            }
            finally
            {
                req?.Dispose();
            }
            return result;
        }

        /// <summary>
        /// 判断 Content-Type 是否文本类型，文本才直接显示，二进制走占位符。
        /// 规则与网页版 _isTextContentType 一致。
        /// </summary>
        public static bool IsTextContentType(string ctype)
        {
            if (string.IsNullOrEmpty(ctype)) return true;
            var c = ctype.ToLowerInvariant();
            if (c.StartsWith("text/")) return true;
            if (c.StartsWith("application/json")) return true;
            if (c.StartsWith("application/xml")) return true;
            if (c.StartsWith("application/javascript")) return true;
            if (c.StartsWith("application/x-www-form-urlencoded")) return true;
            if (c.Contains("+json") || c.Contains("+xml")) return true;
            if (c.StartsWith("image/svg")) return true;
            return false;
        }

        private static (byte[] body, string contentType) BuildMultipartBody(List<CurlParser.FormPart> parts)
        {
            // 手动构建 multipart body，格式与 curl 完全一致：
            // 每个 part 只有 Content-Disposition 和 Content-Type，不含 Content-Length
            string boundary = "------------------------" + Guid.NewGuid().ToString("N").Substring(0, 16);
            using (var ms = new MemoryStream())
            {
                foreach (var part in parts)
                {
                    Write(ms, "--" + boundary + "\r\n");
                    if (part.FilePath != null)
                    {
                        string filename = Path.GetFileName(part.FilePath);
                        string mime = GetMimeType(part.FilePath);
                        Write(ms, $"Content-Disposition: form-data; name=\"{part.Name}\"; filename=\"{filename}\"\r\n");
                        Write(ms, $"Content-Type: {mime}\r\n");
                        Write(ms, "\r\n");
                        byte[] fileBytes = File.ReadAllBytes(part.FilePath);
                        ms.Write(fileBytes, 0, fileBytes.Length);
                    }
                    else
                    {
                        Write(ms, $"Content-Disposition: form-data; name=\"{part.Name}\"\r\n");
                        Write(ms, "\r\n");
                        byte[] val = Encoding.UTF8.GetBytes(part.Value ?? "");
                        ms.Write(val, 0, val.Length);
                    }
                    Write(ms, "\r\n");
                }
                Write(ms, "--" + boundary + "--\r\n");
                return (ms.ToArray(), "multipart/form-data; boundary=" + boundary);
            }
        }

        private static void Write(MemoryStream ms, string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        private static string GetMimeType(string filePath)
        {
            switch (Path.GetExtension(filePath).ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".ico": return "image/x-icon";
                case ".svg": return "image/svg+xml";
                case ".mp4": return "video/mp4";
                case ".mp3": return "audio/mpeg";
                case ".pdf": return "application/pdf";
                case ".json": return "application/json";
                case ".txt": return "text/plain";
                default: return "application/octet-stream";
            }
        }
    }
}
