using System.Runtime.InteropServices;

namespace MyTopBar.Infrastructure.Interop
{
    /// <summary>
    /// 集中管理材质、系统指标采集所需的 Win32 互操作声明。
    /// 业务代码不应直接引用 DllImport，统一从这里调用。
    /// </summary>
    internal static class NativeMethods
    {
        #region 低级鼠标/键盘钩子

        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        /// <summary>低级钩子回调。必须在托管侧持有引用，否则会被 GC 回收导致崩溃。</summary>
        public delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

        /// <summary>低级键盘钩子回调（与 LowLevelHookProc 同签名，保留以兼容既有代码）。</summary>
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        /// <summary>WH_MOUSE_LL 的 lParam 指向的结构。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        /// <summary>取指定屏幕坐标处顶层窗口的句柄。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT pt);

        /// <summary>取窗口所属进程 id。</summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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

        public const uint ASFW_ANY = 0xFFFFFFFF;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllowSetForegroundWindow(uint dwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_RESTORE = 9;

        /// <summary>
        /// 强制把窗口带到前台。
        ///
        /// 背景：顶栏带 WS_EX_NOACTIVATE，永远不是活动窗口，本进程也就从不
        /// 被系统认作「前台进程」。Windows 的前台锁定规则因此会静默拒绝
        /// 本进程发出的 SetForegroundWindow —— 表现为新建的窗口（如设置窗口）
        /// 能显示，但落在其他窗口后面拿不到焦点。
        ///
        /// 这里先调用 AllowSetForegroundWindow(ASFW_ANY) 显式让出前台权，
        /// 再逐级升级：ShowWindow(SW_RESTORE) → BringWindowToTop →
        /// SetForegroundWindow。任一步成功即可把窗口推到最前。
        /// </summary>
        public static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            // 放弃本进程的"前台特权"，允许随后的请求生效。
            AllowSetForegroundWindow(ASFW_ANY);

            ShowWindow(hwnd, SW_RESTORE);
            BringWindowToTop(hwnd);

            if (GetForegroundWindow() != hwnd)
            {
                SetForegroundWindow(hwnd);
            }
        }

        #endregion

        #region DWM 窗口属性

        /// <summary>深色模式开关，Windows 10 1809+ 可用。</summary>
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        /// <summary>系统背景材质类型，Windows 11 22H2 (22621) 起可用。</summary>
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        /// <summary>窗口圆角偏好，Windows 11 起可用。</summary>
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        /// <summary>圆角偏好的取值。</summary>
        public const int DWMWCP_DEFAULT = 0;
        public const int DWMWCP_DONOTROUND = 1;
        public const int DWMWCP_ROUND = 2;
        public const int DWMWCP_ROUNDSMALL = 3;

        public const int DWMSBT_AUTO = 0;
        public const int DWMSBT_NONE = 1;
        public const int DWMSBT_MAINWINDOW = 2;      // Mica
        public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
        public const int DWMSBT_TABBEDWINDOW = 4;    // Mica Alt

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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

        /// <summary>
        /// 应用毛玻璃（ACCENT_ENABLE_BLURBEHIND）。
        ///
        /// 只做高斯模糊，不加噪点、不叠调色，背景纹理仍然可见。
        /// 与 ACRYLICBLURBEHIND 的区别就在这里——后者会额外叠一层色调，
        /// 观感更像磨砂塑料而不是玻璃。
        ///
        /// 返回 true 表示 API 调用成功。
        /// </summary>
        public static bool ApplyBlurBehind(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var accent = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_BLURBEHIND,
                AccentFlags = 0,
                GradientColor = 0,
            };

            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, buffer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = buffer,
                    SizeOfData = size,
                };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>清除合成层（把 AccentState 设为 DISABLED）。</summary>
        public static void ClearAccent(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var accent = new AccentPolicy { AccentState = 0 };
            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, buffer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = buffer,
                    SizeOfData = size,
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            catch
            {
                // 清除失败不影响后续操作。
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

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
