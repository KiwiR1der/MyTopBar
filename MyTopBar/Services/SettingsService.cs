using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
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
            Current = Load();

            // 首次启动（或配置损坏回退）时立即落盘一份默认配置，方便用户直接编辑。
            if (!File.Exists(_filePath))
            {
                Flush();
            }
        }

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
                return Normalize(loaded ?? CreateDefault());
            }
            catch (Exception)
            {
                // 配置文件损坏时退回默认值，避免顶栏因配置问题无法启动。
                return CreateDefault();
            }
        }

        private static AppSettings CreateDefault() => new();

        /// <summary>
        /// 补齐缺失字段、裁剪非法值，保证运行期不用到处判空。
        /// </summary>
        private static AppSettings Normalize(AppSettings settings)
        {
            settings.Appearance ??= new AppearanceSettings();
            settings.Api ??= new ApiSettings();
            settings.Api.Hitokoto ??= new HitokotoSettings();
            settings.Metrics ??= new MetricsSettings();
            settings.Widgets ??= new List<WidgetSettings>();

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
            Current = Normalize(settings);
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
