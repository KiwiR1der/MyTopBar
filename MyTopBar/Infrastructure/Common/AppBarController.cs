using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Xml.Serialization;

namespace MyTopBar.Infrastructure.Common
{
    public sealed class AppBarController : IDisposable
    {
        private const uint ABM_NEW = 0x00000000;    // Registers a new appbar and specifies the message identifier that the system should use to send notification messages to the appbar.
                                                    // 注册新的应用栏并指定系统应用来向应用栏发送通知消息的消息标识符。
        private const uint ABM_REMOVE = 0x00000001; // Unregisters an appbar, removing the message identifier that the system uses to send notification messages to the appbar.
                                                    // 注销应用栏，删除系统用于向应用栏发送通知消息的消息标识符。
        private const uint ABM_QUERYPOS = 0x00000002; // Asks an appbar for its position and size.
                                                      // 询问应用栏其位置和大小。
        private const uint ABM_SETPOS = 0x00000003; // Sets the position and size of an appbar.
                                                    // 设置应用栏的位置和大小。
        private const uint ABN_POSCHANGED = 0x00000001; // Notifies an appbar that its position or size has changed.
                                                        // 通知应用栏其位置或大小已更改。
        private const int WM_DESTROY = 0x0002; // Sent when a window is being destroyed.
                                               // 当窗口被销毁时发送。
        private const int WM_DISPLAYCHANGE = 0x007E;   // Sent to all windows when the display resolution has changed.
                                                       // 当显示分辨率更改时发送给所有窗口。
        private const int WM_DPICHANGED = 0x02E0;  // Sent to all windows when the DPI setting has changed.
                                                   // 当DPI设置更改时发送给所有窗口。
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;   // Returns a handle to the display monitor that is nearest to the window.
                                                                    // 返回最接近窗口的显示器的句柄。
        private const uint SWP_NOACTIVATE = 0x0001; // Does not activate the window. If this flag is not set, the window is activated and moved to the top of either the topmost or non-topmost group (depending on the setting of the hWndInsertAfter parameter).
                                                    // 不激活窗口。如果未设置此标志，则窗口将被激活并移动到顶层或非顶层组的顶部（取决于hWndInsertAfter参数的设置）。
        private const uint SWP_SHOWWINDOW = 0x0004; // Displays the window.
                                                    // 显示窗口。

        private static readonly IntPtr HwndTopmost = new nint(-1);
        private readonly Window _window;
        private readonly AppBarEdge _edge;  // The edge of the screen where the appbar is docked.
                                            // 应用栏停靠的屏幕边缘。
        private IntPtr _windowHandle;
        private HwndSource? _source;    // The HwndSource object that provides access to the window's handle and message loop.
                                        // 提供对窗口句柄和消息循环的访问的HwndSource对象。
        private uint _callbackMessage;  // The message identifier that the system uses to send notification messages to the appbar.
                                        // 系统用于向应用栏发送通知消息的消息标识符。

        private uint _taskbarCreatedMessage;   // The message identifier that the system uses to notify the appbar when the taskbar is created or destroyed.
                                               // 系统用于通知应用栏任务栏创建或销毁的消息标识符。

        private bool _isRegistered;
        private bool _isInitialized;
        private bool _isPositioning;
        private bool _positionPending;
        private bool _disposed;

        /// <summary>
        /// AppBar 的厚度，取决于停靠的边缘，单位为 WPF DIP，而不是物理像素。
        /// </summary>
        public double Thickness { get; private set; }

        public bool IsRegistered => _isRegistered;

        public AppBarController(Window window, AppBarEdge edge, double thickness)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (thickness < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(thickness), "AppBar 厚度必须大于 0。");
            }

            _window = window;
            _edge = edge;
            Thickness = thickness;
            _window.SourceInitialized += OnSourceInitialized;
            _window.Loaded += OnWindowLoaded;
            _window.Closed += OnWindowClosed;

