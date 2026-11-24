using System.Windows;
using System.Windows.Media;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Infrastructure.Interop;

namespace MyTopBar.Services
{
    /// <summary>
    /// 字体选择与字号发布。
    ///
    /// 两个职责：
    ///   1. 维护当前字体（内置 HarmonyOS Sans SC，或用户选择的系统字体），
    ///      写入 Application.Resources["AppFont"]，界面用 DynamicResource 自动跟随。
    ///   2. 将用户选择的字号（WPF DIP）发布到全局资源 AppFontSize / AppFontSizeSmall。
    ///      WPF 会按系统 DPI 自动将 DIP 映射到对应物理像素，高分屏（125%/150%/200%）
    ///      保留完整 DIP 字号即可获得更细腻的字形采样，无需反向除以缩放压小字号。
    /// </summary>
    public sealed class FontService
    {
        /// <summary>内置字体的名称，也作为「使用内置字体」的配置值。</summary>
        public const string BuiltinFontName = "HarmonyOS Sans SC";

        /// <summary>默认字号（WPF DIP）。13 DIP 在 100% 下为 13px，150% 下为 19.5px。</summary>
        public const double DefaultFontSizeDip = 13.0;

        private static readonly FontFamily BuiltinFont =
            new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#HarmonyOS Sans SC");

        /// <summary>当前生效的字体名（用于设置界面回显）。</summary>
        public string CurrentFontName { get; private set; } = BuiltinFontName;

        /// <summary>当前生效的主字号（DIP）。</summary>
        public double CurrentFontSize { get; private set; } = DefaultFontSizeDip;

        /// <summary>字体或字号变化时触发。</summary>
        public event EventHandler? FontChanged;

        /// <summary>
        /// 应用字体与字号。传空或 <see cref="BuiltinFontName"/> 时使用内置字体，
        /// 否则按系统已安装的字体名解析；<paramref name="fontSizeDip"/> 为空时保持当前字号。
        /// </summary>
        public void Apply(string? fontName, double? fontSizeDip = null)
        {
            string name = string.IsNullOrWhiteSpace(fontName) ? BuiltinFontName : fontName.Trim();

            FontFamily family;
            if (string.Equals(name, BuiltinFontName, StringComparison.OrdinalIgnoreCase))
            {
                family = BuiltinFont;
                name = BuiltinFontName;
            }
            else if (!IsInstalled(name))
            {
                /*
                 * 字体不存在时 WPF 会静默回退到默认字体，用户看不出「没生效」，
                 * 只会觉得「选了没变化」。这里先校验并回退到内置字体，同时记日志。
                 */
                AppLog.Warn($"字体 {name} 未安装，回退到内置字体");
                family = BuiltinFont;
                name = BuiltinFontName;
            }
            else
            {
                family = new FontFamily(name);
            }

            if (fontSizeDip is { } size && !double.IsNaN(size) && size > 0)
            {
                CurrentFontSize = Math.Clamp(Math.Round(size, 2), 10.0, 24.0);
            }

            CurrentFontName = name;
            Publish(family, CurrentFontSize);

            FontChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 刷新当前字体与字号资源。窗口在切换到不同缩放的显示器后可调用。
        /// </summary>
        public void RefreshScale()
        {
            Publish(CurrentFontName, CurrentFontSize);
            FontChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void Publish(string fontName, double fontSizeDip)
        {
            FontFamily family = string.Equals(fontName, BuiltinFontName, StringComparison.OrdinalIgnoreCase)
                ? BuiltinFont
                : new FontFamily(fontName);

            Publish(family, fontSizeDip);
        }

        private static void Publish(FontFamily family, double fontSizeDip)
        {
            if (Application.Current is not { } app)
            {
                return;
            }

            double baseSize = ComputeBaseSize(fontSizeDip);
            double smallSize = Math.Max(10.0, Math.Round(baseSize - 1.0, 2));

            // 覆盖资源字典项，DynamicResource 会自动重新求值。
            app.Resources["AppFont"] = family;
            app.Resources["AppFontSize"] = baseSize;
            app.Resources["AppFontSizeSmall"] = smallSize;
        }

        /// <summary>系统已安装的字体名列表（去重排序），供设置界面下拉。内置字体固定第一项。</summary>
        public static IReadOnlyList<string> GetInstalledFontNames()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { BuiltinFontName };
            var rest = new List<string>();

            try
            {
                foreach (FontFamily family in Fonts.SystemFontFamilies)
                {
                    string name = family.Source;
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    {
                        rest.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("枚举系统字体失败", ex);
            }

            rest.Sort(StringComparer.CurrentCulture);

            var result = new List<string> { BuiltinFontName };
            result.AddRange(rest);
            return result;
        }

        private static bool IsInstalled(string name)
        {
            try
            {
                return Fonts.SystemFontFamilies.Any(f =>
                    string.Equals(f.Source, name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // 枚举失败时不阻断，交给 WPF 自己回退。
                return true;
            }
        }

        /// <summary>
        /// 返回规范化后的主字号（WPF DIP）。
        /// 不再反向除以 DPI 缩放，避免高分屏上字号过小、像素采样不足导致锯齿。
        /// </summary>
        public static double ComputeBaseSize(double fontSizeDip = DefaultFontSizeDip)
        {
            if (double.IsNaN(fontSizeDip) || fontSizeDip <= 0)
            {
                return DefaultFontSizeDip;
            }

            return Math.Clamp(Math.Round(fontSizeDip, 2), 10.0, 24.0);
        }

        /// <summary>当前窗口所在显示器的缩放（1.0 = 100%）。</summary>
        public static double GetSystemScale()
        {
            try
            {
                if (Application.Current?.MainWindow is { } window
                    && PresentationSource.FromVisual(window) is System.Windows.Interop.HwndSource source
                    && source.CompositionTarget is not null)
                {
                    double s = source.CompositionTarget.TransformToDevice.M11;
                    if (s > 0)
                    {
                        return s;
                    }
                }
            }
            catch
            {
                // 主窗口尚未创建（启动早期），用系统 DPI 兜底。
            }

            return GetScaleFromSystemDpi();
        }

        /// <summary>
        /// 用屏幕 DPI 推算缩放，作为主窗口不可用时的兜底。
        ///
        /// 用 GetDC(0) + LOGPIXELSX 而不是 System.Drawing.Common：
        /// UseWPF 项目默认不隐式引用它，为一个 DPI 查询引入整包依赖不划算。
        /// </summary>
        private static double GetScaleFromSystemDpi()
        {
            try
            {
                IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero)
                {
                    return 1.0;
                }

                try
                {
                    int dpi = NativeMethods.GetDeviceCaps(dc, NativeMethods.LOGPIXELSX);
                    return dpi > 0 ? dpi / 96.0 : 1.0;
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, dc);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("读取系统 DPI 失败，按 100% 处理", ex);
                return 1.0;
            }
        }
    }
}
