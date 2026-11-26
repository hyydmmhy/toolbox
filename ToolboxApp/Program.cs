using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ToolboxApp
{
    public static class Program
    {
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern bool FreeConsole();
        [DllImport("kernel32.dll")] private static extern bool SetConsoleOutputCP(uint wCodePageID);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleCP(uint wCodePageID);

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "cli", StringComparison.OrdinalIgnoreCase))
            {
                // CLI 模式：附加到父控制台（从 cmd/powershell 启动），否则新建一个
                if (!AttachConsole(-1))
                    AllocConsole();

                // 设置控制台为 UTF-8，避免中文乱码
                SetConsoleOutputCP(65001);
                SetConsoleCP(65001);

                var utf8 = new UTF8Encoding(false);
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });

                int exitCode;
                try
                {
                    exitCode = Cli.CliHandler.Run(args);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(Cli.CliHandler.FailJson("内部错误: " + ex.Message));
                    exitCode = 1;
                }

                // 释放控制台，让父进程的命令提示符正常返回
                FreeConsole();
                Environment.Exit(exitCode);
            }
            else
            {
                // GUI 模式：正常启动 WPF 应用
                var app = new App();
                app.InitializeComponent();
                app.Run();
            }
        }
    }
}
