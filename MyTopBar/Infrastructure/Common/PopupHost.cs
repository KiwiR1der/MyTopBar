using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MyTopBar.Infrastructure.Interop;
using MyTopBar.Infrastructure.Material;

namespace MyTopBar.Infrastructure.Common
{
    /// <summary>
    /// WPF Popup 的宿主封装。
    ///
    /// 除置顶与防抖外，还在 Popup 打开时给它的宿主 HWND 应用毛玻璃，
    /// 让菜单 / 日历 / 音量弹窗与顶栏、设置窗口保持一致的背景效果。
    ///
    /// Popup 不是普通 WPF 窗口：WPF 会为它单独创建一个 HWND，
    /// 只有对这个 HWND 调用 SetWindowCompositionAttribute 才可能生效。
    /// </summary>
    public sealed class PopupHost
    {
        private const uint SwpNoMove = 0x0001;
        private const uint SwpNoSize = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private static readonly IntPtr HwndTopmost = new(-1);

        private readonly Popup _popup;
        private readonly Dispatcher _dispatcher;
        private IntPtr _hostHandle = IntPtr.Zero;
        private bool _closing;

        public PopupHost(Popup popup)
        {
            ArgumentNullException.ThrowIfNull(popup);
            _popup = popup;
            _dispatcher = popup.Dispatcher;

            _popup.Opened += OnOpened;
            _popup.Closed += OnClosed;
            PopupDismissManager.Register(this);
        }

        public bool IsOpen => _popup.IsOpen;

        /// <summary>弹窗宿主 HWND（打开后才有值），供外部点击判定用。</summary>
        public IntPtr HostHandle => _hostHandle;

        /// <summary>
        /// 由 PopupDismissManager 在外部点击时调用。
        /// 钩子回调不在 UI 线程，需投递回 UI 线程再关。
        /// </summary>
        internal void CloseFromExternal()
        {
            if (!_popup.IsOpen)
            {
                return;
            }

            _dispatcher.BeginInvoke(new Action(() => _popup.IsOpen = false));
        }

        public void Toggle()
        {
            if (_closing)
            {
                return;
            }

            _popup.IsOpen = !_popup.IsOpen;
        }

        public void Close()
        {
            _popup.IsOpen = false;
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            if (_popup.Child is not Visual child)
            {
                return;
            }

            if (PresentationSource.FromVisual(child) is not HwndSource source)
            {
                return;
            }

            GlassEffect.ApplyToPopupHost(source);

            // 记录宿主句柄，供「点外部即收起」判定落点是否在弹窗内。
            _hostHandle = source.Handle;

            // 弹窗开了，挂上全局鼠标钩子以便「点外部即收起」。
            PopupDismissManager.NotifyOpened();

            SetWindowPos(
                source.Handle,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _closing = true;
            _dispatcher.BeginInvoke(() => _closing = false);

            // 全部关闭后摘掉钩子，不常驻监听。
            PopupDismissManager.NotifyClosed();
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);
    }
}
