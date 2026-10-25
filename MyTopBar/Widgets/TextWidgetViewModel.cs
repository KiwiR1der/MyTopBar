using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Models.Settings;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 涓尯鏂囨湰缁勪欢锛氶潤鎬佹枃鏈垨涓€瑷€ API銆?    /// 涓€瑷€澶辫触鏃朵繚鐣欎笂涓€娆″唴瀹癸紝涓嶆樉绀洪敊璇€?    /// </summary>
    public sealed partial class TextWidgetViewModel : WidgetViewModelBase
    {
        private readonly HttpClient _http;
        private readonly Dispatcher _dispatcher;
        private string _cached = string.Empty;

        [ObservableProperty]
        private string _text = "MyTopBar";

        [ObservableProperty]
        private string _source = string.Empty;

        public override string Id => "text";
        public override string DisplayName => "鏂囨湰 / 涓€瑷€";
        public override WidgetZone DefaultZone => WidgetZone.Center;

        public override double MinWidth => 80;

        public TextWidgetViewModel(HttpClient http, Dispatcher dispatcher)
        {
            _http = http;
            _dispatcher = dispatcher;
        }

        public void Configure(TextWidgetMode mode, string staticText, int intervalMinutes)
        {
            Mode = mode;
            StaticText = staticText ?? string.Empty;
            IntervalMinutes = Math.Clamp(intervalMinutes, 1, 1440);
        }

        public TextWidgetMode Mode { get; private set; } = TextWidgetMode.Static;
        public string StaticText { get; private set; } = string.Empty;
        public int IntervalMinutes { get; private set; } = 30;
        private readonly DispatcherTimer _timer = new();

        /// <summary>启动刷新循环。静态模式只设置一次文本。</summary>
        public void Start()
        {
            if (Mode == TextWidgetMode.Static)
            {
                Text = string.IsNullOrWhiteSpace(StaticText) ? "MyTopBar" : StaticText;
                return;
            }

            _timer.Interval = TimeSpan.FromMinutes(IntervalMinutes);
            _timer.Tick += (_, _) => _ = RefreshAsync();
            _timer.Start();
            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                string json = await _http.GetStringAsync("https://v1.hitokoto.cn/").ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                string hitokoto = root.TryGetProperty("hitokoto", out JsonElement h) ? h.GetString() ?? string.Empty : string.Empty;
                string from = root.TryGetProperty("from", out JsonElement f) ? f.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(hitokoto))
                {
                    return;
                }

                _cached = hitokoto;
                _ = _dispatcher.BeginInvoke(() =>
                {
                    Text = hitokoto;
                    Source = from;
                });
            }
            catch
            {
                // 断网或接口异常时保留上一次内容，不打断顶栏
                if (!string.IsNullOrEmpty(_cached))
                {
                    _ = _dispatcher.BeginInvoke(() => Text = _cached);
                }
            }
        }

        protected override void DisposeCore()
        {
            _timer.Stop();
        }
    }
}
