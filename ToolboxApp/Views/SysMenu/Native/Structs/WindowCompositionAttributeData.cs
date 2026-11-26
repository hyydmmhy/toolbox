using System;
using System.Runtime.InteropServices;
using SysMenu.Native.Enums;

namespace SysMenu.Native.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}
