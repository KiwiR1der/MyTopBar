using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 中区天气组件：和风天气 now 接口。
    /// 无 Key 时显示「未配置」，不重试、不报错。
    /// </summary>
    public sealed partial class WeatherWidgetViewModel : WidgetViewModelBase
    {
        private readonly HttpClient _http;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer = new();
        private string _key = string.Empty;
        private string _location = "101010100";

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
            _timer.Tick += (_, _) => _ = RefreshAsync();
        }

        public void Configure(string key, string location, int intervalMinutes)
        {
            _key = key?.Trim() ?? string.Empty;
            _location = string.IsNullOrWhiteSpace(location) ? "101010100" : location.Trim();
            _timer.Interval = TimeSpan.FromMinutes(Math.Clamp(intervalMinutes, 5, 240));
        }

        public void Start()
        {
            if (string.IsNullOrWhiteSpace(_key))
            {
                Text = "天气 未配置";
                return;
            }

            _timer.Start();
            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                string url = $"https://devapi.qweather.com/v7/weather/now?location={Uri.EscapeDataString(_location)}&key={Uri.EscapeDataString(_key)}";
                string json = await _http.GetStringAsync(url).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                string code = root.TryGetProperty("code", out JsonElement c) ? c.GetString() ?? string.Empty : string.Empty;
                if (code != "200" || !root.TryGetProperty("now", out JsonElement now))
                {
                    return;
                }

                string text = now.TryGetProperty("text", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;
                string temp = now.TryGetProperty("temp", out JsonElement tp) ? tp.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(temp))
                {
                    return;
                }

                string display = $"{text} {temp}°";
                _ = _dispatcher.BeginInvoke(() => Text = display);
            }
            catch
            {
                // 网络失败保留上一次读数。
            }
        }

        protected override void DisposeCore()
        {
            _timer.Stop();
        }
    }
}
