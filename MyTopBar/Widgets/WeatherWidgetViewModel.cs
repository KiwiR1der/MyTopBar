using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Models.Settings;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 右区天气组件：和风天气新版实时天气接口。
    ///
    ///   GET https://{ApiHost}/weather/v1/current/{lat}/{lon}?lang=zh
    ///   X-QW-Api-Key: {key}
    ///
    /// 注意这是**新版**接口：旧的 devapi.qweather.com/v7/weather/now?key=
    /// 已被官方列入停用计划，且位置参数由「城市 ID」改为经纬度。
    /// 未配置时显示占位，不重试、不报错。
    /// </summary>
    public sealed partial class WeatherWidgetViewModel : WidgetViewModelBase
    {
        private readonly HttpClient _http;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer = new();

        private QWeatherSettings _config = new();

        [ObservableProperty]
        private string _text = "天气 --";

        public override string Id => "weather";

        public override string DisplayName => "天气";

        public override WidgetZone DefaultZone => WidgetZone.Right;

        public override double MinWidth => 72;

        public WeatherWidgetViewModel(HttpClient http, Dispatcher dispatcher)
        {
            _http = http;
            _dispatcher = dispatcher;
            // 只挂一次；Start() 会在每次应用设置时重复调用。
            _timer.Tick += OnTimerTick;
        }

        private void OnTimerTick(object? sender, EventArgs e) => _ = RefreshAsync();

        public void Configure(QWeatherSettings settings)
        {
            _config = settings ?? new QWeatherSettings();
            _timer.Interval = TimeSpan.FromMinutes(Math.Clamp(_config.IntervalMinutes, 5, 240));
        }

        public void Start()
        {
            if (!IsConfigured(out string reason))
            {
                _timer.Stop();
                Text = reason;
                return;
            }

            if (_timer.IsEnabled)
            {
                _ = RefreshAsync();
                return;
            }

            _timer.Start();
            _ = RefreshAsync();
        }

        /// <summary>检查配置是否完整，不完整时给出顶栏上能显示的原因。</summary>
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
                string? display = ParseCurrent(json);
                if (display is not null)
                {
                    _ = _dispatcher.BeginInvoke(() => Text = display);
                }
            }
            catch
            {
                // 网络失败保留上一次读数。
            }
        }

        /// <summary>
        /// 解析新版响应：
        ///   { "condition": { "text": "少云" }, "temperature": { "value": 31.71 } }
        /// 与旧版 { "now": { "text", "temp" } } 的结构完全不同。
        /// </summary>
        private static string? ParseCurrent(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string text = string.Empty;
                if (root.TryGetProperty("condition", out JsonElement condition)
                    && condition.TryGetProperty("text", out JsonElement textEl))
                {
                    text = textEl.GetString() ?? string.Empty;
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
                return $"{text} {Math.Round(value)}°";
            }
            catch
            {
                return null;
            }
        }

        protected override void DisposeCore()
        {
            _timer.Stop();
        }
    }
}
