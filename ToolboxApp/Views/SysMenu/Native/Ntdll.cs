using System;
using System.Runtime.InteropServices;
using SysMenu.Native.Structs;

namespace SysMenu.Native
{
    static class Ntdll
    {
        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ProcessBasicInformation pbi, int processInformationLength, out int returnLength);
    }
}
