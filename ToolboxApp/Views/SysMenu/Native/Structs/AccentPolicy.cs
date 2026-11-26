using System.Runtime.InteropServices;
using SysMenu.Native.Enums;

namespace SysMenu.Native.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }
}
