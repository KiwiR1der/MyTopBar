using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Models.Settings;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 中区文本组件：静态文本或一言 API。
    /// 一言失败时保留上一次内容，不显示错误。
    /// </summary>
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
        public override string DisplayName => "文本 / 一言";
        public override WidgetZone DefaultZone => WidgetZone.Center;

        public override double MinWidth => 80;

        public TextWidgetViewModel(HttpClient http, Dispatcher dispatcher)
        {
            _http = http;
            _dispatcher = dispatcher;
            // 只在这里挂一次。Start() 会在每次应用设置时被重复调用，
            // 若在 Start() 里 += 就会叠加出多个 Tick 处理器，请求数成倍增长。
            _timer.Tick += OnTimerTick;
        }

        private void OnTimerTick(object? sender, EventArgs e) => _ = RefreshAsync();

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

        /// <summary>启动刷新循环。静态模式只设置一次文本。可重复调用。</summary>
        public void Start()
        {
            if (Mode == TextWidgetMode.Static)
            {
                _timer.Stop();
                Text = string.IsNullOrWhiteSpace(StaticText) ? "MyTopBar" : StaticText;
                return;
            }

            _timer.Interval = TimeSpan.FromMinutes(IntervalMinutes);
            if (_timer.IsEnabled)
            {
                // 已在运行：只刷新一次内容，不重启计时器，避免改设置时打断节奏。
                _ = RefreshAsync();
                return;
            }

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
