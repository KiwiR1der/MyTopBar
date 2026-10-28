using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Infrastructure.Interop;

namespace MyTopBar.Infrastructure.Material
{
    /// <summary>
    /// 窗口毛玻璃效果（ACCENT_ENABLE_BLURBEHIND）注册器。
    ///
    /// 给任意 WPF Window / Popup 宿主 HWND 应用同一套配方：
    ///   客户区背景置透明 → 扩展玻璃帧 → 关掉 DWM 原生背景 → 同步明暗模式 → 合成层模糊。
    ///
    /// 为什么固定用 BLURBEHIND 而不是 Acrylic/Mica，见 <see cref="MaterialInfo"/>。
    ///
    /// 使用前提（调用方负责）：
    ///   - 窗口 Background 必须为 Transparent，且不能设 AllowsTransparency="True"
    ///     （分层窗口会让 DWM 模糊失效）
    ///   - CompositionTarget.BackgroundColor 必须在窗口完成首次合成后再置透明，
    ///     否则会被 WPF 首帧覆盖成纯黑 RGB(0,0,0)；因此 Register(window) 会在
    ///     SourceInitialized 与 Loaded 两个阶段各应用一次，调用方无需关心时机
    /// </summary>
    public static class GlassEffect
    {
        /// <summary>
        /// 给一个 WPF 窗口注册毛玻璃。
        ///
        /// 挂接 SourceInitialized / Loaded / ThemeChanged，
        /// 之后主题明暗切换时会自动重新应用（边框/阴影颜色随主题）。
        /// 返回一个可 Dispose 的订阅句柄；不 Dispose 也没关系——
        /// 事件挂在窗口自身上，窗口关闭后自然释放。
        /// </summary>
        public static GlassRegistration Register(Window window, ThemeServiceLike theme)
        {
            ArgumentNullException.ThrowIfNull(window);
            ArgumentNullException.ThrowIfNull(theme);

            IntPtr Handle() => new WindowInteropHelper(window).Handle;

            window.SourceInitialized += (_, _) => Apply(Handle(), theme.IsDark, roundCorners: false);
            window.Loaded += (_, _) =>
            {
                // 窗口已渲染，此时再置透明才不会被首帧合成覆盖。
                SetTransparentClientArea(window);
                Apply(Handle(), theme.IsDark, roundCorners: false);
            };
            window.Closed += (_, _) => NativeMethods.ClearAccent(Handle());

            EventHandler<bool> onTheme = (_, isDark) => Apply(Handle(), isDark, roundCorners: false);
            theme.ThemeChanged += onTheme;

            return new GlassRegistration(() => theme.ThemeChanged -= onTheme);
        }

        /// <summary>
        /// 给 Popup 的宿主 HWND 应用毛玻璃（Popup 由 WPF 单独创建 HWND）。
        ///
        /// Popup 的内容通常是有圆角的 Border，这里额外让 DWM 把窗口裁成圆角，
        /// 否则四角会露出矩形模糊块。
        /// </summary>
        public static bool ApplyToPopupHost(HwndSource source)
        {
            ArgumentNullException.ThrowIfNull(source);

            try
            {
                if (source.CompositionTarget is not null)
                {
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                }

                return ApplyCore(source.Handle, isDark: null, roundCorners: true);
            }
            catch (Exception ex)
            {
                AppLog.Error("弹窗毛玻璃应用失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 对已存在的 HWND 直接应用一次毛玻璃（不管明暗）。
        /// 一般用于窗口内部自行管理事件的场景。
        /// </summary>
        public static bool Apply(IntPtr hwnd, bool isDark, bool roundCorners)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return ApplyCore(hwnd, isDark, roundCorners);
            }
            catch (Exception ex)
            {
                AppLog.Error("毛玻璃应用失败", ex);
                return false;
            }
        }

        /// <summary>让 WPF 不绘制不透明客户区底。</summary>
        public static void SetTransparentClientArea(Window window)
        {
            if (PresentationSource.FromVisual(window) is HwndSource source
                && source.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
        }

        private static bool ApplyCore(IntPtr hwnd, bool? isDark, bool roundCorners)
        {
            // 1. 扩展玻璃帧进客户区
            var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // 2. 关掉 DWM 原生背景（Mica/Acrylic），避免盖住合成层
            int none = NativeMethods.DWMSBT_NONE;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));

            // 3. 明暗标志：让边框/阴影随主题（Popup 等场景可传 null 跳过）
            if (isDark.HasValue)
            {
                int dark = isDark.Value ? 1 : 0;
                NativeMethods.DwmSetWindowAttribute(
                    hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }

            // 4. 圆角裁剪：模糊层严格按窗口矩形铺满，内容 Border 是圆角时四角会露矩形块
            if (roundCorners)
            {
                int corner = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(
                    hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }

            // 5. 上模糊层
            if (!NativeMethods.ApplyBlurBehind(hwnd))
            {
                AppLog.Error("毛玻璃：SetWindowCompositionAttribute 调用失败");
                return false;
            }

            return true;
        }

        /// <summary>ThemeService 的最小契约，避免 Material 层反向依赖 Services 层。</summary>
        public interface ThemeServiceLike
        {
            bool IsDark { get; }
            event EventHandler<bool> ThemeChanged;
        }

        /// <summary>注销订阅用的小句柄。</summary>
        public sealed class GlassRegistration : IDisposable
        {
            private readonly Action _unsubscribe;
            private bool _disposed;

            internal GlassRegistration(Action unsubscribe) => _unsubscribe = unsubscribe;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _unsubscribe();
            }
        }
    }
}
