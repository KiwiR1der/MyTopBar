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
        private bool _closing;

        public PopupHost(Popup popup)
        {
            ArgumentNullException.ThrowIfNull(popup);
            _popup = popup;
            _dispatcher = popup.Dispatcher;

            _popup.Opened += OnOpened;
            _popup.Closed += OnClosed;
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
