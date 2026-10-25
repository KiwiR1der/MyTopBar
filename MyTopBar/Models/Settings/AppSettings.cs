using MyTopBar.Infrastructure.Material;

namespace MyTopBar.Models.Settings
{
    /// <summary>应用主题偏好。</summary>
    public enum ThemeMode
    {
        /// <summary>跟随系统（读取注册表 AppsUseLightTheme）。</summary>
        System = 0,
        Light = 1,
        Dark = 2
    }

    /// <summary>文本组件的数据来源。</summary>
    public enum TextWidgetMode
    {
        /// <summary>用户自填的静态文本。</summary>
        Static = 0,

        /// <summary>一言 API（v1.hitokoto.cn）。</summary>
        Hitokoto = 1
    }

    /// <summary>外观设置。</summary>
    public sealed class AppearanceSettings
    {
        public MaterialKind Material { get; set; } = MaterialKind.Mica;

        public ThemeMode Theme { get; set; } = ThemeMode.System;

        /// <summary>顶栏高度（WPF DIP），28–40。</summary>
        public double BarHeight { get; set; } = 30;
    }

    /// <summary>单个组件的持久化状态。</summary>
    public sealed class WidgetSettings
    {
        public string Id { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public int Order { get; set; }

        /// <summary>组件自有配置，键值均为字符串。</summary>
        public Dictionary<string, string> Options { get; set; } = new();
    }

    /// <summary>一言相关设置。</summary>
    public sealed class HitokotoSettings
    {
        public bool Enabled { get; set; } = true;

        public int IntervalMinutes { get; set; } = 30;
    }

    /// <summary>外部 API 凭据。</summary>
    public sealed class ApiSettings
    {
        public string QWeatherKey { get; set; } = string.Empty;

        public HitokotoSettings Hitokoto { get; set; } = new();
    }

    /// <summary>系统指标采样设置。</summary>
    public sealed class MetricsSettings
    {
        public int IntervalMs { get; set; } = 1000;

        /// <summary>true 使用 1024 进制（G），false 使用 1000 进制（GB）。</summary>
        public bool UseIecUnits { get; set; } = true;
    }

    /// <summary>settings.json 根对象。</summary>
    public sealed class AppSettings
    {
        public AppearanceSettings Appearance { get; set; } = new();

        public List<WidgetSettings> Widgets { get; set; } = new();

        public ApiSettings Api { get; set; } = new();

        public MetricsSettings Metrics { get; set; } = new();
    }
}
