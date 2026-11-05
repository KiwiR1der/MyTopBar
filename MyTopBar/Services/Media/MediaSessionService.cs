using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Media.Streaming;
using Windows.Storage.Streams;

namespace MyTopBar.Services.Media
{
    /// <summary>
    /// 通过 SMTC 读取播放器的当前曲目，并在切歌/播放状态变化时通知。
    /// 所有 WinRT 事件都在非 UI 线程触发，统一回到 Dispatcher。
    ///
    /// 支持按来源应用筛选：同时有 Chrome 和音乐播放器在播放时，
    /// 可以指定只读取音乐播放器的会话（默认行为是跟随系统当前会话，
    /// 那个通常是浏览器，会导致歌词显示成网页里正在播的东西）。
    /// </summary>
    public sealed class MediaSessionService : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private bool _disposed;

        /// <summary>
        /// 只接受该来源的会话（按 SourceAppUserModelId 匹配，忽略大小写）。
        /// 空字符串表示跟随系统当前会话。
        /// </summary>
        private string _sourceFilter = string.Empty;

        /// <summary>曲目信息变化（含播放/暂停）。</summary>
        public event Action<MediaTrackInfo?>? TrackChanged;

        /// <summary>可用会话的来源列表变化（供设置界面展示可选的应用）。</summary>
        public event Action<IReadOnlyList<string>>? AvailableSourcesChanged;

        public MediaTrackInfo? Current { get; private set; }

        /// <summary>当前已知的可选来源，第一个为系统当前会话。</summary>
        public IReadOnlyList<string> AvailableSources { get; private set; } = Array.Empty<string>();

