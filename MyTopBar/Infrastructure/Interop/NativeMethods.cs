using System.Runtime.InteropServices;

namespace MyTopBar.Infrastructure.Interop
{
    /// <summary>
    /// 集中管理材质、系统指标采集所需的 Win32 互操作声明。
    /// 业务代码不应直接引用 DllImport，统一从这里调用。
    /// </summary>
    internal static class NativeMethods
    {
        #region 低级键盘钩子

        public const int WH_KEYBOARD_LL = 13;

        /// <summary>低级键盘钩子回调。必须在托管侧持有引用，否则会被 GC 回收导致崩溃。</summary>
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        #endregion

        #region 窗口样式（不抢焦点）

        public const int GWL_EXSTYLE = -20;

        /// <summary>不激活窗口。点击时前台窗口保持激活，顶栏只收鼠标消息。</summary>
        public const int WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>不出现在 Alt+Tab 列表里，配合 NOACTIVATE 一起用。</summary>
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// 给窗口加上 NOACTIVATE + TOOLWINDOW 扩展样式。
        ///
        /// 关键在于必须同时处理 WPF 的 AllowsTransparency / ShowActivated：
        /// 只设样式而不设 ShowActivated=False 时，WPF 仍会在 Show() 时激活窗口，
        /// 之后点击又会把焦点抢回来。两处都要做。
        /// </summary>
        public static void MakeNonActivating(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            long value = style.ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(value));
        }

        #endregion

        #region DWM 窗口属性

        /// <summary>深色模式开关，Windows 10 1809+ 可用。</summary>
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        /// <summary>系统背景材质类型，Windows 11 22H2 (22621) 起可用。</summary>
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        /// <summary>窗口圆角偏好，Windows 11 起可用。</summary>
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        public const int DWMSBT_AUTO = 0;
        public const int DWMSBT_NONE = 1;
        public const int DWMSBT_MAINWINDOW = 2;      // Mica
        public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
        public const int DWMSBT_TABBEDWINDOW = 4;    // Mica Alt

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        #endregion

        #region SetWindowCompositionAttribute（Win10 经典模糊 / Acrylic 兜底）

        public const int WCA_ACCENT_POLICY = 19;

        public const int ACCENT_NORMAL = 1;
        public const int ACCENT_ENABLE_BLURBEHIND = 3;
        public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll", PreserveSig = true)]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        #endregion

        #region 系统指标

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint Low;
            public uint High;
            public readonly ulong Value => ((ulong)High << 32) | Low;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        #endregion
    }
}
