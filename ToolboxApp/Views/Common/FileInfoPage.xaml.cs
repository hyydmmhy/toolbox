using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ToolboxApp.Utils;

namespace ToolboxApp.Views.Common
{
    public class NotEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrWhiteSpace(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class FileInfoItem
    {
        public string FileName { get; set; }
        public string SizeText { get; set; }
        public long RawSize { get; set; }
        public string FileVersion { get; set; }
        public string ProductVersion { get; set; }
        public string ProductName { get; set; }
        public string FileDescription { get; set; }
        public string CompanyName { get; set; }
    }

    public partial class FileInfoPage : UserControl
    {
        private List<FileInfoItem> _items = new List<FileInfoItem>();

        public FileInfoPage()
        {
            InitializeComponent();
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    PathBox.Text = dialog.SelectedPath;
                }
            }
        }

        private async void ScanBtn_Click(object sender, RoutedEventArgs e)
        {
            var path = PathBox.Text.Trim();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                StatusLabel.Content = "请输入有效的目录路径";
                return;
            }

            ScanBtn.IsEnabled = false;
            StatusLabel.Content = "正在扫描...";

            try
            {
                _items = await Task.Run(() =>
                {
                    var files = EnumerateFilesSafe(path);
                    var results = new List<FileInfoItem>();
                    int total = files.Count;
                    int done = 0;

                    foreach (var file in files)
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            var vi = FileVersionInfo.GetVersionInfo(file);
                            results.Add(new FileInfoItem
                            {
                                FileName = fi.Name,
                                SizeText = FormatSize(fi.Length),
                                RawSize = fi.Length,
                                FileVersion = vi.FileVersion ?? "",
                                ProductVersion = vi.ProductVersion ?? "",
                                ProductName = vi.ProductName ?? "",
                                FileDescription = vi.FileDescription ?? "",
                                CompanyName = vi.CompanyName ?? ""
                            });
                        }
                        catch { }

                        done++;
                        if (done % 100 == 0 || done == total)
                        {
                            Dispatcher.Invoke(() => StatusLabel.Content = $"正在扫描 {done}/{total}");
                        }
                    }

                    return results.OrderBy(i => i.FileName).ToList();
                });

                ResultGrid.ItemsSource = _items;
                StatusLabel.Content = $"扫描完成，共 {_items.Count} 个文件";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "扫描失败: " + ex.Message;
            }
            finally
            {
                ScanBtn.IsEnabled = true;
            }
        }

        private async void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_items == null || _items.Count == 0)
            {
                MessageBox.Show("没有可复制内容", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("文件名\t大小\t文件版本\t产品版本\t产品名称\t文件说明\t公司");
            foreach (var item in _items)
            {
                sb.AppendLine($"{item.FileName}\t{item.SizeText}\t{item.FileVersion}\t{item.ProductVersion}\t{item.ProductName}\t{item.FileDescription}\t{item.CompanyName}");
            }

            if (!await ClipboardHelper.SetTextAsync(sb.ToString()))
            {
                MessageBox.Show("复制失败：剪贴板被其他程序占用，请稍后重试", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MessageBox.Show("已复制到剪切板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        internal static List<FileInfoItem> ScanFiles(string path)
        {
            var files = EnumerateFilesSafe(path);
            var results = new List<FileInfoItem>();
            foreach (var file in files)
            {
                try
                {
                    var fi = new FileInfo(file);
                    var vi = FileVersionInfo.GetVersionInfo(file);
                    results.Add(new FileInfoItem
                    {
                        FileName = fi.Name,
                        SizeText = FormatSize(fi.Length),
                        RawSize = fi.Length,
                        FileVersion = vi.FileVersion ?? "",
                        ProductVersion = vi.ProductVersion ?? "",
                        ProductName = vi.ProductName ?? "",
                        FileDescription = vi.FileDescription ?? "",
                        CompanyName = vi.CompanyName ?? ""
                    });
                }
                catch { }
            }
            return results.OrderBy(i => i.FileName).ToList();
        }

        internal static List<string> EnumerateFilesSafe(string root)
        {
            var result = new List<string>();
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var file in Directory.GetFiles(dir))
                        result.Add(file);
                    foreach (var sub in Directory.GetDirectories(dir))
                        stack.Push(sub);
                }
                catch { }
            }

            return result;
        }

        internal static string FormatSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024)
                return $"{kb:F2} KB";
            double mb = kb / 1024.0;
            if (mb < 1024)
                return $"{mb:F2} MB";
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }
    }
}