            /*
             * 如果控制器是在窗口句柄已经创建之后创建的，则直接进行初始化。
             * 如果控制器是在窗口句柄创建之前创建的，则在 SourceInitialized 事件
             */
            if (PresentationSource.FromVisual(_window) is not null)
            {
                Initialize();
            }
        }

        /// <summary>
        /// 修改 AppBar 厚度并重新计算位置。
        /// </summary>
        public void SetThickness(double thickness)
        {
            ThrowIfDisposed();
            _window.Dispatcher.VerifyAccess();
            if (thickness <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(thickness), "AppBar 厚度必须大于 0。");
            }
            Thickness = thickness;
            if (_isRegistered)
            {
                PositionAppBar();
            }
        }

        /// <summary>
        /// 重新计算并设置 AppBar 位置。
        /// 必须在 WPF UI 线程调用。
        /// </summary>
        public void UpdatePosition()
        {
            _window.Dispatcher.VerifyAccess();
            if (_isRegistered)
            {
                PositionAppBar();
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            Initialize();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            RequestPositionUpdate();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            Dispose();
        }

        private void Initialize()
        {
            if (_disposed || _isInitialized)
                return;

            _window.Dispatcher.VerifyAccess();
            _windowHandle = new WindowInteropHelper(_window).Handle;
            if (_windowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法获取窗口句柄。");
            }

            _source = HwndSource.FromHwnd(_windowHandle);
            if (_source is null)
            {
                throw new InvalidOperationException("无法获取窗口消息源 HwndSource。");
            }

            _source.AddHook(WindowMessageHook);
            _callbackMessage = RegisterWindowMessage("MyTopBar.AppBar.Callback");
            if (_callbackMessage == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册 AppBar 回调消息。");
            }

            /*
             * Explorer 重启后会广播 TaskbarCreated 消息，通知所有应用栏重新注册。
             */
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            if (_taskbarCreatedMessage == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册 TaskbarCreated 消息。");
            }

            _isInitialized = true;
            RegisterAppBar();
            PositionAppBar();
        }

        private void RegisterAppBar()
        {
            if (_isRegistered || _windowHandle == IntPtr.Zero)
                return;

            var data = CreateAppBarData();
            data.uCallbackMessage = _callbackMessage;
            UIntPtr result = SHAppBarMessage(ABM_NEW, ref data);
            if (result == UIntPtr.Zero)
            {
                throw new InvalidOperationException("Windows Shell 拒绝注册 AppBar。");
            }
            _isRegistered = true;
        }

        private void UnregisterAppBar()
        {
            if (!_isRegistered || _windowHandle == IntPtr.Zero)
                return;

            var data = CreateAppBarData();
            UIntPtr result = SHAppBarMessage(ABM_REMOVE, ref data);
            if (result == UIntPtr.Zero)
            {
                throw new InvalidOperationException("Windows Shell 拒绝注销 AppBar。");
            }

            _isRegistered = false;
        }

        private void PositionAppBar()
        {
            if (!_isRegistered || _windowHandle == IntPtr.Zero || _isPositioning)
            {
                return;
            }

            try
            {
                _isPositioning = true;

                IntPtr monitor = MonitorFromWindow(_windowHandle, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero)
                    return;

                var monitorInfo = new MONITORINFO
                {
                    cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
                };
                if (!GetMonitorInfo(monitor, ref monitorInfo))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法获取显示器信息。");
                }

                uint dpi = GetDpiForWindow(_windowHandle);
                if (dpi == 0)
                    dpi = 96;

                int thicknessPixels = Math.Max(1, (int)Math.Round(Thickness * dpi / 96.0));

                var data = CreateAppBarData();
                data.uEdge = (uint)_edge;
                data.rc = CreateDesiredRectangle(monitorInfo.rcMonitor, _edge, thicknessPixels);

                /*
                 * 让 Windows Shell 根据任务栏以及其他 AppBar，计算一个可用位置
                 */
                SHAppBarMessage(ABM_QUERYPOS, ref data);

                /*
                 * ABM_QUERYPOS 可能需要修改矩形，查询后需要再次恢复指定方向上的厚度
                 */
                ApplyThickness(ref data.rc, _edge, thicknessPixels);
                SHAppBarMessage(ABM_SETPOS, ref data);

                bool success = SetWindowPos(_windowHandle, HwndTopmost,
                    data.rc.Left, data.rc.Top, data.rc.Right - data.rc.Left, data.rc.Bottom - data.rc.Top,
                    SWP_NOACTIVATE | SWP_SHOWWINDOW);
                if (!success)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置 AppBar 窗口位置。");
                }

                /*
                 * 同步更新 WPF 窗口的 DIP 尺寸和坐标，确保 WPF 布局系统完整铺满屏幕
                 */
                double dpiScale = dpi / 96.0;
                _window.Left = data.rc.Left / dpiScale;
                _window.Top = data.rc.Top / dpiScale;
                _window.Width = (data.rc.Right - data.rc.Left) / dpiScale;
                _window.Height = (data.rc.Bottom - data.rc.Top) / dpiScale;
            }
            finally
            {
                _isPositioning = false;
            }
        }

        private void RequestPositionUpdate()
        {
            if (_positionPending || _disposed)
                return;

            _positionPending = true;

            /*
            * 延迟到当前 Windows 消息处理完成之后再重新定位，
            * 避免在 DPI 或显示器消息中递归修改窗口。
            */
            _window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _positionPending = false;
                if (!_disposed)
                    PositionAppBar();
            }));
        }

        private IntPtr WindowMessageHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            /*
             * AppBar 的 Shell 回调
             */
            if ((uint)msg == _callbackMessage)
            {
                int notification = unchecked((int)wParam.ToInt64());
                if (notification == ABN_POSCHANGED)
                {
                    // The position of the appbar has changed, so we need to reposition it.
                    RequestPositionUpdate();
                    handled = true;
                }
                return IntPtr.Zero;
            }

            /*
             * Explorer 重启后，原来的 AppBar 注册已经失效。
             * 此时不能走 ABM_REMOVE，也不能把本地标记先设成已注册，
             * 否则 RegisterAppBar 会直接 return，工作区预留就丢了。
             */
            if ((uint)msg == _taskbarCreatedMessage)
            {
                _isRegistered = false;
                RegisterAppBar();
                RequestPositionUpdate();
                handled = true;
                return IntPtr.Zero;
            }

            switch (msg)
            {
                case WM_DISPLAYCHANGE:
                case WM_DPICHANGED:
                    RequestPositionUpdate();
                    break;
                case WM_DESTROY:
                    /*
                     * 在 HWND 被真正销毁前注销 AppBar。
                    */
                    UnregisterAppBar();
                    break;
            }
            return IntPtr.Zero;
        }

        private static RECT CreateDesiredRectangle(RECT monitorRectangle, AppBarEdge edge, int thickness)
        {
            var rectangle = monitorRectangle;
            ApplyThickness(ref rectangle, edge, thickness);
            return rectangle;
        }

        private APPBARDATA CreateAppBarData()
        {
            return new APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
                hWnd = _windowHandle
            };
        }

        private static void ApplyThickness(ref RECT rectangle, AppBarEdge edge, int thickness)
        {
            switch (edge)
            {
                case AppBarEdge.Left:
                    rectangle.Right =
                        rectangle.Left + thickness;
                    break;
                case AppBarEdge.Top:
                    rectangle.Bottom =
                        rectangle.Top + thickness;
                    break;
                case AppBarEdge.Right:
                    rectangle.Left =
                        rectangle.Right - thickness;
                    break;
                case AppBarEdge.Bottom:
                    rectangle.Top =
                        rectangle.Bottom - thickness;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(edge),
                        edge,
                        "不支持的 AppBar 方向。");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _window.Dispatcher.VerifyAccess();
            UnregisterAppBar();

            _window.SourceInitialized -= OnSourceInitialized;
            _window.Loaded -= OnWindowLoaded;
            _window.Closed -= OnWindowClosed;

            if (_source is not null)
            {
                _source.RemoveHook(WindowMessageHook);
                _source = null;
            }

            _windowHandle = IntPtr.Zero;
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public enum AppBarEdge : uint
        {
            Left = 0,
            Top = 1,
            Right = 2,
            Bottom = 3
        }

        #region Native API

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        #endregion
    }
}
