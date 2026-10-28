using MyTopBar.Infrastructure.Material;
using MyTopBar.Models.Settings;
using System.Windows;
using System.Windows.Media;
namespace MyTopBar.Services
{
    /// <summary>
    /// 解析最终的明暗模式，并在变化时通知界面换色。
    /// </summary>
    public sealed class ThemeService : GlassEffect.ThemeServiceLike, IDisposable
    {
        private bool _disposed;

        /// <summary>当前是否为深色。</summary>
        public bool IsDark { get; private set; }

        /// <summary>明暗模式生效时触发。</summary>
        public event EventHandler<bool>? ThemeChanged;

        public void Apply(ThemeMode mode)
        {
            ThrowIfDisposed();

            bool isDark = ResolveIsDark(mode);
            bool changed = isDark != IsDark;
            IsDark = isDark;
            ApplyBrushes(isDark);

            if (changed)
            {
                ThemeChanged?.Invoke(this, isDark);
            }
        }

        /// <summary>
        /// 把顶栏前景/分隔色与弹窗配色按明暗切换，供 DynamicResource 自动刷新。
        /// 弹窗（Apple 菜单 / 日历 / 音量）此前是硬编码浅色，深色主题下白底压白字，
        /// 这里统一收口：XAML 一律用 DynamicResource，切换时整体重刷。
        /// </summary>
        public static void ApplyBrushes(bool isDark)
        {
            Application app = Application.Current;
            if (app is null)
            {
                return;
            }

            ResourceDictionary r = app.Resources;

            // 顶栏组件
            SetBrush(r, "BarForegroundBrush", isDark ? "#FFFFFFFF" : "#E6000000");
            SetBrush(r, "BarSecondaryForegroundBrush", isDark ? "#B3FFFFFF" : "#99000000");
            SetBrush(r, "BarSeparatorBrush", isDark ? "#33FFFFFF" : "#1A000000");

            // 弹窗容器：MenuBackgroundBrush 仍供设置窗口等不接受毛玻璃的地方使用
            SetBrush(r, "MenuBackgroundBrush", isDark ? "#FF2B2B2E" : "#FFF7F7F7");
            SetBrush(r, "MenuBorderBrush", isDark ? "#40FFFFFF" : "#26000000");

            /*
             * 弹窗的毛玻璃底色：必须接近透明，否则会盖住模糊层。
             * 深色偏暗、浅色偏亮，让文字在复杂壁纸上仍有对比度。
             */
            SetBrush(r, "PopupGlassBrush", isDark ? "#24000000" : "#1FFFFFFF");

            // 弹窗内容
            SetBrush(r, "MenuForegroundBrush", isDark ? "#E6FFFFFF" : "#E6000000");
            SetBrush(r, "MenuSecondaryForegroundBrush", isDark ? "#80FFFFFF" : "#80000000");
            SetBrush(r, "MenuHoverBrush", isDark ? "#33FFFFFF" : "#1A000000");
            SetBrush(r, "MenuPressedBrush", isDark ? "#4DFFFFFF" : "#33000000");
            SetBrush(r, "MenuSeparatorBrush", isDark ? "#33FFFFFF" : "#18000000");

            // 日历：今天的高亮底色在深色下要更亮一点才够对比
            SetBrush(r, "CalendarTodayBrush", isDark ? "#FF0A84FF" : "#FF0A84FF");
            SetBrush(r, "CalendarTodayForegroundBrush", "#FFFFFFFF");

            // 输入控件（设置窗口）。深色下输入框要比弹窗底色浅一档，才有层次。
            SetBrush(r, "InputBackgroundBrush", isDark ? "#FF3A3A3E" : "#FFFFFFFF");
            SetBrush(r, "InputForegroundBrush", isDark ? "#E6FFFFFF" : "#E6000000");
            SetBrush(r, "InputBorderBrush", isDark ? "#33FFFFFF" : "#33000000");
            SetBrush(r, "InputBorderHoverBrush", isDark ? "#59FFFFFF" : "#59000000");
            SetBrush(r, "AccentSoftBrush", isDark ? "#4D0A84FF" : "#330A84FF");
        }

        private static void SetBrush(ResourceDictionary resources, string key, string color)
        {
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        /// <summary>
        /// 把主题偏好解析成实际明暗。
        ///
        /// 只处理 Light / Dark 两档。旧配置里的 System(0) 不应该走到这里
        /// （SettingsService.Normalize 已将其归一到 Light），
        /// 万一漏网也按浅色处理，绝不回读注册表。
        /// </summary>
        private static bool ResolveIsDark(ThemeMode mode) => mode == ThemeMode.Dark;

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
