using System;
using System.Runtime.InteropServices;
using SysMenu.Native.Enums;

namespace SysMenu.Native.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    struct DwmBlurBehind
    {
        public DwmBB dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;
    }
}
