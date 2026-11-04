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

        /// <summary>手动刷新结束时触发，参数为是否成功（供弹窗反馈）。</summary>
        public event Action<bool>? RefreshCompleted;

        /// <summary>是否正在请求，避免用户连点刷新按钮造成并发请求。</summary>
        public bool IsRefreshing { get; private set; }

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

        /// <summary>
        /// 手动刷新（弹窗里的刷新按钮调用）。
        /// 与定时刷新共用同一套逻辑，但会反馈完成结果，并防止并发请求。
        /// </summary>
        public void RefreshNow()
        {
            if (IsRefreshing)
            {
                return;
            }

            IsRefreshing = true;
            _ = RefreshAsync(manual: true);
        }

        private async Task RefreshAsync() => await RefreshAsync(manual: false);

        private async Task RefreshAsync(bool manual)
        {
            bool success = false;
            try
            {
                if (!IsConfigured(out _))
                {
                    return;
                }

                success = await FetchCoreAsync().ConfigureAwait(false);
            }
            catch
            {
                // 网络失败保留上一次读数。
            }
            finally
            {
                if (manual)
                {
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        IsRefreshing = false;
                        RefreshCompleted?.Invoke(success);
                    });
                }
            }
        }

        private async Task<bool> FetchCoreAsync()
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
                return false;
            }

            string currentJson = await currentResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            CurrentWeatherData? current = ParseCurrent(currentJson);
            if (current is null)
            {
                return false;
            }

            // 3 天预报
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

            return true;
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
                string icon = GetWeatherIconKey(iconCode, text);

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
                        dayIcon = GetWeatherIconKey(ReadString(cond, "code"), dayText);
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
        /*
         * 把和风天气的 icon 代码映射成彩色矢量图标的资源 key。
         * 返回值对应 WeatherIcons.xaml 里的 WeatherIcon_{key}（阿里 iconfont 彩色 SVG 转换而来）。
         * 文档：https://dev.qweather.com/docs/resource/icons/
         *
         * 之前用 emoji（Segoe UI Emoji），但 WPF 字体回退在深色背景下渲染成单色轮廓，
         * 且彩色渲染依赖系统/DirectWrite 行为不稳定，故改用自绘矢量图标。
         */
        private static string GetWeatherIconKey(string iconCode, string conditionText)
        {
            string key = iconCode switch
            {
                "100" => "qingtian",      // 晴
                "101" or "102" or "103" => "duoyun",   // 多云/少云/晴间多云
                "104" => "yintian",       // 阴
                "150" => "yewan",         // 晴（夜）
                "151" or "152" or "153" => "duoyun",   // 多云（夜）
                "300" or "301" => "zhenyu",            // 阵雨/强阵雨
                "302" or "303" or "304" => "fengbao",  // 雷阵雨/冰雹
                "305" or "309" or "314" => "xiaoyu",   // 小雨/毛毛雨/小到中雨
                "306" or "315" => "zhongyu",           // 中雨/中到大雨
                "307" or "308" or "310" or "311" or "312"
                    or "316" or "317" or "318" => "dayu",  // 大雨及以上
                "313" => "bingbaoyu",     // 冻雨
                "399" => "xiayu",         // 雨
                "400" or "407" => "xiaoxue",           // 小雪/阵雪
                "401" or "402" or "403" or "499" => "daxue",  // 中雪/大雪/暴雪/雪
                "404" or "405" or "406" => "yuxue",    // 雨夹雪/雨雪/阵雨夹雪
                "500" or "501" or "509" or "510" or "514" or "515" => "dawu",  // 雾
                "502" or "511" or "512" or "513" => "mai",     // 霾
                "503" or "504" => "chentu",            // 扬沙/浮尘
                "507" or "508" => "shachen",           // 沙尘暴
                "900" => "qingtian",      // 热
                "901" => "daxue",         // 冷
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(key))
            {
                return key;
            }

            // 按文字描述兜底匹配
            if (conditionText.Contains("雷")) return "fengbao";
            if (conditionText.Contains("雪")) return "daxue";
            if (conditionText.Contains("雨")) return "xiayu";
            if (conditionText.Contains("雾") || conditionText.Contains("霾")) return "dawu";
            if (conditionText.Contains("沙") || conditionText.Contains("尘")) return "shachen";
            if (conditionText.Contains("晴")) return "qingtian";
            if (conditionText.Contains("云") || conditionText.Contains("阴")) return "duoyun";

            return "qingtian";
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
