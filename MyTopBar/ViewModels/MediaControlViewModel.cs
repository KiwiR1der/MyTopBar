using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTopBar.Services;
using MyTopBar.Services.Media;

namespace MyTopBar.ViewModels
{
    /// <summary>
    /// 中区歌词点击后弹出的媒体控制浮窗。
    ///
    /// 数据来源全部是 <see cref="MediaSessionService"/>：
    /// 曲目信息走 TrackChanged 事件，进度条靠 250ms 的全局 Tick 推进，
    /// 三颗按钮直接下发 SMTC 命令。
    /// </summary>
    public sealed partial class MediaControlViewModel : ObservableObject, IDisposable
    {
        private readonly MediaSessionService _media;
        private readonly Dispatcher _dispatcher;
        private readonly GlobalTickService _tick;

        private IDisposable? _tickSubscription;
        private bool _active;
        private bool _disposed;

        /// <summary>
        /// 拖动进度条时暂停用 Tick 回写，否则用户手指还没松开，
        /// 进度条会被每 250ms 一次的 tick 拉回播放位置，根本拖不动。
        /// </summary>
        private bool _isSeeking;

        /// <summary>SMTC 的 Position 不连续，用本地时间外推，与歌词组件同一套算法。</summary>
        private TimeSpan _basePosition;
        private DateTime _baseStamp = DateTime.UtcNow;

        [ObservableProperty]
        private string _title = "无播放";

        [ObservableProperty]
        private string _artist = string.Empty;

        [ObservableProperty]
        private string _album = string.Empty;

        [ObservableProperty]
        private BitmapImage? _artwork;

        [ObservableProperty]
        private bool _isPlaying;

        [ObservableProperty]
        private bool _hasTrack;

        /// <summary>进度条当前位置（秒）。</summary>
        [ObservableProperty]
        private double _positionSeconds;

        /// <summary>曲目总长度（秒）。时长为 0 时隐藏进度条。</summary>
        [ObservableProperty]
        private double _durationSeconds;

        /// <summary>是否存在可用的时长信息，用于控制进度条可见性。</summary>
        public bool HasDuration => DurationSeconds > 0;

        /// <summary>「3:41 / 4:12」形式的时长文本。</summary>
        public string ProgressText => HasDuration
            ? $"{Format(TimeSpan.FromSeconds(PositionSeconds))} / {Format(TimeSpan.FromSeconds(DurationSeconds))}"
            : string.Empty;

        public MediaControlViewModel(
            MediaSessionService media,
            Dispatcher dispatcher,
            GlobalTickService tick)
        {
            _media = media;
            _dispatcher = dispatcher;
            _tick = tick;
            _media.TrackChanged += OnTrackChanged;

            // 构造函数里拿到的可能是启动前就已在播放的会话快照。
            Apply(_media.Current);
        }

        /// <summary>
        /// 浮窗打开时调用：开始推进进度条。
        /// 与音量弹窗一样，Popup 关闭会把 Child 摘出视觉树，
        /// 所以订阅必须能反复进出，不能只在 Dispose 里退订。
        /// </summary>
        public void Activate()
        {
            _active = true;
            Apply(_media.Current);

            _tickSubscription ??= _tick.Subscribe(
                TimeSpan.FromMilliseconds(250),
                OnTick);

            // 打开瞬间主动拉一次最新状态，避免显示的是几分钟前的快照。
            _ = _media.RefreshAsync();
        }

        /// <summary>浮窗关闭时调用：停掉进度推进。</summary>
        public void Deactivate()
        {
            _active = false;
            _tickSubscription?.Dispose();
            _tickSubscription = null;
        }

        private void OnTick()
        {
            if (!_active || _isSeeking || !HasTrack)
            {
                return;
            }

            UpdatePosition();
        }

        private void OnTrackChanged(MediaTrackInfo? track)
        {
            if (_dispatcher.CheckAccess())
            {
                Apply(track);
                return;
            }

            _dispatcher.BeginInvoke(() => Apply(track));
        }

