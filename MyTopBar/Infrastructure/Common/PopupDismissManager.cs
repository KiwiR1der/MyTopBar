using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MyTopBar.Infrastructure.Interop;

namespace MyTopBar.Infrastructure.Common
{
    /// <summary>
    /// 「点击弹窗外部即收起」的全局协调器。
    ///
    /// 背景：顶栏带 WS_EX_NOACTIVATE，永远不是活动窗口，
    /// WPF 的 Popup.StaysOpen=false 依赖「应用失活」事件，这里检测不到；
    /// 而 EVENT_SYSTEM_FOREGROUND 又只在前台窗口**变化**时触发——
    /// 若用户点击的本来就是当前前台窗口，事件不发，弹窗就不收。
    ///
    /// 这里改用 WH_MOUSE_LL 低级鼠标钩子：弹窗打开期间挂上，
    /// 拦截每次鼠标按下，若落点不在任何已打开的弹窗内，就收起全部。
    /// 所有弹窗关闭后摘掉钩子，不常驻监听。
    /// </summary>
    internal static class PopupDismissManager
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_NCLBUTTONDOWN = 0x00A1;

        private const uint GA_ROOT = 2;

        private static readonly object _gate = new();
        private static readonly List<PopupHost> _hosts = new();

        private static IntPtr _hook = IntPtr.Zero;
        private static NativeMethods.LowLevelHookProc? _hookProc;

        /// <summary>注册一个弹窗宿主，之后外部点击会收起它。</summary>
        public static void Register(PopupHost host)
        {
            lock (_gate)
            {
                if (!_hosts.Contains(host))
                {
                    _hosts.Add(host);
                }
            }
        }

        /// <summary>任意弹窗打开时调用：确保钩子已挂。</summary>
        public static void NotifyOpened()
        {
            lock (_gate)
            {
                if (_hook != IntPtr.Zero)
                {
                    return;
                }

                _hookProc = HookCallback;
                _hook = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_MOUSE_LL, _hookProc, IntPtr.Zero, 0);

                if (_hook == IntPtr.Zero)
                {
                    _hookProc = null;
                }
            }
        }

        /// <summary>弹窗关闭后调用：若没有弹窗还开着，摘掉钩子。</summary>
        public static void NotifyClosed()
        {
            lock (_gate)
            {
                foreach (PopupHost host in _hosts)
                {
                    if (host.IsOpen)
                    {
                        return; // 还有弹窗开着，保留钩子
                    }
                }

                RemoveHook();
            }
        }

        private static void RemoveHook()
        {
            if (_hook == IntPtr.Zero)
            {
                return;
            }

            try
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
            }
            finally
            {
                _hook = IntPtr.Zero;
                _hookProc = null;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsButtonDown(wParam))
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                IntPtr hit = NativeMethods.WindowFromPoint(data.pt);

                if (hit != IntPtr.Zero && !IsInsideAnyOpenPopup(hit))
                {
                    /*
                     * 落点不在任何已打开的弹窗内 —— 收起全部。
                     *
                     * 不按进程判断而按「是否落在弹窗内」判断：
                     * 这样点顶栏空白处、设置窗口、其他应用都会收起，
                     * 与 macOS 菜单栏行为一致；点弹窗内部则不受影响。
                     */
                    CloseAll();
                }
            }

            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>判断落点窗口是否属于某个已打开的弹窗（含其子窗口）。</summary>
        private static bool IsInsideAnyOpenPopup(IntPtr hit)
        {
            var openHandles = new List<IntPtr>();
            lock (_gate)
            {
                foreach (PopupHost host in _hosts)
                {
                    if (host.IsOpen && host.HostHandle != IntPtr.Zero)
                    {
                        openHandles.Add(host.HostHandle);
                    }
                }
            }

            if (openHandles.Count == 0)
            {
                return false;
            }

            // WindowFromPoint 可能返回弹窗的子窗口，向上追溯根窗口再比对。
            IntPtr root = GetAncestor(hit, GA_ROOT);
            foreach (IntPtr handle in openHandles)
            {
                if (hit == handle || root == handle)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsButtonDown(IntPtr wParam)
        {
            int msg = (int)wParam;
            return msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN
                || msg == WM_MBUTTONDOWN || msg == WM_XBUTTONDOWN
                || msg == WM_NCLBUTTONDOWN;
        }

        private static void CloseAll()
        {
            List<PopupHost> snapshot;
            lock (_gate)
            {
                snapshot = new List<PopupHost>(_hosts);
            }

            foreach (PopupHost host in snapshot)
            {
                host.CloseFromExternal();
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    }
}
