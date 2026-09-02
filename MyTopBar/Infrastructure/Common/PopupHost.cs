using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyTopBar.Infrastructure.Common
{
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
