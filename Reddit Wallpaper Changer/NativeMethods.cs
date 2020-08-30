using System;
using System.Runtime.InteropServices;

namespace Reddit_Wallpaper_Changer
{
    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr result);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string lpClassName, IntPtr ZeroOnly);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
    }
}
