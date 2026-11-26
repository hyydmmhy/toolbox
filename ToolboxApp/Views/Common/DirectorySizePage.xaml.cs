using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ToolboxApp.Views.Common
{
    public class DirectorySizeItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Size { get; set; }
        public int FileCount { get; set; }
        public long RawSize { get; set; }
    }

    public partial class DirectorySizePage : UserControl
    {
        public DirectorySizePage()
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

            if (IsDriveRoot(path))
            {
                StatusLabel.Content = "不允许扫描驱动器根目录";
                return;
            }

            SetScanning(true);
            try
            {
                var dirs = Directory.GetDirectories(path);
                var items = new List<DirectorySizeItem>();
                int done = 0;

                foreach (var dir in dirs)
                {
                    var item = await Task.Run(() => ScanDirectory(dir));
                    items.Add(item);
                    done++;
                    Dispatcher.Invoke(() => StatusLabel.Content = $"已扫描 {done}/{dirs.Length}");
                }

                ResultGrid.ItemsSource = items.OrderByDescending(i => i.RawSize).ToList();
                var totalSize = items.Sum(i => i.RawSize);
                StatusLabel.Content = $"扫描完成，共 {items.Count} 个子目录，总大小 {FormatSize(totalSize)}";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "扫描失败: " + ex.Message;
            }
            finally
            {
                SetScanning(false);
            }
        }

        private async void ScanBuiltinBtn_Click(object sender, RoutedEventArgs e)
        {
            SetScanning(true);
            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var builtinDirs = new (Environment.SpecialFolder? folder, string fallback)[]
                {
                    (Environment.SpecialFolder.Desktop, "Desktop"),
                    (null, "Downloads"),
                    (Environment.SpecialFolder.MyPictures, "Pictures"),
                    (Environment.SpecialFolder.MyDocuments, "Documents"),
                    (Environment.SpecialFolder.MyMusic, "Music"),
                    (Environment.SpecialFolder.MyVideos, "Videos"),
                    (Environment.SpecialFolder.Favorites, "Favorites"),
                    (null, "Links"),
                    (null, "Saved Games"),
                    (null, "Searches"),
                    (null, "Contacts"),
                };

                var items = new List<DirectorySizeItem>();
                int done = 0;

                foreach (var (folder, fallback) in builtinDirs)
                {
                    string path = null;
                    if (folder.HasValue)
                    {
                        path = Environment.GetFolderPath(folder.Value);
                    }
                    if (string.IsNullOrEmpty(path))
                    {
                        path = System.IO.Path.Combine(userProfile, fallback);
                    }

                    if (Directory.Exists(path))
                    {
                        var item = await Task.Run(() => ScanDirectory(path));
                        items.Add(item);
                    }

                    done++;
                    Dispatcher.Invoke(() => StatusLabel.Content = $"已扫描 {done}/{builtinDirs.Length}");
                }

                ResultGrid.ItemsSource = items.OrderByDescending(i => i.RawSize).ToList();
                var totalSize = items.Sum(i => i.RawSize);
                StatusLabel.Content = $"扫描完成，共 {items.Count} 个目录，总大小 {FormatSize(totalSize)}";
            }
            catch (Exception ex)
            {
                StatusLabel.Content = "扫描失败: " + ex.Message;
            }
            finally
            {
                SetScanning(false);
            }
        }

        internal static DirectorySizeItem ScanDirectory(string path)
        {
            long totalSize = 0;
            int fileCount = 0;

            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(path));

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    // Skip reparse points / symlinks
                    if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;

                    foreach (var file in dir.GetFiles())
                    {
                        try
                        {
                            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                                continue;
                            totalSize += file.Length;
                            fileCount++;
                        }
                        catch { }
                    }

                    foreach (var sub in dir.GetDirectories())
                    {
                        stack.Push(sub);
                    }
                }
                catch { }
            }

            return new DirectorySizeItem
            {
                Name = System.IO.Path.GetFileName(path),
                Path = path,
                Size = FormatSize(totalSize),
                FileCount = fileCount,
                RawSize = totalSize
            };
        }

        internal static bool IsDriveRoot(string path)
        {
            try
            {
                var full = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
                return full.Length <= 3 && full.EndsWith(":");
            }
            catch
            {
                return false;
            }
        }

        internal static string FormatSize(long bytes)
        {
            double kb = bytes / 1024.0;
            if (kb < 1024)
                return $"{kb:F2} KB";
            double mb = kb / 1024.0;
            if (mb < 1024)
                return $"{mb:F2} MB";
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }

        private void SetScanning(bool scanning)
        {
            ScanBtn.IsEnabled = !scanning;
            ScanBuiltinBtn.IsEnabled = !scanning;
        }
    }
}
