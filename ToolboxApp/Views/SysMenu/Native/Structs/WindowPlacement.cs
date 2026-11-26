using System.Drawing;
using System.Runtime.InteropServices;
using SysMenu.Native.Enums;

namespace SysMenu.Native.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    struct WindowPlacement
    {
        public int length;
        public int flags;
        public ShowWindowCommands showCmd;
        public Point ptMinPosition;
        public Point ptMaxPosition;
        public Rectangle rcNormalPosition;
    }
}
