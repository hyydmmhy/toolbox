using System.Runtime.InteropServices;
using SysMenu.Native.Enums;

namespace SysMenu.Native
{
    static class SHCore
    {
        [DllImport("SHCore.dll", SetLastError = true)]
        public static extern bool SetProcessDpiAwareness(ProcessDpiAwareness processDpiAwareness);
    }
}