        public MediaSessionService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public async Task StartAsync()
        {
            if (_disposed)
            {
                return;
            }

            // 幂等：Start() 会在每次应用设置时被重复调用，
            // 再 RequestAsync 一次会新建 manager 并叠加事件订阅（旧的不解绑）。
            if (_manager is not null)
            {
                return;
            }

            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                _manager.SessionsChanged += OnSessionsChanged;
                RefreshAvailableSources();
                AttachSession(PickSession());
            }
            catch
            {
                // 系统不支持 SMTC 或无权限时静默降级，顶栏照常显示
            }
        }

        /// <summary>
        /// 设置来源筛选。传空字符串则跟随系统当前会话。
        /// 切换后会立即重新绑定会话。
        /// </summary>
        public void SetSourceFilter(string? sourceAppId)
        {
            string next = (sourceAppId ?? string.Empty).Trim();
            if (string.Equals(next, _sourceFilter, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _sourceFilter = next;

            if (_manager is null)
            {
                return;
            }

            AttachSession(PickSession());
        }

        /// <summary>
        /// 选出要绑定的会话。
        /// 配了筛选就优先找匹配来源的会话；找不到则回退到系统当前会话，
        /// 否则用户指定的播放器没在播放时会整块空白。
        /// </summary>
        private GlobalSystemMediaTransportControlsSession? PickSession()
        {
            if (_manager is null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(_sourceFilter))
            {
                return _manager.GetCurrentSession();
            }

            try
            {
                foreach (GlobalSystemMediaTransportControlsSession candidate in _manager.GetSessions())
                {
                    if (string.Equals(candidate.SourceAppUserModelId, _sourceFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // 枚举失败时退回系统当前会话。
            }

            return _manager.GetCurrentSession();
        }

        private void RefreshAvailableSources()
        {
            if (_manager is null)
            {
                return;
            }

            var sources = new List<string>();
            try
            {
                foreach (GlobalSystemMediaTransportControlsSession session in _manager.GetSessions())
                {
                    string id = session.SourceAppUserModelId ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(id)
                        && !sources.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        sources.Add(id);
                    }
                }
            }
            catch
            {
                // 忽略枚举异常，保持上一次的列表。
            }

            if (sources.SequenceEqual(AvailableSources, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            AvailableSources = sources;
            _dispatcher.BeginInvoke(() => AvailableSourcesChanged?.Invoke(AvailableSources));
        }

        private void OnSessionsChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            SessionsChangedEventArgs args)
        {
            RefreshAvailableSources();

            // 配了筛选时，目标播放器可能是刚启动的那个，需要重新挑一次。
            if (!string.IsNullOrEmpty(_sourceFilter))
            {
                GlobalSystemMediaTransportControlsSession? next = PickSession();
                if (!ReferenceEquals(next, _session))
                {
                    AttachSession(next);
                }
            }
        }

        private void OnCurrentSessionChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            CurrentSessionChangedEventArgs args)
        {
            RefreshAvailableSources();

            // 配了筛选时不跟随系统的当前会话变化，除非当前绑定的会话已经失效。
            if (string.IsNullOrEmpty(_sourceFilter)
                || _session is null)
            {
                AttachSession(PickSession());
            }
        }

        private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
        {
            if (_disposed)
            {
                return;
            }

            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _session = session;

            if (_session is null)
            {
                RaiseTrackChanged(null);
                return;
            }

            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

            _ = RefreshAsync();
        }

        private void OnMediaPropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            MediaPropertiesChangedEventArgs args) => _ = RefreshAsync();

        private void OnPlaybackInfoChanged(
            GlobalSystemMediaTransportControlsSession sender,
            PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();

        private void OnTimelinePropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            TimelinePropertiesChangedEventArgs args) => _ = RefreshAsync();

        public async Task RefreshAsync()
        {
            if (_disposed || _session is null)
            {
                return;
            }

            try
            {
                GlobalSystemMediaTransportControlsSessionMediaProperties props =
                    await _session.TryGetMediaPropertiesAsync();
                GlobalSystemMediaTransportControlsSessionPlaybackInfo? info = _session.GetPlaybackInfo();
                GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline = _session.GetTimelineProperties();

                string title = props.Title ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title))
                {
                    RaiseTrackChanged(null);
                    return;
                }

                string artist = props.Artist ?? string.Empty;
                string album = props.AlbumTitle ?? string.Empty;

                var snapshot = new MediaTrackInfo(
                    title,
                    artist,
                    album,
                    info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    timeline?.Position ?? TimeSpan.Zero,
                    (timeline?.EndTime ?? TimeSpan.Zero) - (timeline?.StartTime ?? TimeSpan.Zero),
                    _session.SourceAppUserModelId ?? string.Empty,
                    await ResolveArtworkAsync(props, title, artist, album));

                RaiseTrackChanged(snapshot);
            }
            catch
            {
                // 播放器切歌瞬间可能抛异常，忽略即可
            }
        }

        // 封面缓存：RefreshAsync 会被播放状态/进度变化频繁触发，
        // 每次重新解码缩略图既慢又浪费内存，这里按曲目 key 缓存一份。
        private BitmapImage? _artwork;
        private string _artworkKey = string.Empty;

        /// <summary>
        /// 取当前曲目的封面。同一首歌复用已解码的 BitmapImage，
        /// 切歌时用 DecodePixelWidth 缩到显示尺寸附近，避免加载原图。
        /// </summary>
        private async Task<BitmapImage?> ResolveArtworkAsync(
            GlobalSystemMediaTransportControlsSessionMediaProperties props,
            string title,
            string artist,
            string album)
        {
            string key = title + "\x01" + artist + "\x01" + album;
            if (string.Equals(key, _artworkKey, StringComparison.Ordinal))
            {
                return _artwork;
            }

            _artworkKey = key;
            _artwork = null;

            try
            {
                IRandomAccessStreamReference? reference = props.Thumbnail;
                if (reference is null)
                {
                    return null;
                }

                /*
                 * WinRT 流桥接到 WPF 要绕开 System.Runtime.WindowsRuntime 的扩展方法
                 * （AsStream / AsStreamForRead 在 net8.0-windows 目标下解析不到）。
                 *
                 * 改用 WinRT 原生的 DataReader 按字节读出，再喂给 MemoryStream：
                 *   - ReadBytes 会自动推进流位置，不需要手动 Seek
                 *   - 封面一般几十 KB，一次性读完代价很低
                 *   - CacheOption=OnLoad 让 BitmapImage 不持有流，MemoryStream 可以立刻释放
                 */
                using IRandomAccessStreamWithContentType winrtStream = await reference.OpenReadAsync();

                uint size = (uint)winrtStream.Size;
                if (size == 0)
                {
                    return null;
                }

                byte[] buffer = new byte[size];
                using (var reader = new DataReader(winrtStream))
                {
                    await reader.LoadAsync(size);
                    reader.ReadBytes(buffer);
                }

                var memory = new System.IO.MemoryStream(buffer);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memory;
                bitmap.EndInit();
                bitmap.Freeze();
                _artwork = bitmap;
            }
            catch
            {
                // 封面读取失败（部分播放器不提供缩略图）时留空，界面显示占位色块。
            }

            return _artwork;
        }

        #region 播放控制

        /// <summary>播放 / 暂停切换。</summary>
        public Task<bool> TogglePlayPauseAsync() => TrySendAsync(
            static s => s.TryTogglePlayPauseAsync().AsTask());

        /// <summary>上一首。</summary>
        public Task<bool> PreviousAsync() => TrySendAsync(
            static s => s.TrySkipPreviousAsync().AsTask());

        /// <summary>下一首。</summary>
        public Task<bool> NextAsync() => TrySendAsync(
            static s => s.TrySkipNextAsync().AsTask());

        /// <summary>
        /// 试跳转到指定位置。
        ///
        /// 注意大部分播放器（含网易云）不支持 SMTC 跳转，
        /// TryChangePlaybackPositionAsync 会返回 false，调用方据结果决定是否提示。
        /// </summary>
        public Task<bool> SeekAsync(TimeSpan position) => TrySendAsync(
            s => s.TryChangePlaybackPositionAsync(position.Ticks).AsTask());

        /// <summary>
        /// 统一包装：会话为空或调用抛异常时返回 false，不向上冒泡。
        /// 发送成功后主动刷新一次，让界面立刻反映新的播放状态。
        /// </summary>
        private async Task<bool> TrySendAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
        {
            if (_disposed || _session is null)
            {
                return false;
            }

            try
            {
                bool ok = await action(_session);
                if (ok)
                {
                    await RefreshAsync();
                }

                return ok;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        private void RaiseTrackChanged(MediaTrackInfo? info)
        {
            Current = info;
            if (_dispatcher.CheckAccess())
            {
                TrackChanged?.Invoke(info);
                return;
            }

            _dispatcher.BeginInvoke(() => TrackChanged?.Invoke(info));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                _session = null;
            }

            if (_manager is not null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager.SessionsChanged -= OnSessionsChanged;
                _manager = null;
            }
        }
    }
}
