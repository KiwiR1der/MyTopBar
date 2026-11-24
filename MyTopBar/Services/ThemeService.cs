using FluentWpfCore.AttachedProperties;
using FluentWpfCore.Interop;
using MyTopBar.Models.Settings;
using System.Windows;
using System.Windows.Media;

namespace MyTopBar.Services
{
    /// <summary>
    /// 解析最终的明暗模式与亚克力通透感档位，并在变化时通知界面刷新。
    /// </summary>
    public sealed class ThemeService : IDisposable
    {
        private bool _disposed;

        /// <summary>当前是否为深色。</summary>
        public bool IsDark { get; private set; }

        /// <summary>当前背景通透感档位。</summary>
        public BackdropTranslucency Translucency { get; private set; } = BackdropTranslucency.Clear;

        /// <summary>明暗模式生效时触发。</summary>
        public event EventHandler<bool>? ThemeChanged;

        /// <summary>主题明暗或通透感变化时触发，供各窗口同步亚克力底色。</summary>
        public event EventHandler? BackdropChanged;

        public void Apply(ThemeMode mode)
            => Apply(mode, Translucency);

        public void Apply(ThemeMode mode, BackdropTranslucency translucency)
        {
            ThrowIfDisposed();

            bool isDark = ResolveIsDark(mode);
            bool themeChanged = isDark != IsDark;
            bool backdropChanged = themeChanged || translucency != Translucency;

            IsDark = isDark;
            Translucency = translucency;

            ApplyBrushes(isDark);

            if (themeChanged)
            {
                ThemeChanged?.Invoke(this, isDark);
            }

            if (backdropChanged)
            {
                BackdropChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void ApplyTranslucency(BackdropTranslucency translucency)
        {
            ThrowIfDisposed();

            if (translucency == Translucency)
            {
                return;
            }

            Translucency = translucency;
            BackdropChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 将当前主题与通透感同步到指定窗口的 FluentWpfCore WindowMaterial。
        /// 统一收口主顶栏、弹窗与设置窗口的亚克力色表。
        /// </summary>
        public void SyncWindowMaterial(WindowMaterial windowMaterial, bool isBar)
        {
            ArgumentNullException.ThrowIfNull(windowMaterial);

            windowMaterial.IsDarkMode = IsDark;
            windowMaterial.MaterialMode = MaterialType.Acrylic;
            windowMaterial.UseWindowComposition = true;
            windowMaterial.CompositonColor = GetAcrylicTintColor(IsDark, Translucency, isBar);
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

            // 顶栏组件：主前景使用不透明色，避免半透明字叠透明毛玻璃表面削弱边缘锐度
            SetBrush(r, "BarForegroundBrush", isDark ? "#FFFFFFFF" : "#FF141414");
            SetBrush(r, "BarSecondaryForegroundBrush", isDark ? "#D9FFFFFF" : "#CC000000");
            SetBrush(r, "BarSeparatorBrush", isDark ? "#33FFFFFF" : "#1A000000");

            // 弹窗容器：MenuBackgroundBrush 仍供设置窗口下拉框等独立 Popup 使用
            SetBrush(r, "MenuBackgroundBrush", isDark ? "#FF2B2B2E" : "#FFF7F7F7");
            SetBrush(r, "MenuBorderBrush", isDark ? "#40FFFFFF" : "#26000000");

            /*
             * 弹窗与次级表面毛玻璃画刷：
             * - PopupGlassBrush 保持完全透明，由 DWM 原生 Acrylic（CompositonColor）统一负责模糊与染色；
             * - SurfaceTintBrush 仅用于设置窗口导航栏、卡片与媒体按钮等需要区分层级的局部容器。
             */
            SetBrush(r, "PopupGlassBrush", "#00FFFFFF");
            SetBrush(r, "SurfaceTintBrush", isDark ? "#18FFFFFF" : "#12000000");

            // 弹窗内容
            SetBrush(r, "MenuForegroundBrush", isDark ? "#FFFFFFFF" : "#FF141414");
            SetBrush(r, "MenuSecondaryForegroundBrush", isDark ? "#B3FFFFFF" : "#99000000");
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

        /// <summary>亚克力模式下传给 FluentWpfCore CompositonColor 的染色。</summary>
        public static Color GetAcrylicTintColor(bool isDark, BackdropTranslucency translucency, bool isBar)
        {
            byte alpha = (translucency, isDark, isBar) switch
            {
                (BackdropTranslucency.UltraClear, true, true) => 0x33,
                (BackdropTranslucency.UltraClear, false, true) => 0x40,
                (BackdropTranslucency.UltraClear, true, false) => 0x40,
                (BackdropTranslucency.UltraClear, false, false) => 0x4D,

                (BackdropTranslucency.Clear, true, true) => 0x59,
                (BackdropTranslucency.Clear, false, true) => 0x66,
                (BackdropTranslucency.Clear, true, false) => 0x66,
                (BackdropTranslucency.Clear, false, false) => 0x73,

                (BackdropTranslucency.Balanced, true, true) => 0x80,
                (BackdropTranslucency.Balanced, false, true) => 0x8C,
                (BackdropTranslucency.Balanced, true, false) => 0x8C,
                (BackdropTranslucency.Balanced, false, false) => 0x99,

                (BackdropTranslucency.Frosted, true, true) => 0xB3,
                (BackdropTranslucency.Frosted, false, true) => 0xBF,
                (BackdropTranslucency.Frosted, true, false) => 0xBF,
                (BackdropTranslucency.Frosted, false, false) => 0xCC,

                (BackdropTranslucency.Opaque, _, true) => 0xF2,
                (BackdropTranslucency.Opaque, _, false) => 0xF5,
                _ => isBar ? (byte)0x59 : (byte)0x66
            };

            return isDark
                ? Color.FromArgb(alpha, 0x1E, 0x1E, 0x1E)
                : Color.FromArgb(alpha, 0xF6, 0xF6, 0xF6);
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
