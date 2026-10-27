using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using MyTopBar.Models.Settings;

namespace MyTopBar.Services
{
    /// <summary>
    /// 天气数据源：和风天气新版实时天气接口。
    ///
    /// 已接入 GlobalTickService：不再自建 DispatcherTimer，改为订阅全局 Tick。
    /// 组件被关闭时调用 Stop() 即可，不会留下孤儿定时器。
    /// </summary>
    public sealed class WeatherDataService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly Dispatcher _dispatcher;
        private readonly GlobalTickService _tick;
        private IDisposable? _subscription;
        private bool _disposed;

        private QWeatherSettings _config = new();

        public string CurrentText { get; private set; } = "天气 --";
        public string CurrentIcon { get; private set; } = "🌡️";

        public event Action<string, string>? DataChanged;

        public WeatherDataService(HttpClient http, Dispatcher dispatcher, GlobalTickService tick)
        {
            _http = http;
            _dispatcher = dispatcher;
            _tick = tick;
        }

        public void Configure(QWeatherSettings settings)
        {
            _config = settings ?? new QWeatherSettings();
            Resubscribe();
        }

        public void Start()
        {
            if (!IsConfigured(out string reason))
            {
                Stop();
                CurrentText = reason;
                CurrentIcon = "⚠️";
                DataChanged?.Invoke(CurrentText, CurrentIcon);
                return;
            }

            if (_subscription is not null)
            {
                _ = RefreshAsync();
                return;
            }

            Resubscribe();
            _ = RefreshAsync();
        }

        public void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Resubscribe()
        {
            _subscription?.Dispose();
            if (!IsConfigured(out _))
            {
                return;
            }

            _subscription = _tick.Subscribe(
                TimeSpan.FromMinutes(Math.Clamp(_config.IntervalMinutes, 5, 240)),
                () => _ = RefreshAsync());
        }

