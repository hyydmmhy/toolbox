using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ToolboxApp.Utils
{
    /// <summary>
    /// cURL 命令解析（直译自网页版 cf-toolbox/public/js/app.js）。
    /// 支持 bash / cmd 两种转义风格、单/双引号、ANSI-C 字符串 $'...'、反斜杠续行、^续行、^"" cmd 转义。
    /// </summary>
    public static class CurlParser
    {
        public class FormPart
        {
            public string Name;
            public string FilePath;  // 非 null 时为文件字段（@ 前缀）
            public string Value;     // 非 null 时为文本字段
        }

        public class Parsed
        {
            public string Method;
            public string Url;
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Body;
            public bool Insecure;
            public List<FormPart> FormParts = new List<FormPart>();
        }

        public static Parsed Parse(string command)
        {
            var tokens = Tokenize(command);
            return ParseTokens(tokens);
        }

        // ───── 分词 ─────
        private static List<string> Tokenize(string input)
        {
            var s = input.Trim();
            bool isCmdEscaped = Regex.IsMatch(s, @"\^""");
            s = Regex.Replace(s, @"[\\^]\r?\n", " ");
            if (isCmdEscaped) s = Regex.Replace(s, @"\^(.)", "$1");

            var ansiCMap = new Dictionary<char, string>
            {
                ['n'] = "\n", ['r'] = "\r", ['t'] = "\t", ['a'] = "\x07",
                ['b'] = "\b", ['e'] = "\x1b", ['E'] = "\x1b", ['f'] = "\f",
                ['v'] = "\v", ['\\'] = "\\", ['\''] = "'", ['"'] = "\"", ['?'] = "?",
            };

            var tokens = new List<string>();
            var cur = new StringBuilder();
            string mode = null;
            bool inToken = false;
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (mode == "single")
                {
                    if (c == '\'') mode = null;
                    else cur.Append(c);
                }
                else if (mode == "ansi-c")
                {
                    if (c == '\'') mode = null;
                    else if (c == '\\')
                    {
                        char next = (i + 1 < s.Length) ? s[i + 1] : '\0';
                        if (next == '\0') cur.Append(c);
                        else if (ansiCMap.TryGetValue(next, out var repl)) { cur.Append(repl); i++; }
                        else if (next == 'x')
                        {
                            var hex = new StringBuilder();
                            int j = i + 2;
                            while (j < s.Length && hex.Length < 2 && IsHex(s[j])) { hex.Append(s[j]); j++; }
                            if (hex.Length > 0) { cur.Append((char)Convert.ToInt32(hex.ToString(), 16)); i = j - 1; }
                            else { cur.Append(c).Append(next); i++; }
                        }
                        else if (next == 'u')
                        {
                            var hex = new StringBuilder();
                            int j = i + 2;
                            while (j < s.Length && hex.Length < 4 && IsHex(s[j])) { hex.Append(s[j]); j++; }
                            if (hex.Length > 0) { cur.Append(char.ConvertFromUtf32(Convert.ToInt32(hex.ToString(), 16))); i = j - 1; }
                            else { cur.Append(c).Append(next); i++; }
                        }
                        else if (next >= '0' && next <= '7')
                        {
                            var oct = new StringBuilder();
                            int j = i + 1;
                            while (j < s.Length && oct.Length < 3 && s[j] >= '0' && s[j] <= '7') { oct.Append(s[j]); j++; }
                            cur.Append((char)Convert.ToInt32(oct.ToString(), 8));
                            i = j - 1;
                        }
                        else { cur.Append(next); i++; }
                    }
                    else cur.Append(c);
                }
                else if (mode == "double")
                {
                    if (c == '"')
                    {
                        if (isCmdEscaped && i + 1 < s.Length && s[i + 1] == '"') { cur.Append('"'); i++; }
                        else mode = null;
                    }
                    else if (c == '\\')
                    {
                        char next = (i + 1 < s.Length) ? s[i + 1] : '\0';
                        if (next == '"' || next == '\\' || next == '$' || next == '`') { cur.Append(next); i++; }
                        else cur.Append(c);
                    }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '$' && i + 1 < s.Length && s[i + 1] == '\'') { mode = "ansi-c"; inToken = true; i++; }
                    else if (c == '\'') { mode = "single"; inToken = true; }
                    else if (c == '"') { mode = "double"; inToken = true; }
                    else if (c == '\\') { if (i + 1 < s.Length) { cur.Append(s[i + 1]); i++; inToken = true; } }
                    else if (char.IsWhiteSpace(c)) { if (inToken) { tokens.Add(cur.ToString()); cur.Clear(); inToken = false; } }
                    else { cur.Append(c); inToken = true; }
                }
                i++;
            }
            if (inToken) tokens.Add(cur.ToString());
            return tokens;
        }

        private static bool IsHex(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        // ───── 参数解析 ─────
        private static readonly HashSet<string> NoArgFlags = new HashSet<string>(StringComparer.Ordinal)
        {
            "-L", "--location", "-k", "--insecure", "--compressed",
            "-s", "--silent", "-S", "--show-error", "-v", "--verbose",
            "-i", "--include", "-f", "--fail", "-#", "--progress-bar",
            "-N", "--no-buffer", "-O", "--remote-name", "-J", "--remote-header-name",
            "-I", "--head", "-G", "--get", "-q", "--disable",
        };

        private static Parsed ParseTokens(List<string> tokens)
        {
            if (tokens.Count == 0 || !tokens[0].Equals("curl", StringComparison.OrdinalIgnoreCase))
                throw new Exception("命令必须以 curl 开头");

            string method = null, url = "";
            bool isGet = false;
            var p = new Parsed();
            var dataParts = new List<string>();

            for (int i = 1; i < tokens.Count; i++)
            {
                string a = tokens[i];
                if (a == "-X" || a == "--request") method = NextArg(tokens, ref i);
                else if (a == "-H" || a == "--header")
                {
                    string h = NextArg(tokens, ref i) ?? "";
                    int idx = h.IndexOf(':');
                    if (idx > 0)
                    {
                        var name = h.Substring(0, idx).Trim();
                        var val = h.Substring(idx + 1).Trim();
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith(":"))
                            p.Headers[name] = val;
                    }
                }
                else if (a == "-F" || a == "--form" || a == "--form-string")
                {
                    bool forceString = (a == "--form-string");
                    string v = NextArg(tokens, ref i) ?? "";
                    int eq = v.IndexOf('=');
                    if (eq >= 0)
                    {
                        string name = v.Substring(0, eq);
                        string val = v.Substring(eq + 1);
                        var part = new FormPart { Name = name };
                        if (!forceString && val.StartsWith("@"))
                            part.FilePath = val.Substring(1);
                        else
                            part.Value = val;
                        p.FormParts.Add(part);
                    }
                }
                else if (a == "-d" || a == "--data" || a == "--data-raw" || a == "--data-binary" || a == "--data-ascii")
                {
                    dataParts.Add(NextArg(tokens, ref i) ?? "");
                }
                else if (a == "--data-urlencode")
                {
                    string v = NextArg(tokens, ref i) ?? "";
                    int eq = v.IndexOf('=');
                    dataParts.Add(eq >= 0
                        ? Uri.EscapeDataString(v.Substring(0, eq)) + "=" + Uri.EscapeDataString(v.Substring(eq + 1))
                        : Uri.EscapeDataString(v));
                }
                else if (a == "-u" || a == "--user")
                {
                    var v = NextArg(tokens, ref i) ?? "";
                    p.Headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(v));
                }
                else if (a == "-A" || a == "--user-agent") p.Headers["User-Agent"] = NextArg(tokens, ref i) ?? "";
                else if (a == "-e" || a == "--referer") p.Headers["Referer"] = NextArg(tokens, ref i) ?? "";
                else if (a == "-b" || a == "--cookie") p.Headers["Cookie"] = NextArg(tokens, ref i) ?? "";
                else if (a == "-I" || a == "--head") { if (method == null) method = "HEAD"; }
                else if (a == "-G" || a == "--get") isGet = true;
                else if (a == "-k" || a == "--insecure") p.Insecure = true;
                else if (a == "--url") url = NextArg(tokens, ref i) ?? "";
                else if (a.StartsWith("-"))
                {
                    if (!NoArgFlags.Contains(a)) i++;   // 跳过未知参数的值
                }
                else
                {
                    if (string.IsNullOrEmpty(url)) url = a;
                }
            }

            if (string.IsNullOrEmpty(url)) throw new Exception("未找到 URL");

            string body = dataParts.Count > 0 ? string.Join("&", dataParts) : null;
            if (isGet && body != null)
            {
                url += (url.Contains("?") ? "&" : "?") + body;
                body = null;
                method = "GET";
            }
            if (method == null) method = (body != null || p.FormParts.Count > 0) ? "POST" : "GET";
            if (method == "GET" || method == "HEAD") body = null;
            if (body != null
                && !p.Headers.ContainsKey("Content-Type")
                && !p.Headers.ContainsKey("content-type"))
            {
                p.Headers["Content-Type"] = "application/x-www-form-urlencoded";
            }

            p.Method = method;
            p.Url = url;
            p.Body = body;
            return p;
        }

        private static string NextArg(List<string> tokens, ref int i)
        {
            i++;
            return i < tokens.Count ? tokens[i] : null;
        }
    }
}
