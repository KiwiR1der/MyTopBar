using System.Globalization;
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

        // ---- 弹窗详情数据 ----
        public string ConditionText { get; private set; } = string.Empty;
        public string Temperature { get; private set; } = "--";
        public string FeelsLike { get; private set; } = "--";
        public string Humidity { get; private set; } = "--";
        public string WindInfo { get; private set; } = "--";
        public string HighTemp { get; private set; } = "--";
        public string LowTemp { get; private set; } = "--";
        public string Sunrise { get; private set; } = "--";
        public string Sunset { get; private set; } = "--";
        public string PlaceName { get; private set; } = string.Empty;

        /// <summary>未来预报（含今天），最多 3 天。</summary>
        public List<DailyForecast> Forecasts { get; private set; } = new();

        public event Action<string, string>? DataChanged;

        /// <summary>详情数据变化时触发（弹窗刷新用）。</summary>
        public event Action? DetailChanged;

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

                // localTime=true：返回本地时间（日出日落直接用），否则是 UTC。
                var query = new List<string> { "localTime=true" };
                if (!string.IsNullOrWhiteSpace(_config.Language))
                {
                    query.Add("lang=" + Uri.EscapeDataString(_config.Language.Trim()));
                }
                string queryString = "?" + string.Join("&", query);

                // 实时天气
                string currentUrl = $"https://{host}/weather/v1/current/{lat}/{lon}{queryString}";
                using var currentRequest = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                currentRequest.Headers.TryAddWithoutValidation("X-QW-Api-Key", _config.ApiKey.Trim());

                using HttpResponseMessage currentResponse = await _http
                    .SendAsync(currentRequest, HttpCompletionOption.ResponseContentRead, CancellationToken.None)
                    .ConfigureAwait(false);

                if (!currentResponse.IsSuccessStatusCode)
                {
                    return;
                }

                string currentJson = await currentResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                CurrentWeatherData? current = ParseCurrent(currentJson);
                if (current is null)
                {
                    return;
                }

                // 3天预报
                List<DailyForecast>? forecasts = null;
                try
                {
                    // 天数是查询参数 days=3（默认 7 天），不是路径段。
                    string forecastUrl = $"https://{host}/weather/v1/daily/{lat}/{lon}{queryString}&days=3";
                    using var forecastRequest = new HttpRequestMessage(HttpMethod.Get, forecastUrl);
                    forecastRequest.Headers.TryAddWithoutValidation("X-QW-Api-Key", _config.ApiKey.Trim());

                    using HttpResponseMessage forecastResponse = await _http
                        .SendAsync(forecastRequest, HttpCompletionOption.ResponseContentRead, CancellationToken.None)
                        .ConfigureAwait(false);

                    if (forecastResponse.IsSuccessStatusCode)
                    {
                        string forecastJson = await forecastResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                        forecasts = ParseDailyForecast(forecastJson);
                    }
                }
                catch
                {
                    // 预报失败不影响实时数据展示
                }

                _ = _dispatcher.BeginInvoke(() =>
                {
                    CurrentText = current.Display;
                    CurrentIcon = current.Icon;
                    ConditionText = current.Condition;
                    Temperature = current.Temperature;
                    FeelsLike = current.FeelsLike;
                    Humidity = current.Humidity;
                    WindInfo = current.WindInfo;
                    PlaceName = _config.PlaceName;

                    if (forecasts is { Count: > 0 })
                    {
                        Forecasts = forecasts;
                        HighTemp = forecasts[0].High;
                        LowTemp = forecasts[0].Low;
                        Sunrise = forecasts[0].Sunrise;
                        Sunset = forecasts[0].Sunset;
                    }

                    DataChanged?.Invoke(CurrentText, CurrentIcon);
                    DetailChanged?.Invoke();
                });
            }
            catch
            {
                // 网络失败保留上一次读数。
            }
        }

        /*
         * 和风新版 API 的字段有三种形态，必须全部兼容：
         *   纯数字:  "humidity": 45
         *   字符串:  "tempMax": "34"
         *   嵌套对象: "temperature": { "value": 31.7, "unit": "C" }
         * 对 Object / String 直接调 TryGetDouble 会抛 InvalidOperationException，
         * 之前就是因为 humidity 按纯数字解析导致整个 ParseCurrent 返回 null。
         */
        private static bool TryReadNumber(JsonElement parent, string name, out double value)
        {
            value = 0;
            if (!parent.TryGetProperty(name, out JsonElement el))
            {
                return false;
            }

            switch (el.ValueKind)
            {
                case JsonValueKind.Number:
                    return el.TryGetDouble(out value);
                case JsonValueKind.String:
                    return double.TryParse(
                        el.GetString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out value);
                case JsonValueKind.Object:
                    return el.TryGetProperty("value", out JsonElement inner) && TryReadNumberValue(inner, out value);
                default:
                    return false;
            }
        }

        private static bool TryReadNumberValue(JsonElement el, out double value)
        {
            value = 0;
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.TryGetDouble(out value),
                JsonValueKind.String => double.TryParse(
                    el.GetString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value),
                _ => false
            };
        }

        private static string ReadString(JsonElement parent, string name)
        {
            return parent.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() ?? string.Empty
                : string.Empty;
        }

        private static CurrentWeatherData? ParseCurrent(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string text = string.Empty;
                string iconCode = string.Empty;
                if (root.TryGetProperty("condition", out JsonElement condition))
                {
                    text = ReadString(condition, "text");
                    iconCode = ReadString(condition, "icon");
                }

                if (!TryReadNumber(root, "temperature", out double tempValue))
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                // 体感温度：字段名是 feelsLike（嵌套 {value,unit}）。
                string feelsLike = TryReadNumber(root, "feelsLike", out double feelsValue)
                    ? $"{Math.Round(feelsValue)}°"
                    : "--";

                // 湿度是比例值（0.69 = 69%），乘以 100。
                string humidity = TryReadNumber(root, "humidity", out double humidityValue)
                    ? $"{Math.Round(humidityValue * 100)}%"
                    : "--";

                string windInfo = "--";
                if (root.TryGetProperty("wind", out JsonElement wind) && wind.ValueKind == JsonValueKind.Object)
                {
                    // 风向：direction.compass（"sw"），转成中文方位。
                    string windDir = string.Empty;
                    if (wind.TryGetProperty("direction", out JsonElement dirEl) && dirEl.ValueKind == JsonValueKind.Object)
                    {
                        windDir = CompassToChinese(ReadString(dirEl, "compass"));
                    }

                    // 风速：speed.value 单位 m/s，转成 km/h（×3.6）。
                    string windSpeed = TryReadNumber(wind, "speed", out double spd)
                        ? $"{Math.Round(spd * 3.6)} km/h"
                        : string.Empty;

                    if (!string.IsNullOrWhiteSpace(windDir) && !string.IsNullOrWhiteSpace(windSpeed))
                    {
                        windInfo = $"{windDir} {windSpeed}";
                    }
                    else if (!string.IsNullOrWhiteSpace(windDir))
                    {
                        windInfo = windDir;
                    }
                    else if (!string.IsNullOrWhiteSpace(windSpeed))
                    {
                        windInfo = windSpeed;
                    }
                }

                string tempStr = $"{Math.Round(tempValue)}";
                string display = $"{text} {tempStr}°";
                string icon = GetWeatherIcon(iconCode, text);

                return new CurrentWeatherData
                {
                    Display = display,
                    Icon = icon,
                    Condition = text,
                    Temperature = tempStr,
                    FeelsLike = feelsLike,
                    Humidity = humidity,
                    WindInfo = windInfo
                };
            }
            catch
            {
                return null;
            }
        }

        private static List<DailyForecast>? ParseDailyForecast(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                // 根字段是 days[]，不是 daily[]。
                if (!root.TryGetProperty("days", out JsonElement days)
                    || days.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var forecasts = new List<DailyForecast>();
                foreach (JsonElement day in days.EnumerateArray())
                {
                    // 日期标识用 forecastStartTime（ISO），截取日期部分。
                    string dateStr = FormatIsoDate(ReadString(day, "forecastStartTime"));

                    // 白天天气：daytime.condition.text / code。
                    string dayText = string.Empty;
                    string dayIcon = string.Empty;
                    if (day.TryGetProperty("daytime", out JsonElement daytime) && daytime.ValueKind == JsonValueKind.Object
                        && daytime.TryGetProperty("condition", out JsonElement cond) && cond.ValueKind == JsonValueKind.Object)
                    {
                        dayText = ReadString(cond, "text");
                        dayIcon = GetWeatherIcon(ReadString(cond, "code"), dayText);
                    }

                    string high = TryReadNumber(day, "temperatureMax", out double maxVal)
                        ? $"{Math.Round(maxVal)}°"
                        : "--";
                    string low = TryReadNumber(day, "temperatureMin", out double minVal)
                        ? $"{Math.Round(minVal)}°"
                        : "--";

                    // 日出日落：astro.sunrise/sunset，ISO 格式（localTime=true 时是本地时间）。
                    string sunrise = "--";
                    string sunset = "--";
                    if (day.TryGetProperty("astro", out JsonElement astro) && astro.ValueKind == JsonValueKind.Object)
                    {
                        sunrise = FormatIsoTime(ReadString(astro, "sunrise"));
                        sunset = FormatIsoTime(ReadString(astro, "sunset"));
                    }

                    forecasts.Add(new DailyForecast
                    {
                        Date = dateStr,
                        DayName = GetDayName(dateStr),
                        Icon = dayIcon,
                        Condition = dayText,
                        High = high,
                        Low = low,
                        Sunrise = sunrise,
                        Sunset = sunset
                    });
                }

                return forecasts.Count > 0 ? forecasts : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>从 ISO 时间字符串（2024-08-11T04:22Z）截取日期部分（yyyy-MM-dd），失败返回原串。</summary>
        private static string FormatIsoDate(string iso)
        {
            if (string.IsNullOrWhiteSpace(iso))
            {
                return string.Empty;
            }

            return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset dto)
                ? dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : iso;
        }

        /// <summary>从 ISO 时间字符串截取 HH:mm。localTime=true 时是本地时间，直接取即可；UTC 则转本地。</summary>
        private static string FormatIsoTime(string iso)
        {
            if (string.IsNullOrWhiteSpace(iso))
            {
                return "--";
            }

            if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTimeOffset dto))
            {
                return "--";
            }

            // 带 Z 后缀的是 UTC，转本地；否则按本地时间直接显示。
            return iso.EndsWith('Z')
                ? dto.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
                : dto.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>把罗盘方位码（n/ne/e/.../nw）转成中文风向。</summary>
        private static string CompassToChinese(string compass) => compass.ToLowerInvariant() switch
        {
            "n" => "北风",
            "nne" or "ne" or "ene" => "东北风",
            "e" => "东风",
            "ese" or "se" or "sse" => "东南风",
            "s" => "南风",
            "ssw" or "sw" or "wsw" => "西南风",
            "w" => "西风",
            "wnw" or "nw" or "nnw" => "西北风",
            _ => string.Empty
        };

        private static string GetDayName(string dateStr)
        {
            if (!DateTime.TryParse(dateStr, out DateTime date))
            {
                return dateStr;
            }

            if (date.Date == DateTime.Today)
            {
                return "今天";
            }

            if (date.Date == DateTime.Today.AddDays(1))
            {
                return "明天";
            }

            return date.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
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

        // ---- 数据模型 ----

        /// <summary>实时天气解析结果。</summary>
        private sealed class CurrentWeatherData
        {
            public string Display { get; init; } = string.Empty;
            public string Icon { get; init; } = string.Empty;
            public string Condition { get; init; } = string.Empty;
            public string Temperature { get; init; } = "--";
            public string FeelsLike { get; init; } = "--";
            public string Humidity { get; init; } = "--";
            public string WindInfo { get; init; } = "--";
        }

        /// <summary>单日天气预报。</summary>
        public sealed class DailyForecast
        {
            public string Date { get; init; } = string.Empty;
            public string DayName { get; init; } = string.Empty;
            public string Icon { get; init; } = string.Empty;
            public string Condition { get; init; } = string.Empty;
            public string High { get; init; } = "--";
            public string Low { get; init; } = "--";
            public string Sunrise { get; init; } = "--";
            public string Sunset { get; init; } = "--";
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
