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
            _ticker.Interval = TimeSpan.FromMilliseconds(250);
            _ticker.Tick += OnTick;
            _media.TrackChanged += OnTrackChanged;
        }

        public void Start()
        {
            _ticker.Start();
            _ = _media.StartAsync();
        }

        public override string Id => "lyrics";

        public override string DisplayName => "歌词";

        public override WidgetZone DefaultZone => WidgetZone.Center;

        public override double MinWidth => 120;

        /// <summary>是否显示下一句。</summary>
        public bool ShowNextLine { get; set; }

        /// <summary>无播放时是否隐藏整块。</summary>
        public bool HideWhenIdle { get; set; } = true;

        /// <summary>
        /// 当前是否有活跃的媒体会话。中区据此在「歌词」与「一言」间互斥切换：
        /// 有播放显示歌词，无播放显示一言，二者不同时出现。
        /// </summary>
        [ObservableProperty]
        private bool _hasActiveMedia;

        /// <summary>
        /// 按当前播放位置定位歌词行并刷新文本。
        /// force=true 时即使行号没变也强制刷新，用于切歌后立即出词。
        private void OnTick(object? sender, EventArgs e) => RefreshFromClock(force: false);

        private void RefreshFromClock(bool force)
        {
            if (_lines.Count == 0)
            {
                return;
            }

            // 播放位置早于第一行歌词时 FindIndex 返回 -1，退回第 0 行，避免整块空白。
            int index = FindIndex(EstimatePosition());
            if (index < 0)
            {
                index = 0;
            }

            if (!force && index == _lastIndex)
            {
                return;
            }

            _lastIndex = index;
            Text = index < _lines.Count ? _lines[index].Text : string.Empty;
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
                HasActiveMedia = false;
                Text = HideWhenIdle ? string.Empty : "无播放";
                NextText = string.Empty;
                return;
            }

            HasActiveMedia = true;

            MediaTrackInfo info = track.Value;
            _basePosition = info.Position;
            _baseStamp = DateTime.UtcNow;

            string key = info.Title + "\u0001" + info.Artist;
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
                LrcResult? result = await FetchLrcAsync(info.Title, info.Artist).ConfigureAwait(false);
                // 查不到（含搜索无结果）统一按 null 处理，避免 default(LrcResult) 这种"非 null 但为空"的坑。
                if (result is null || result.Value.IsEmpty)
                {
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        _lines = Array.Empty<LyricLine>();
                        _lastIndex = -1;
                        Text = CleanTitle(info.Title);
                        NextText = string.Empty;
                    });
                    return;
                }
                LrcResult payload = result.Value;
                IReadOnlyList<LyricLine> parsed = payload.IsSynced
                    ? LrcParser.Parse(payload.Lrc)
                    : Array.Empty<LyricLine>();

                _ = _dispatcher.BeginInvoke(() =>
                {
                    _lines = parsed;
                    _lastIndex = -1;

                    if (parsed.Count == 0)
                    {
                        // 无时间轴：显示纯文本首行，至少让用户看到歌词存在。
                        Text = FirstLine(payload.Lrc, info.Title);
                        NextText = string.Empty;
                        return;
                    }

                    // 关键：拿到时间轴后立即按当前位置刷新一次，
                    // 否则要等 OnTick 发现 index 变化，而 index 未变时会一直空白。
                    RefreshFromClock(force: true);
                });
            }
            catch
            {
                _ = _dispatcher.BeginInvoke(() => Text = info.Title);
            }
        }

        /// <summary>
        /// 查询歌词。优先带时间轴，取不到时退回纯文本。
        /// </summary>
        private static async Task<LrcResult> FetchLrcAsync(string rawTitle, string artist)
        {
            string title = CleanTitle(rawTitle);

            // 1) 精确匹配
            string getUrl = "https://lrclib.net/api/get"
                + "?track_name=" + Uri.EscapeDataString(title)
                + "&artist_name=" + Uri.EscapeDataString(artist);
            LrcResult? exact = await TryReadAsync(getUrl).ConfigureAwait(false);
            if (exact is { IsEmpty: false })
            {
                return exact.Value;
            }

            // 2) 搜索匹配：优先带时间轴
            string searchUrl = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(title + " " + artist);
            try
            {
                string json = await Http.GetStringAsync(searchUrl).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return default;
                }

                LrcResult plainFallback = default;
                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    LrcResult r = ReadItem(item);
                    if (r.IsEmpty)
                    {
                        continue;
                    }

                    if (r.IsSynced)
                    {
                        return r;
                    }

                    if (plainFallback.IsEmpty)
                    {
                        plainFallback = r;
                    }
                }

                return plainFallback;
            }
            catch
            {
                return default;
            }
        }

        private static async Task<LrcResult?> TryReadAsync(string url)
        {
            try
            {
                string json = await Http.GetStringAsync(url).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                return ReadItem(doc.RootElement);
            }
            catch
            {
                return null;
            }
        }

        private static LrcResult ReadItem(JsonElement item)
        {
            if (item.TryGetProperty("syncedLyrics", out JsonElement synced)
                && synced.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(synced.GetString()))
            {
                return new LrcResult(synced.GetString()!, true);
            }

            if (item.TryGetProperty("plainLyrics", out JsonElement plain)
                && plain.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(plain.GetString()))
            {
                return new LrcResult(plain.GetString()!, false);
            }

            return default;
        }

        /// <summary>去掉播放器加在曲名外层的括号、空白等修饰。</summary>
        private static string CleanTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            string t = title.Trim();

            // 反复剥离成对的包裹符，例如 "[Sunrise]" -> "Sunrise"
            while (t.Length >= 2)
            {
                char first = t[0];
                char last = t[^1];
                bool paired =
                    (first == (char)91 && last == (char)93) ||
                    (first == (char)40 && last == (char)41) ||
                    (first == (char)0x300A && last == (char)0x300B) ||
                    (first == (char)0x3010 && last == (char)0x3011) ||
                    (first == (char)34 && last == (char)34) ||
                    (first == (char)39 && last == (char)39);

                if (!paired)
                {
                    break;
                }

                t = t.Substring(1, t.Length - 2).Trim();
            }

            // 去掉结尾的版本标记，例如 "写给黄淮 (Live)" / "Song - Remastered"。
            // 这类后缀会让 LRCLIB 精确匹配失败。
            string[] suffixes =
            [
                " (Live)", "(Live)", " [Live]", "[Live]",
                " (live)", "(live)", " - Live", "- Live",
                " (Remastered)", "(Remastered)", " - Remastered", "- Remastered",
                " (Acoustic)", "(Acoustic)", " (伴奏)", "(伴奏)"
            ];
            foreach (string suffix in suffixes)
            {
                if (t.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    string trimmed = t.Substring(0, t.Length - suffix.Length).Trim();
                    if (trimmed.Length > 0)
                    {
                        t = trimmed;
                        break;
                    }
                }
            }

            return t.Length == 0 ? title.Trim() : t;
        }

        /// <summary>取纯文本歌词的首个非空行，用于无时间轴时的静态展示。</summary>
        private static string FirstLine(string? plain, string fallback)
        {
            if (string.IsNullOrWhiteSpace(plain))
            {
                return fallback;
            }

            foreach (string line in plain.Split((char)10))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }

            return fallback;
        }

        /// <summary>歌词查询结果：内容 + 是否带时间轴。</summary>
        private readonly record struct LrcResult(string Lrc, bool IsSynced)
        {
            public bool IsEmpty => string.IsNullOrWhiteSpace(Lrc);
        }

        protected override void DisposeCore()
        {
            _ticker.Stop();
            _ticker.Tick -= OnTick;
            _media.TrackChanged -= OnTrackChanged;
        }
    }
}
