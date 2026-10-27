using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MyTopBar.Infrastructure.Interop;

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

            ApplyBlur(source);

            SetWindowPos(
                source.Handle,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        /// <summary>
        /// 给 Popup 的宿主窗口应用毛玻璃，步骤与主窗口一致：
        /// 客户区背景置透明 → 扩展边框 → 清掉残留的系统背景 → 合成层模糊。
        /// </summary>
        private static void ApplyBlur(HwndSource source)
        {
            try
            {
                if (source.CompositionTarget is not null)
                {
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                }

                var margins = new NativeMethods.MARGINS
                {
                    Left = -1,
                    Right = -1,
                    Top = -1,
                    Bottom = -1,
                };
                NativeMethods.DwmExtendFrameIntoClientArea(source.Handle, ref margins);

                // 清掉可能残留的 DWM 原生材质，避免盖住合成层。
                int none = NativeMethods.DWMSBT_NONE;
                NativeMethods.DwmSetWindowAttribute(
                    source.Handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));

                /*
                 * 关键：让 DWM 把窗口裁成圆角。
                 *
                 * 模糊层严格按窗口矩形铺满，而 WPF 内容是有圆角的 Border，
                 * 两者不一致时四个角就会露出矩形的模糊块（观测到的灰角）。
                 * 这里给窗口本身设置圆角，DWM 会按圆角裁剪整层，
                 * 与 Border 的 CornerRadius 对齐后就看不出来了。
                 */
                int corner = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(
                    source.Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

                if (!NativeMethods.ApplyBlurBehind(source.Handle))
                {
                    AppLog.Error("弹窗毛玻璃：SetWindowCompositionAttribute 调用失败");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("弹窗毛玻璃应用失败", ex);
            }
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
