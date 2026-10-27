using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Models.Settings;

namespace MyTopBar.Services
{
    /// <summary>
    /// settings.json 的读写。写入采用 500ms 防抖，退出时强制刷盘。
    /// </summary>
    public sealed class SettingsService : IDisposable
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly string _filePath;
        private readonly object _gate = new();
        private System.Threading.Timer? _debounceTimer;
        private bool _disposed;

        public SettingsService()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MyTopBar");
            _filePath = Path.Combine(directory, "settings.json");
            Directory.CreateDirectory(directory);

            bool existed = File.Exists(_filePath);
            Current = Load();

            /*
             * 落盘两种情况：
             * 1. 首次启动（或配置损坏回退）——生成一份默认配置方便用户直接编辑
             * 2. 文件已存在，但 Normalize 改写过内容（例如把废弃的
             *    theme=0 / material=1 迁移成新值）——回写一次，
             *    避免磁盘上一直留着废弃值、每次启动都要重新迁移
             */
            if (!existed)
            {
                Flush();
            }
            else if (NeedsRewrite)
            {
                Flush();
            }
        }

        /// <summary>本次加载是否发生过迁移改写，用于决定是否回写磁盘。</summary>
        private bool NeedsRewrite { get; set; }

        /// <summary>当前生效的配置。修改后调用 <see cref="Save"/> 持久化。</summary>
        public AppSettings Current { get; private set; }

        /// <summary>配置被外部替换或首次加载完成后触发。</summary>
        public event EventHandler? Changed;

        private AppSettings Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return CreateDefault();
                }

                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return CreateDefault();
                }

                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                AppSettings normalized = Normalize(loaded ?? CreateDefault(), out bool changed);
                NeedsRewrite = changed;
                return normalized;
            }
            catch (Exception)
            {
                // 配置文件损坏时退回默认值，避免顶栏因配置问题无法启动。
                return CreateDefault();
            }
        }

        private static AppSettings CreateDefault() => new();

        /// <summary>
        /// 补齐缺失字段、裁剪非法值、迁移已废弃的枚举值。
        /// </summary>
        /// <param name="changed">
        /// 输出是否发生过迁移改写。true 时调用方应回写磁盘，
        /// 否则废弃值会一直留在文件里，每次启动重复迁移。
        /// </param>
        private static AppSettings Normalize(AppSettings settings, out bool changed)
        {
            changed = false;

            settings.Appearance ??= new AppearanceSettings();
            settings.Api ??= new ApiSettings();
            settings.Api.QWeather ??= new QWeatherSettings();
            settings.Api.Hitokoto ??= new HitokotoSettings();
            settings.Metrics ??= new MetricsSettings();
            settings.Widgets ??= new List<WidgetSettings>();

            /*
             * 和风天气从旧版公共域名 API 迁到新版独立 Host + 请求头认证。
             *
             * 旧配置只有一个 qWeatherKey 字符串；新版还需要 API Host 和经纬度。
             * Key 直接搬运过来，Host 与坐标留空/用默认值——用户必须自己补 Host，
             * 因为它是每个账号随机分配、无法猜测的，这也是新版的安全机制。
             * 老 Key 搬运后清空，避免文件里留两份凭据。
             */
            if (!string.IsNullOrWhiteSpace(settings.Api.QWeatherKey))
            {
                if (string.IsNullOrWhiteSpace(settings.Api.QWeather.ApiKey))
                {
                    settings.Api.QWeather.ApiKey = settings.Api.QWeatherKey;
                }

                settings.Api.QWeatherKey = string.Empty;
                changed = true;
            }

            if (settings.Api.QWeather.IntervalMinutes < 5)
            {
                settings.Api.QWeather.IntervalMinutes = 5;
            }
            else if (settings.Api.QWeather.IntervalMinutes > 240)
            {
                settings.Api.QWeather.IntervalMinutes = 240;
            }

            if (double.IsNaN(settings.Appearance.BarHeight) || settings.Appearance.BarHeight < 24)
            {
                settings.Appearance.BarHeight = 30;
            }
            else if (settings.Appearance.BarHeight > 60)
            {
                settings.Appearance.BarHeight = 60;
            }

            if (settings.Metrics.IntervalMs < 250)
            {
                settings.Metrics.IntervalMs = 250;
            }

            /*
             * Blur 已从设置项移除：Win11 的 SetWindowCompositionAttribute 只提供
             * Acrylic 一种效果，Blur 与它在观感上完全相同，留着只会误导。
             * 旧配置里若写着 Blur(1)，统一归一到 Acrylic(2)。
             */
            if (settings.Appearance.Material == MaterialKind.Blur)
            {
                settings.Appearance.Material = MaterialKind.Acrylic;
                changed = true;
            }

            /*
             * 「跟随系统」主题已移除：RefreshFromSystem 一直没有调用点，
             * 该选项实际上只在启动时读一次注册表，名不副实。
             * 旧配置里 theme=0 表示跟随系统，这里归一到浅色（与原先的默认观感一致）。
             */
            if (settings.Appearance.Theme == ThemeMode.System)
            {
                settings.Appearance.Theme = ThemeMode.Light;
                changed = true;
            }

            return settings;
        }

        /// <summary>防抖写入：连续修改只在最后一次调用后 500ms 落盘。</summary>
        public void Save()
        {
            if (_disposed)
            {
                return;
            }

            lock (_gate)
            {
                _debounceTimer ??= new System.Threading.Timer(
                    _ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
                _debounceTimer.Change(500, Timeout.Infinite);
            }
        }

        /// <summary>立即落盘，退出时调用。</summary>
        public void Flush()
        {
            try
            {
                string json;
                lock (_gate)
                {
                    json = JsonSerializer.Serialize(Current, SerializerOptions);
                }

                string temp = _filePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _filePath, overwrite: true);
            }
            catch (Exception)
            {
                // 落盘失败不应影响运行中的顶栏。
            }
        }

        /// <summary>替换当前配置（设置窗口导入/重置时使用）。</summary>
        public void Replace(AppSettings settings)
        {
            // Replace 之后一律会 Save，迁移标记没有意义，丢弃即可。
            Current = Normalize(settings, out _);
            Changed?.Invoke(this, EventArgs.Empty);
            Save();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_gate)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }

            Flush();
        }
    }
}
