using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Media.Streaming;

namespace MyTopBar.Services.Media
{
    /// <summary>
    /// 通过 SMTC 读取任意播放器的当前曲目，并在切歌/播放状态变化时通知。
    /// 所有 WinRT 事件都在非 UI 线程触发，统一回到 Dispatcher。
    /// </summary>
    public sealed class MediaSessionService : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private bool _disposed;

        /// <summary>曲目信息变化（含播放/暂停）。</summary>
        public event Action<MediaTrackInfo?>? TrackChanged;

        public MediaTrackInfo? Current { get; private set; }

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

            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                AttachSession(_manager.GetCurrentSession());
            }
            catch
            {
                // 系统不支持 SMTC 或无权限时静默降级，顶栏照常显示
            }
        }

        private void OnCurrentSessionChanged(
            GlobalSystemMediaTransportControlsSessionManager sender,
            CurrentSessionChangedEventArgs args)
        {
            AttachSession(sender.GetCurrentSession());
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

                var snapshot = new MediaTrackInfo(
                    title,
                    props.Artist ?? string.Empty,
                    props.AlbumTitle ?? string.Empty,
                    info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    timeline?.Position ?? TimeSpan.Zero,
                    (timeline?.EndTime ?? TimeSpan.Zero) - (timeline?.StartTime ?? TimeSpan.Zero),
                    _session.SourceAppUserModelId ?? string.Empty);

                RaiseTrackChanged(snapshot);
            }
            catch
            {
                // 播放器切歌瞬间可能抛异常，忽略即可
            }
        }

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
                _manager = null;
            }
        }
    }
}