        private bool IsConfigured(out string reason)
        {
            if (string.IsNullOrWhiteSpace(_config.ApiHost))
            {
                reason = "天气 未设 API Host";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                reason = "天气 未设 Key";
                return false;
            }

            if (!IsValidCoordinate(_config.Latitude) || !IsValidCoordinate(_config.Longitude))
            {
                reason = "天气 坐标无效";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool IsValidCoordinate(string? value) => double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out _);

        private async Task RefreshAsync()
        {
            if (!IsConfigured(out _))
            {
                return;
            }

            try
            {
                string host = _config.ApiHost.Trim().TrimEnd('/');
                string lat = _config.Latitude.Trim();
                string lon = _config.Longitude.Trim();

                string url = $"https://{host}/weather/v1/current/{lat}/{lon}";
                if (!string.IsNullOrWhiteSpace(_config.Language))
                {
                    url += "?lang=" + Uri.EscapeDataString(_config.Language.Trim());
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                // 新版接口用请求头认证；URL 参数 key= 已废弃。
                request.Headers.TryAddWithoutValidation("X-QW-Api-Key", _config.ApiKey.Trim());

                using HttpResponseMessage response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, CancellationToken.None)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // 401 多半是 Key 无效或 Host 与 Key 不属于同一账号，
                    // 不在顶栏上打扰用户，保留上一次读数。
                    return;
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                (string display, string icon)? result = ParseCurrent(json);
                if (result is not null)
                {
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        CurrentText = result.Value.display;
                        CurrentIcon = result.Value.icon;
                        DataChanged?.Invoke(CurrentText, CurrentIcon);
                    });
                }
            }
            catch
            {
                // 网络失败保留上一次读数。
            }
        }

        private static (string Display, string Icon)? ParseCurrent(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string text = string.Empty;
                string iconCode = string.Empty;
                if (root.TryGetProperty("condition", out JsonElement condition))
                {
                    if (condition.TryGetProperty("text", out JsonElement textEl))
                    {
                        text = textEl.GetString() ?? string.Empty;
                    }
                    if (condition.TryGetProperty("icon", out JsonElement iconEl))
                    {
                        iconCode = iconEl.GetString() ?? string.Empty;
                    }
                }

                if (!root.TryGetProperty("temperature", out JsonElement temperature)
                    || !temperature.TryGetProperty("value", out JsonElement valueEl)
                    || !valueEl.TryGetDouble(out double value))
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                // 温度取整，避免顶栏出现 31.71° 这种过长内容。
                string display = $"{text} {Math.Round(value)}°";
                return (display, GetWeatherIcon(iconCode, text));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 把和风天气的 icon 代码映射成 emoji。
        /// 文档：https://dev.qweather.com/docs/resource/icons/
        /// </summary>
        private static string GetWeatherIcon(string iconCode, string conditionText)
        {
            string icon = iconCode switch
            {
                "100" => "☀️",   // 晴
                "101" => "⛅",   // 多云
                "102" => "⛅",   // 少云
                "103" => "⛅",   // 晴间多云
                "104" => "☁️",   // 阴
                "150" => "🌙",   // 晴（夜）
                "151" => "☁️",   // 多云（夜）
                "152" => "☁️",   // 少云（夜）
                "153" => "☁️",   // 晴间多云（夜）
                "300" => "🌦️",   // 阵雨
                "301" => "🌧️",   // 强阵雨
                "302" => "⛈️",   // 雷阵雨
                "303" => "⛈️",   // 强雷阵雨
                "304" => "⛈️",   // 雷阵雨伴有冰雹
                "305" => "🌧️",   // 小雨
                "306" => "🌧️",   // 中雨
                "307" => "🌧️",   // 大雨
                "308" => "🌧️",   // 极端降雨
                "309" => "🌧️",   // 毛毛雨/细雨
                "310" => "🌧️",   // 暴雨
                "311" => "🌧️",   // 大暴雨
                "312" => "🌧️",   // 特大暴雨
                "313" => "🌧️",   // 冻雨
                "314" => "🌧️",   // 小到中雨
                "315" => "🌧️",   // 中到大雨
                "316" => "🌧️",   // 大到暴雨
                "317" => "🌧️",   // 暴雨到大暴雨
                "318" => "🌧️",   // 大暴雨到特大暴雨
                "399" => "🌧️",   // 雨
                "400" => "🌨️",   // 小雪
                "401" => "🌨️",   // 中雪
                "402" => "🌨️",   // 大雪
                "403" => "🌨️",   // 暴雪
                "404" => "🌨️",   // 雨夹雪
                "405" => "🌨️",   // 雨雪天气
                "406" => "🌨️",   // 阵雨夹雪
                "407" => "🌨️",   // 阵雪
                "499" => "🌨️",   // 雪
                "500" => "🌫️",   // 薄雾
                "501" => "🌫️",   // 雾
                "502" => "🌫️",   // 霾
                "503" => "🌪️",   // 扬沙
                "504" => "🌪️",   // 浮尘
                "507" => "🌪️",   // 沙尘暴
                "508" => "🌪️",   // 强沙尘暴
                "509" => "🌫️",   // 浓雾
                "510" => "🌫️",   // 强浓雾
                "511" => "🌫️",   // 中度霾
                "512" => "🌫️",   // 重度霾
                "513" => "🌫️",   // 严重霾
                "514" => "🌫️",   // 大雾
                "515" => "🌫️",   // 特强浓雾
                "900" => "🔥",   // 热
                "901" => "❄️",   // 冷
                "999" => "🌡️",   // 未知
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(icon))
            {
                return icon;
            }

            // 按文字描述兜底匹配
            if (conditionText.Contains("晴")) return "☀️";
            if (conditionText.Contains("云") || conditionText.Contains("阴")) return "☁️";
            if (conditionText.Contains("雨")) return "🌧️";
            if (conditionText.Contains("雪")) return "🌨️";
            if (conditionText.Contains("雷")) return "⛈️";
            if (conditionText.Contains("雾") || conditionText.Contains("霾")) return "🌫️";
            if (conditionText.Contains("沙") || conditionText.Contains("尘")) return "🌪️";

            return "🌡️";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }
    }
}