        private void Apply(MediaTrackInfo? track)
        {
            if (track is null)
            {
                HasTrack = false;
                Title = "无播放";
                Artist = string.Empty;
                Album = string.Empty;
                Artwork = null;
                IsPlaying = false;
                DurationSeconds = 0;
                PositionSeconds = 0;
                RaiseProgressChanged();
                PlayPauseGlyph = PlayGlyph;
                return;
            }

            MediaTrackInfo info = track.Value;

            HasTrack = true;
            Title = info.Title;
            Artist = info.Artist;
            Album = info.Album;
            Artwork = info.Artwork;
            IsPlaying = info.IsPlaying;
            PlayPauseGlyph = info.IsPlaying ? PauseGlyph : PlayGlyph;

            _basePosition = info.Position;
            _baseStamp = DateTime.UtcNow;

            DurationSeconds = info.Duration.TotalSeconds;
            UpdatePosition();
        }

        /// <summary>按外推位置刷新进度条与时间文本。</summary>
        private void UpdatePosition()
        {
            MediaTrackInfo? current = _media.Current;
            if (current is null)
            {
                return;
            }

            TimeSpan position;
            if (current.Value.IsPlaying)
            {
                position = _basePosition + (DateTime.UtcNow - _baseStamp);
            }
            else
            {
                position = current.Value.Position;
                _basePosition = position;
                _baseStamp = DateTime.UtcNow;
            }

            TimeSpan duration = current.Value.Duration;
            if (duration > TimeSpan.Zero && position > duration)
            {
                position = duration;
            }

            PositionSeconds = position.TotalSeconds;
            RaiseProgressChanged();
        }

        private void RaiseProgressChanged()
        {
            OnPropertyChanged(nameof(HasDuration));
            OnPropertyChanged(nameof(ProgressText));
        }

        partial void OnDurationSecondsChanged(double value) => RaiseProgressChanged();

        partial void OnPositionSecondsChanged(double value) => RaiseProgressChanged();

        #region 播放控制

        private const string PlayGlyph = "\uE768";
        private const string PauseGlyph = "\uE769";

        /// <summary>播放/暂停按钮的图标字符，随播放状态切换。</summary>
        [ObservableProperty]
        private string _playPauseGlyph = PlayGlyph;

        [RelayCommand]
        private async Task TogglePlayPauseAsync() => await _media.TogglePlayPauseAsync();

        [RelayCommand]
        private async Task PreviousAsync() => await _media.PreviousAsync();

        [RelayCommand]
        private async Task NextAsync() => await _media.NextAsync();

        /// <summary>
        /// 进度条被用户拖动。
        ///
        /// 不是所有播放器都支持 SMTC 跳转（网易云就不支持），
        /// 调用失败时把滑块拉回真实播放位置，避免界面假装跳转成功。
        /// </summary>
        public async Task SeekAsync(double seconds)
        {
            _isSeeking = false;

            if (!HasTrack)
            {
                return;
            }

            bool ok = await _media.SeekAsync(TimeSpan.FromSeconds(seconds));
            if (!ok)
            {
                // 回退到真实位置。
                MediaTrackInfo? current = _media.Current;
                if (current is not null)
                {
                    _basePosition = current.Value.Position;
                    _baseStamp = DateTime.UtcNow;
                }
            }

            UpdatePosition();
        }

        /// <summary>拖动开始：暂停 Tick 回写。</summary>
        public void BeginSeek() => _isSeeking = true;

        #endregion

        /// <summary>把秒数格式化成 m:ss。</summary>
        private static string Format(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
            {
                value = TimeSpan.Zero;
            }

            return $"{(int)value.TotalMinutes}:{value.Seconds:00}";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _media.TrackChanged -= OnTrackChanged;
            Deactivate();
            _disposed = true;
        }
    }
}
