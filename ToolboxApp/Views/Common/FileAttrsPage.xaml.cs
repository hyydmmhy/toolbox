using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ToolboxApp.Utils;

namespace ToolboxApp.Views.Common
{
    public class FileAttrItem
    {
        public string FileName { get; set; }
        public long Size { get; set; }
        public string MD5 { get; set; }
        public string FilePath { get; set; }
    }

    public partial class FileAttrsPage : UserControl
    {
        private List<FileAttrItem> _items = new List<FileAttrItem>();

        public FileAttrsPage()
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
                    var bag = new ConcurrentBag<FileAttrItem>();
                    int total = files.Count;
                    int done = 0;

                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            var md5 = ComputeMD5(file);
                            bag.Add(new FileAttrItem
                            {
                                FileName = fi.Name,
                                Size = fi.Length,
                                MD5 = md5,
                                FilePath = file
                            });
                        }
                        catch { }

                        var d = System.Threading.Interlocked.Increment(ref done);
                        if (d % 100 == 0 || d == total)
                        {
                            Dispatcher.Invoke(() => StatusLabel.Content = $"正在扫描 {d}/{total}");
                        }
                    });

                    return bag.OrderBy(i => i.FileName).ToList();
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
            sb.AppendLine("文件名\t大小(bytes)\tMD5\t文件路径");
            foreach (var item in _items)
            {
                sb.AppendLine($"{item.FileName}\t{item.Size}\t{item.MD5}\t{item.FilePath}");
            }

            if (!await ClipboardHelper.SetTextAsync(sb.ToString()))
            {
                MessageBox.Show("复制失败：剪贴板被其他程序占用，请稍后重试", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MessageBox.Show("已复制到剪切板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        internal static string ComputeMD5(string filePath)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
            {
                var hash = md5.ComputeHash(stream);
                var sb = new StringBuilder(32);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("X2"));
                }
                return sb.ToString();
            }
        }

        internal static List<FileAttrItem> ScanFiles(string path)
        {
            var files = EnumerateFilesSafe(path);
            var bag = new System.Collections.Concurrent.ConcurrentBag<FileAttrItem>();
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
            {
                try
                {
                    var fi = new FileInfo(file);
                    bag.Add(new FileAttrItem { FileName = fi.Name, Size = fi.Length, MD5 = ComputeMD5(file), FilePath = file });
                }
                catch { }
            });
            return bag.OrderBy(i => i.FileName).ToList();
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
    }
}
