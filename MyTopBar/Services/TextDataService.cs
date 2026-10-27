using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using MyTopBar.Models.Settings;

namespace MyTopBar.Services
{
    /// <summary>
    /// 文本/一言数据源：静态文本或一言 API。
    ///
    /// 已接入 GlobalTickService：不再自建 DispatcherTimer，改为订阅全局 Tick。
    /// 一言失败时保留上一次内容，不显示错误。
    /// </summary>
    public sealed class TextDataService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly Dispatcher _dispatcher;
        private readonly GlobalTickService _tick;
        private IDisposable? _subscription;

        private string _cached = string.Empty;
        private bool _disposed;

        public string CurrentText { get; private set; } = "MyTopBar";
        public string CurrentSource { get; private set; } = string.Empty;

        public event Action<string, string>? DataChanged;

        public TextDataService(HttpClient http, Dispatcher dispatcher, GlobalTickService tick)
        {
            _http = http;
            _dispatcher = dispatcher;
            _tick = tick;
        }

        public TextWidgetMode Mode { get; private set; } = TextWidgetMode.Static;
        public string StaticText { get; private set; } = string.Empty;
        public int IntervalMinutes { get; private set; } = 30;

        public void Configure(TextWidgetMode mode, string staticText, int intervalMinutes)
        {
            Mode = mode;
            StaticText = staticText ?? string.Empty;
            IntervalMinutes = Math.Clamp(intervalMinutes, 1, 1440);
            Resubscribe();
        }

        public void Start()
        {
            if (Mode == TextWidgetMode.Static)
            {
                Stop();
                CurrentText = string.IsNullOrWhiteSpace(StaticText) ? "MyTopBar" : StaticText;
                CurrentSource = string.Empty;
                DataChanged?.Invoke(CurrentText, CurrentSource);
                return;
            }

            if (_subscription is not null)
            {
                // 已在运行：只刷新一次内容，不重启订阅，避免改设置时打断节奏。
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
            if (Mode == TextWidgetMode.Static)
            {
                return;
            }

            _subscription = _tick.Subscribe(
                TimeSpan.FromMinutes(IntervalMinutes),
                () => _ = RefreshAsync());
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
                    CurrentText = hitokoto;
                    CurrentSource = from;
                    DataChanged?.Invoke(CurrentText, CurrentSource);
                });
            }
            catch
            {
                // 断网或接口异常时保留上一次内容，不打断顶栏
                if (!string.IsNullOrEmpty(_cached))
                {
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        CurrentText = _cached;
                        DataChanged?.Invoke(CurrentText, CurrentSource);
                    });
                }
            }
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
