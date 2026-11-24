namespace MyTopBar.Models.Settings
{
    /// <summary>应用主题偏好。</summary>
    /// <remarks>
    /// 原先是 System / Light / Dark 三档，System 会读注册表跟随系统深浅色。
    /// 但 RefreshFromSystem 一直没有调用点，所谓「跟随系统」实际只在启动时读一次，
    /// 挂着这个选项反而误导，已移除。
    ///
    /// 保留 System = 0 这个枚举位用于兼容旧配置：
    /// 老版本 settings.json 里 "theme": 0 表示跟随系统，
    /// 迁移时按 Light 处理（见 SettingsService.Normalize），不能让它变成未定义值。
    /// </remarks>
    public enum ThemeMode
    {
        /// <summary>已废弃。仅为兼容旧配置保留，读取时归一到 <see cref="Light"/>。</summary>
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

    /// <summary>背景通透感档位。</summary>
    public enum BackdropTranslucency
    {
        /// <summary>极透：最大程度透出壁纸/底层内容。</summary>
        UltraClear = 0,

        /// <summary>通透：默认档位，通透自然。</summary>
        Clear = 1,

        /// <summary>均衡：通透与文字对比度兼顾。</summary>
        Balanced = 2,

        /// <summary>厚重：强遮罩，复杂壁纸下最易读。</summary>
        Frosted = 3,

        /// <summary>不透明：几乎不透底。</summary>
        Opaque = 4
    }

    /// <summary>界面正文字重档位（对应内置 HarmonyOS Sans SC 的五档真实字形）。</summary>
    public enum FontWeightPreset
    {
        Light = 0,
        Regular = 1,
        Medium = 2,
        Bold = 3,
        Black = 4
    }

    /// <summary>外观设置。</summary>
    public sealed class AppearanceSettings
    {
        public ThemeMode Theme { get; set; } = ThemeMode.Light;

        /// <summary>亚克力背景通透感档位，默认通透。</summary>
        public BackdropTranslucency Translucency { get; set; } = BackdropTranslucency.Clear;

        /// <summary>
        /// 顶栏高度（WPF DIP）。
        /// 取值用 <see cref="BarHeightRange"/> 夹取，不要在别处再写一份数字。
        /// </summary>
        public double BarHeight { get; set; } = BarHeightRange.Default;

        /// <summary>
        /// 字体名。空或 "HarmonyOS Sans SC" 表示使用内置字体，
        /// 其余值为系统已安装的字体名。
        /// </summary>
        public string FontFamily { get; set; } = string.Empty;

        /// <summary>
        /// 主字号（WPF DIP），默认 13。
        /// </summary>
        public double FontSizePixels { get; set; } = 13;

        /// <summary>顶栏与界面正文字重，默认常规（Regular / 400）。</summary>
        public FontWeightPreset FontWeight { get; set; } = FontWeightPreset.Regular;
    }

    /// <summary>
    /// 顶栏高度的唯一取值范围。
    ///
    /// 之前 SettingsService 夹到 24–60、SettingsViewModel / MainWindow 夹到 28–40、
    /// 设置界面滑杆又是 28–40，三处各写一份：手改 settings.json 成 50 时顶栏只显示 40，
    /// 而配置文件里永远留着 50。现在统一在这里定义，其余地方只引用。
    /// </summary>
    public static class BarHeightRange
    {
        public const double Min = 28;
        public const double Max = 40;
        public const double Default = 30;

        public static double Clamp(double value) => Math.Clamp(value, Min, Max);
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

    /// <summary>
    /// 和风天气配置。
    ///
    /// 对应官方新版 API（2026 起逐步取代旧公共域名）：
    ///   GET https://{ApiHost}/weather/v1/current/{Latitude}/{Longitude}
    ///
    /// 旧版 devapi.qweather.com/v7/weather/now?key= 已停用，这里不再保留兼容分支。
    /// </summary>
    public sealed class QWeatherSettings
    {
        /// <summary>
        /// 开发者专属 API Host，形如 h2a9cf3mhs.xy.qweatherapi.com。
        /// 在控制台-设置中查看。每个账号独立，也是身份认证的一部分。
        /// </summary>
        public string ApiHost { get; set; } = string.Empty;

        /// <summary>API KEY 凭据（控制台-项目管理中创建）。</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>纬度，十进制，最多两位小数。北京 39.92。</summary>
        public string Latitude { get; set; } = "39.92";

        /// <summary>经度，十进制，最多两位小数。北京 116.41。</summary>
        public string Longitude { get; set; } = "116.41";

        /// <summary>
        /// 由 GeoAPI 选中的地点名称，仅用于界面显示和让用户确认选对了位置。
        /// 天气请求只用经纬度，这个字段不参与请求。
        /// </summary>
        public string PlaceName { get; set; } = string.Empty;

        /// <summary>刷新间隔（分钟），5–240。</summary>
        public int IntervalMinutes { get; set; } = 30;

        /// <summary>返回语言，例如 zh / en。留空使用服务端默认。</summary>
        public string Language { get; set; } = "zh";
    }

    /// <summary>歌词组件设置。</summary>
    public sealed class LyricsSettings
    {
        /// <summary>
        /// 只读取该来源的媒体会话（SourceAppUserModelId）。
        /// 空字符串表示跟随系统当前会话。
        ///
        /// 例如同时开着 Chrome 和网易云，系统当前会话切换会导致歌词乱跳，
        /// 填上网易云的 AUMID 就只跟踪它。
        /// </summary>
        public string SourceAppId { get; set; } = string.Empty;
    }

    /// <summary>外部 API 凭据。</summary>
    public sealed class ApiSettings
    {
        public QWeatherSettings QWeather { get; set; } = new();

        public HitokotoSettings Hitokoto { get; set; } = new();

        /// <summary>
        /// 旧版字段，仅用于把老配置迁移到 <see cref="QWeather"/>。
        /// 迁移后置空，不再使用。
        /// </summary>
        public string QWeatherKey { get; set; } = string.Empty;
    }

    /// <summary>系统指标采样设置。</summary>
    public sealed class MetricsSettings
    {
        public int IntervalMs { get; set; } = 1000;
    }

    /// <summary>settings.json 根对象。</summary>
    public sealed class AppSettings
    {
        public AppearanceSettings Appearance { get; set; } = new();

        public List<WidgetSettings> Widgets { get; set; } = new();

        public ApiSettings Api { get; set; } = new();

        public LyricsSettings Lyrics { get; set; } = new();
        public MetricsSettings Metrics { get; set; } = new();
    }
}
