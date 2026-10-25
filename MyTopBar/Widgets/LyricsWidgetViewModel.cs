using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Services.Lyrics;
using MyTopBar.Services.Media;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 中区歌词组件：走 SMTC 取当前曲目，再向 LRCLIB 查带时间轴歌词。
    /// </summary>
    public sealed partial class LyricsWidgetViewModel : WidgetViewModelBase
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private readonly MediaSessionService _media;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _ticker = new();

        private IReadOnlyList<LyricLine> _lines = Array.Empty<LyricLine>();
        private string _loadedKey = string.Empty;
        private int _lastIndex = -1;
        private TimeSpan _basePosition;
        private DateTime _baseStamp = DateTime.UtcNow;

        [ObservableProperty]
        private string _text = string.Empty;

        [ObservableProperty]
        private string _nextText = string.Empty;

        public LyricsWidgetViewModel(MediaSessionService media, Dispatcher dispatcher)
        {
            _media = media;
            _dispatcher = dispatcher;
            _media.TrackChanged += OnTrackChanged;
            _ticker.Interval = TimeSpan.FromMilliseconds(250);
            _ticker.Tick += OnTick;
        }

        public override string Id => "lyrics";

        public override string DisplayName => "歌词";

        public override WidgetZone DefaultZone => WidgetZone.Center;

        public override double MinWidth => 120;

        /// <summary>是否显示下一句。</summary>
        public bool ShowNextLine { get; set; }

        /// <summary>无播放时是否隐藏整块。</summary>
        public bool HideWhenIdle { get; set; } = true;

        public void Start()
        {
            _ticker.Start();
            _ = _media.StartAsync();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_lines.Count == 0)
            {
                return;
            }

            int index = FindIndex(EstimatePosition());
            if (index == _lastIndex)
            {
                return;
            }

            _lastIndex = index;
            Text = index >= 0 && index < _lines.Count ? _lines[index].Text : string.Empty;
            NextText = ShowNextLine && index + 1 < _lines.Count ? _lines[index + 1].Text : string.Empty;
        }

        private TimeSpan EstimatePosition()
        {
            MediaTrackInfo? current = _media.Current;
            if (current is null)
            {
                return TimeSpan.Zero;
            }

            if (!current.Value.IsPlaying)
            {
                return current.Value.Position;
            }

            // SMTC 的 Position 不会持续推送，用本地时间外推并限制在时长内。
            TimeSpan elapsed = DateTime.UtcNow - _baseStamp;
            TimeSpan estimated = _basePosition + elapsed;
            TimeSpan duration = current.Value.Duration;
            if (duration > TimeSpan.Zero && estimated > duration)
            {
                return duration;
            }

            return estimated;
        }

        private int FindIndex(TimeSpan position)
        {
            int index = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Time <= position)
                {
                    index = i;
                }
                else
                {
                    break;
                }
            }

            return index;
        }

        private void OnTrackChanged(MediaTrackInfo? track)
        {
            if (track is null)
            {
                _lines = Array.Empty<LyricLine>();
                _loadedKey = string.Empty;
                _lastIndex = -1;
                Text = HideWhenIdle ? string.Empty : "无播放";
                NextText = string.Empty;
                return;
            }

            MediaTrackInfo info = track.Value;
            _basePosition = info.Position;
            _baseStamp = DateTime.UtcNow;

            string key = $"{info.Title}\u0001{info.Artist}";
            if (key == _loadedKey)
            {
                return;
            }

            _loadedKey = key;
            _ = LoadLyricsAsync(info);
        }

        private async Task LoadLyricsAsync(MediaTrackInfo info)
        {
            try
            {
                string? lrc = await FetchLrcAsync(info.Title, info.Artist).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(lrc))
                {
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        _lines = Array.Empty<LyricLine>();
                        Text = info.Title;
                        NextText = string.Empty;
                    });
                    return;
                }

                IReadOnlyList<LyricLine> parsed = LrcParser.Parse(lrc);
                _ = _dispatcher.BeginInvoke(() =>
                {
                    _lines = parsed;
                    _lastIndex = -1;
                    if (parsed.Count == 0)
                    {
                        Text = info.Title;
                    }
                });
            }
            catch
            {
                // 断网或接口异常时保留曲名，不打断顶栏。
                _ = _dispatcher.BeginInvoke(() => Text = info.Title);
            }
        }

        private static async Task<string?> FetchLrcAsync(string title, string artist)
        {
            string url = "https://lrclib.net/api/get"
                + "?track_name=" + Uri.EscapeDataString(title)
                + "&artist_name=" + Uri.EscapeDataString(artist);
            try
            {
                string json = await Http.GetStringAsync(url).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("syncedLyrics", out JsonElement synced)
                    && synced.ValueKind == JsonValueKind.String)
                {
                    return synced.GetString();
                }
            }
            catch
            {
                // 精确匹配失败后走搜索接口。
            }

            string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(title + " " + artist)}";
            try
            {
                string json = await Http.GetStringAsync(searchUrl).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("syncedLyrics", out JsonElement synced)
                            && synced.ValueKind == JsonValueKind.String)
                        {
                            return synced.GetString();
                        }
                    }
                }
            }
            catch
            {
                // 忽略，交由调用方显示曲名。
            }

            return null;
        }

        protected override void DisposeCore()
        {
            _ticker.Stop();
            _ticker.Tick -= OnTick;
            _media.TrackChanged -= OnTrackChanged;
        }
    }
}