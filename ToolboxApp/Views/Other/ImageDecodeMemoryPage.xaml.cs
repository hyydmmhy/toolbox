using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ToolboxApp.Views.Other
{
    public class ImageInfoItem
    {
        public string FileName { get; set; }
        public string Format { get; set; }
        public string Resolution { get; set; }
        public string DecodeMemory { get; set; }
        public long DecodeMemoryBytes { get; set; }
        public string FileSize { get; set; }
        public long FileSizeBytes { get; set; }
        public string ModifiedTime { get; set; }
        public string FilePath { get; set; }
    }

    public partial class ImageDecodeMemoryPage : UserControl
    {
        private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpParameters;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif",
            ".psd", ".psb", ".heic", ".heif", ".avif", ".jp2", ".j2k", ".svg",
            ".raw", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".sr2", ".dng",
            ".orf", ".rw2", ".pef", ".raf", ".mrw", ".3fr", ".dcr", ".kdc",
            ".rwl", ".x3f", ".iiq", ".srf", ".srw", ".bay", ".cap", ".erf",
            ".fff", ".mef", ".mos", ".ptx", ".r3d", ".rwz", ".obm", ".crw",
            ".cs1", ".cxi", ".jxl", ".qoi", ".tga", ".dds", ".exr", ".hdr", ".pcx"
        };

        private static readonly HashSet<string> RawExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".raw", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".sr2", ".dng",
            ".orf", ".rw2", ".pef", ".raf", ".mrw", ".3fr", ".dcr", ".kdc",
            ".rwl", ".x3f", ".iiq", ".srf", ".srw", ".bay", ".cap", ".erf",
            ".fff", ".mef", ".mos", ".ptx", ".r3d", ".rwz", ".crw"
        };

        private List<ImageInfoItem> _items = new List<ImageInfoItem>();

        public ImageDecodeMemoryPage()
        {
            InitializeComponent();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "选择要扫描的目录";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TxtDirPath.Text = dlg.SelectedPath;
                }
            }
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            var dir = TxtDirPath.Text?.Trim();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                MessageBox.Show("请选择有效的目录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnScan.IsEnabled = false;
            LblStatus.Text = "正在扫描...";
            DgResults.ItemsSource = null;

            var results = await Task.Run(() => ScanDirectory(dir));

            _items = results;
            DgResults.ItemsSource = _items;
            LblStatus.Text = $"扫描完成，共找到 {_items.Count} 个图片文件";
            BtnScan.IsEnabled = true;
        }

        private List<ImageInfoItem> ScanDirectory(string rootDir) => ScanDir(rootDir);

        internal static List<ImageInfoItem> ScanDir(string rootDir)
        {
            var items = new List<ImageInfoItem>();
            var stack = new Stack<string>();
            stack.Push(rootDir);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        try
                        {
                            var subInfo = new DirectoryInfo(subDir);
                            if ((subInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                                stack.Push(subDir);
                        }
                        catch { }
                    }

                    foreach (var file in Directory.GetFiles(dir))
                    {
                        var ext = Path.GetExtension(file);
                        if (!ImageExtensions.Contains(ext)) continue;

                        var item = ProcessFileStatic(file, ext);
                        if (item != null)
                            items.Add(item);
                    }
                }
                catch { }
            }

            return items;
        }

        private ImageInfoItem ProcessFile(string filePath, string ext) => ProcessFileStatic(filePath, ext);

        internal static ImageInfoItem ProcessFileStatic(string filePath, string ext)
        {
            try
            {
                var fi = new FileInfo(filePath);
                var item = new ImageInfoItem
                {
                    FileName = fi.Name,
                    Format = ext.TrimStart('.').ToUpper(),
                    FilePath = filePath,
                    FileSizeBytes = fi.Length,
                    FileSize = FormatSizeStatic(fi.Length),
                    ModifiedTime = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                };

                int width = 0, height = 0;
                bool resolved = false;
                var extLower = ext.ToLowerInvariant();

                // SVG: parse from XML
                if (extLower == ".svg")
                {
                    resolved = TryGetSvgDimensionsStatic(filePath, out width, out height);
                }
                // PSD/PSB: read header
                else if (extLower == ".psd" || extLower == ".psb")
                {
                    resolved = TryGetPsdDimensionsStatic(filePath, out width, out height);
                }
                // RAW: try TIFF-based extraction or BitmapDecoder
                else if (RawExtensions.Contains(extLower))
                {
                    resolved = TryGetRawDimensionsStatic(filePath, out width, out height);
                }

                // Try WPF BitmapDecoder for common formats
                if (!resolved)
                {
                    try
                    {
                        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            var decoder = BitmapDecoder.Create(stream,
                                BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                            if (decoder.Frames.Count > 0)
                            {
                                width = decoder.Frames[0].PixelWidth;
                                height = decoder.Frames[0].PixelHeight;
                                resolved = true;
                            }
                        }
                    }
                    catch { }
                }

                // Fallback: System.Drawing
                if (!resolved)
                {
                    try
                    {
                        using (var img = System.Drawing.Image.FromFile(filePath))
                        {
                            width = img.Width;
                            height = img.Height;
                            resolved = true;
                        }
                    }
                    catch { }
                }

                if (resolved && width > 0 && height > 0)
                {
                    item.Resolution = $"{width} × {height}";
                    long decodeBytes = (long)width * height * 4;
                    item.DecodeMemoryBytes = decodeBytes;
                    item.DecodeMemory = FormatSizeStatic(decodeBytes);
                }
                else
                {
                    item.Resolution = "不支持";
                    item.DecodeMemory = "不支持";
                    item.DecodeMemoryBytes = 0;
                }

                return item;
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetPsdDimensions(string filePath, out int width, out int height)
            => TryGetPsdDimensionsStatic(filePath, out width, out height);
        private bool TryGetSvgDimensions(string filePath, out int width, out int height)
            => TryGetSvgDimensionsStatic(filePath, out width, out height);
        private bool TryGetRawDimensions(string filePath, out int width, out int height)
            => TryGetRawDimensionsStatic(filePath, out width, out height);

        private static bool TryGetPsdDimensionsStatic(string filePath, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(fs))
                {
                    // PSD header: 4 bytes signature "8BPS", 2 bytes version, 6 bytes reserved,
                    // 2 bytes channels, 4 bytes height, 4 bytes width
                    if (fs.Length < 22) return false;
                    var sig = br.ReadBytes(4);
                    if (sig[0] != 0x38 || sig[1] != 0x42 || sig[2] != 0x50 || sig[3] != 0x53) // "8BPS"
                        return false;
                    br.ReadInt16(); // version
                    br.ReadBytes(6); // reserved
                    br.ReadInt16(); // channels
                    // height and width are big-endian 4-byte integers
                    height = ReadBigEndianInt32(br);
                    width = ReadBigEndianInt32(br);
                    return width > 0 && height > 0;
                }
            }
            catch { return false; }
        }

        private static bool TryGetSvgDimensionsStatic(string filePath, out int width, out int height)
        {
            width = height = 0;
            try
            {
                // Read first 4KB to find width/height or viewBox
                string header;
                using (var sr = new StreamReader(filePath, Encoding.UTF8))
                {
                    var buf = new char[4096];
                    int read = sr.Read(buf, 0, buf.Length);
                    header = new string(buf, 0, read);
                }

                // Try viewBox first
                var vbMatch = Regex.Match(header, @"viewBox\s*=\s*""[\d.\s,]+""\s*", RegexOptions.IgnoreCase);
                if (vbMatch.Success)
                {
                    var nums = Regex.Matches(vbMatch.Value, @"[\d.]+");
                    if (nums.Count >= 4)
                    {
                        double vw = double.Parse(nums[2].Value);
                        double vh = double.Parse(nums[3].Value);
                        if (vw > 0 && vh > 0)
                        {
                            width = (int)Math.Round(vw);
                            height = (int)Math.Round(vh);
                            return true;
                        }
                    }
                }

                // Try width/height attributes
                var wMatch = Regex.Match(header, @"\bwidth\s*=\s*""([\d.]+)", RegexOptions.IgnoreCase);
                var hMatch = Regex.Match(header, @"\bheight\s*=\s*""([\d.]+)", RegexOptions.IgnoreCase);
                if (wMatch.Success && hMatch.Success)
                {
                    width = (int)Math.Round(double.Parse(wMatch.Groups[1].Value));
                    height = (int)Math.Round(double.Parse(hMatch.Groups[1].Value));
                    return width > 0 && height > 0;
                }

                return false;
            }
            catch { return false; }
        }

        private static bool TryGetRawDimensionsStatic(string filePath, out int width, out int height)
        {
            width = height = 0;
            // Try BitmapDecoder first (handles DNG, some CR2/NEF via WIC codecs)
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(stream,
                        BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    if (decoder.Frames.Count > 0)
                    {
                        // Pick the largest frame (first frame may be thumbnail)
                        foreach (var frame in decoder.Frames)
                        {
                            if (frame.PixelWidth > width || frame.PixelHeight > height)
                            {
                                width = frame.PixelWidth;
                                height = frame.PixelHeight;
                            }
                        }
                        if (width > 0 && height > 0) return true;
                    }
                }
            }
            catch { }

            // Try TIFF-based header parsing (many RAW formats use TIFF structure)
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 8) return false;
                    var bo = br.ReadUInt16();
                    bool littleEndian;
                    if (bo == 0x4949) littleEndian = true;       // "II"
                    else if (bo == 0x4D4D) littleEndian = false; // "MM"
                    else return false;

                    ushort magic = ReadUInt16(br, littleEndian);
                    if (magic != 42 && magic != 43) return false; // not TIFF

                    uint ifdOffset = ReadUInt32(br, littleEndian);
                    int bestW = 0, bestH = 0;

                    // Scan up to 10 IFDs (first IFD is often thumbnail in RAW files)
                    for (int ifdIndex = 0; ifdIndex < 10 && ifdOffset > 0 && ifdOffset < fs.Length; ifdIndex++)
                    {
                        fs.Seek(ifdOffset, SeekOrigin.Begin);
                        ushort entryCount = ReadUInt16(br, littleEndian);
                        int w = 0, h = 0;
                        uint subIfdOffset = 0;

                        for (int i = 0; i < entryCount && fs.Position + 12 <= fs.Length; i++)
                        {
                            ushort tag = ReadUInt16(br, littleEndian);
                            ushort type = ReadUInt16(br, littleEndian);
                            uint count = ReadUInt32(br, littleEndian);
                            // Value/offset field: 4 bytes. For SHORT (type=3), value is in first 2 bytes (LE) or last 2 bytes (BE)
                            byte[] valBytes = br.ReadBytes(4);
                            uint val;
                            if (type == 3) // SHORT
                                val = littleEndian
                                    ? (uint)(valBytes[0] | (valBytes[1] << 8))
                                    : (uint)((valBytes[0] << 8) | valBytes[1]);
                            else // LONG or default
                                val = littleEndian
                                    ? (uint)(valBytes[0] | (valBytes[1] << 8) | (valBytes[2] << 16) | (valBytes[3] << 24))
                                    : (uint)((valBytes[0] << 24) | (valBytes[1] << 16) | (valBytes[2] << 8) | valBytes[3]);

                            if (tag == 256) w = (int)val;           // ImageWidth
                            else if (tag == 257) h = (int)val;      // ImageLength
                            else if (tag == 330) subIfdOffset = val; // SubIFDs
                        }

                        if (w > 0 && h > 0 && (long)w * h > (long)bestW * bestH)
                        {
                            bestW = w;
                            bestH = h;
                        }

                        // Read next IFD offset (4 bytes after entries)
                        if (fs.Position + 4 <= fs.Length)
                            ifdOffset = ReadUInt32(br, littleEndian);
                        else
                            ifdOffset = 0;

                        // Also try SubIFD if present and we haven't found large dimensions yet
                        if (subIfdOffset > 0 && subIfdOffset < fs.Length && bestW * bestH < 1000000)
                        {
                            long savedPos = fs.Position;
                            try
                            {
                                fs.Seek(subIfdOffset, SeekOrigin.Begin);
                                ushort subCount = ReadUInt16(br, littleEndian);
                                int sw = 0, sh = 0;
                                for (int i = 0; i < subCount && fs.Position + 12 <= fs.Length; i++)
                                {
                                    ushort stag = ReadUInt16(br, littleEndian);
                                    ushort stype = ReadUInt16(br, littleEndian);
                                    ReadUInt32(br, littleEndian); // count
                                    byte[] svalBytes = br.ReadBytes(4);
                                    uint sval;
                                    if (stype == 3)
                                        sval = littleEndian
                                            ? (uint)(svalBytes[0] | (svalBytes[1] << 8))
                                            : (uint)((svalBytes[0] << 8) | svalBytes[1]);
                                    else
                                        sval = littleEndian
                                            ? (uint)(svalBytes[0] | (svalBytes[1] << 8) | (svalBytes[2] << 16) | (svalBytes[3] << 24))
                                            : (uint)((svalBytes[0] << 24) | (svalBytes[1] << 16) | (svalBytes[2] << 8) | svalBytes[3]);

                                    if (stag == 256) sw = (int)sval;
                                    else if (stag == 257) sh = (int)sval;
                                }
                                if (sw > 0 && sh > 0 && (long)sw * sh > (long)bestW * bestH)
                                {
                                    bestW = sw;
                                    bestH = sh;
                                }
                            }
                            catch { }
                            fs.Seek(savedPos, SeekOrigin.Begin);
                        }
                    }

                    if (bestW > 0 && bestH > 0)
                    {
                        width = bestW; height = bestH;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static int ReadBigEndianInt32(BinaryReader br)
        {
            var bytes = br.ReadBytes(4);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        private static ushort ReadUInt16(BinaryReader br, bool littleEndian)
        {
            var bytes = br.ReadBytes(2);
            return littleEndian
                ? (ushort)(bytes[0] | (bytes[1] << 8))
                : (ushort)((bytes[0] << 8) | bytes[1]);
        }

        private static uint ReadUInt32(BinaryReader br, bool littleEndian)
        {
            var bytes = br.ReadBytes(4);
            return littleEndian
                ? (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24))
                : (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }

        private string FormatSize(long bytes) => FormatSizeStatic(bytes);

        private static string FormatSizeStatic(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            return $"{bytes / (1024.0 * 1024):F2} MB";
        }

        private void DgResults_Sorting(object sender, DataGridSortingEventArgs e)
        {
        }

        private void MenuProperties_Click(object sender, RoutedEventArgs e)
        {
            var item = DgResults.SelectedItem as ImageInfoItem;
            if (item == null || !File.Exists(item.FilePath)) return;

            var info = new SHELLEXECUTEINFO();
            info.cbSize = Marshal.SizeOf(info);
            info.lpVerb = "properties";
            info.lpFile = item.FilePath;
            info.fMask = SEE_MASK_INVOKEIDLIST;
            info.nShow = 1;
            ShellExecuteEx(ref info);
        }

        private void MenuOpenLocation_Click(object sender, RoutedEventArgs e)
        {
            var item = DgResults.SelectedItem as ImageInfoItem;
            if (item == null || !File.Exists(item.FilePath)) return;

            Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
        }
    }
}
