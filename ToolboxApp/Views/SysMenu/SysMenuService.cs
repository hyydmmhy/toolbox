using System;
using System.IO;
using SysMenu.Settings;

namespace SysMenu
{
    /// <summary>
    /// 管理 SysMenu 的启动和停止
    /// </summary>
    static class SysMenuService
    {
        private static Forms.MainForm _mainForm;
        private static bool _isRunning;

        public static bool IsRunning => _isRunning;

        public static void Start()
        {
            if (_isRunning) return;

            try
            {
                var settings = ApplicationSettingsFile.Read();
                settings.ShowSystemTrayIcon = false; // 禁用独立托盘图标，使用 Toolbox 自身的托盘

                _mainForm = new Forms.MainForm(settings);
                _mainForm.Show(); // OnLoad 中会设置 Visible=false, ShowInTaskbar=false, Opacity=0
                _isRunning = true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "启动系统菜单功能失败: " + ex.Message,
                    "错误",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public static void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _mainForm?.Close();
                _mainForm?.Dispose();
            }
            catch { }

            _mainForm = null;
            _isRunning = false;
        }

        private static string EnabledFilePath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Toolbox");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "SysMenuEnabled.txt");
            }
        }

        public static bool LoadEnabledState()
        {
            try
            {
                if (File.Exists(EnabledFilePath))
                {
                    var text = File.ReadAllText(EnabledFilePath).Trim();
                    return text.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return true; // 默认启用
        }

        public static void SaveEnabledState(bool enabled)
        {
            try
            {
                File.WriteAllText(EnabledFilePath, enabled ? "true" : "false");
            }
            catch { }
        }
    }
}
