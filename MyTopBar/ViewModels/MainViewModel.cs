using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTopBar.Infrastructure.Interfaces;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Models;
using MyTopBar.Models.Settings;
using MyTopBar.Services;
using MyTopBar.Services.Lyrics;
using MyTopBar.Widgets;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace MyTopBar.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IForegroundWindowService _foregroundWindowService;
        private readonly PowerService _powerService;
        private readonly AudioService _audioService;
        private readonly SettingsService _settings;
        private readonly SystemMetricsService _metrics;
        private readonly WidgetRegistry _registry;

        private readonly LyricsWidgetViewModel _lyrics;
        private readonly TextWidgetViewModel _text;
        private bool _disposed;

        [ObservableProperty]
        private string _currentAppName = "...";

        [ObservableProperty]
        private string _currentWindowTitle = string.Empty;

        [ObservableProperty]
        private float _volume;

        [ObservableProperty]
        private MaterialApplyResult _lastMaterialResult;

        [ObservableProperty]
        private string _materialStatus = string.Empty;
        /// <summary>
        /// 中区实际渲染的组件集合。中区同一时刻最多一个组件：
        /// 有媒体播放时显示歌词，否则显示文本/一言，二者互斥。
        /// </summary>
        public ObservableCollection<WidgetViewModelBase> CenterWidgets { get; } = new();

        /// <summary>右区组件集合。</summary>
        public ObservableCollection<WidgetViewModelBase> RightWidgets => _registry.Right;

        public AppSettings Settings { get; }

        public MainViewModel(
            IForegroundWindowService foregroundWindowService,
            PowerService powerService,
            AudioService audioService,
            SettingsService settings,
            SystemMetricsService metrics,
            WidgetRegistry registry,
            ClockWidgetViewModel clock,
            LyricsWidgetViewModel lyrics,
            WeatherWidgetViewModel weather,
            TextWidgetViewModel text,
            CpuWidgetViewModel cpu,
            MemoryWidgetViewModel memory,
            NetworkWidgetViewModel network)
        {
            _foregroundWindowService = foregroundWindowService;
            _powerService = powerService;
            _audioService = audioService;
            _settings = settings;
            _metrics = metrics;
            _registry = registry;
            Settings = settings.Current;

            // 中区只保留「歌词 / 文本」，二者互斥（同一时刻只显示一个，见 CenterWidgets 计算）
            _registry.Add(lyrics);
            _registry.Add(text);

            // 右区默认顺序：网络 → CPU → 内存 → 天气 → 时钟（音量按钮固定在右区最右，见 XAML）
            _registry.Add(network);
            _registry.Add(cpu);
            _registry.Add(memory);
            _registry.Add(weather);
            _registry.Add(clock);

            _lyrics = lyrics;
            _text = text;
            _lyrics.PropertyChanged += OnLyricsPropertyChanged;

            _foregroundWindowService.OnForegroundAppInfoChanged += OnCurrentChanged;
            Volume = _audioService.GetVolume();
            _audioService.VolumeChanged += OnSystemVolumeChanged;
        }

        /// <summary>歌词组件的播放状态变化时，重新决定中区显示哪一个。</summary>
        private void OnLyricsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LyricsWidgetViewModel.HasActiveMedia))
            {
                RefreshCenterWidget();
            }
        }

        /// <summary>
        /// 中区互斥：有音乐播歌词，无音乐显一言/文本。两者不会同时出现在顶栏上。
        /// </summary>
        private void RefreshCenterWidget()
        {
            WidgetViewModelBase? target = _lyrics.HasActiveMedia ? _lyrics : _text;

            // 组件被用户在设置里关掉时中区留空，不强行回退。
            if (!target.IsEnabled)
            {
                target = null;
            }

            if (CenterWidgets.Count == 1 && ReferenceEquals(CenterWidgets[0], target))
            {
                return;
            }

            CenterWidgets.Clear();
            if (target is not null)
            {
                CenterWidgets.Add(target);
            }
        }

        [RelayCommand]
        private void AboutThisPc() => _powerService.OpenSettings("ms-settings:about");

        [RelayCommand]
        private void OpenSettings() => _powerService.OpenSettings("ms-settings:");
        /// <summary>已打开的自用设置窗口，避免重复打开。</summary>
        private Views.SettingsWindow? _settingsWindow;

        [RelayCommand]
        private void OpenBarSettings()
        {
            if (_settingsWindow is { IsLoaded: true })
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new Views.SettingsWindow();
            _settingsWindow.SettingsApplied += OnBarSettingsApplied;
            _settingsWindow.Closed += (_, _) =>
            {
                if (_settingsWindow is not null)
                {
                    _settingsWindow.SettingsApplied -= OnBarSettingsApplied;
                    _settingsWindow = null;
                }
            };
            _settingsWindow.Show();
        }

        /// <summary>设置窗口改动后立即作用到顶栏；具体应用由 View 层订阅。</summary>
        public event EventHandler? AppearanceChanged;

        private void OnBarSettingsApplied(object? sender, EventArgs e)
        {
            _metrics.IntervalMs = Settings.Metrics.IntervalMs;

            // 组件启停/顺序变化后重建集合，让顶栏立即反映设置。
            ReloadWidgets();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }


        [RelayCommand]
        private void Shutdown()
        {
            if (Confirm("确定要关闭电脑吗？", "确认关机"))
            {
                _powerService.Shutdown();
            }
        }

        [RelayCommand]
        private void Sleep()
        {
            if (Confirm("确定要让电脑进入睡眠吗？", "确认睡眠"))
            {
                _powerService.Sleep();
            }
        }

        [RelayCommand]
        private void Restart()
        {
            if (Confirm("确定要重新启动电脑吗？", "确认重启"))
            {
                _powerService.Restart();
            }
        }

        [RelayCommand]
        private void Exit()
        {
            if (Confirm("确定要退出 MyTopBar 吗？", "确认退出"))
            {
                App.Current.Shutdown();
            }
        }

        private static bool Confirm(string message, string title)
            => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;

        /// <summary>窗口加载后启动各数据源。</summary>
        public void Start()
        {
            _foregroundWindowService.Start();
            UpdateFrom(_foregroundWindowService.Current);

            _metrics.IntervalMs = Settings.Metrics.IntervalMs;
            _metrics.Start();

            _registry.Rebuild(Settings.Widgets);

            foreach (WidgetViewModelBase widget in _registry.Center.Concat(_registry.Right))
            {
                StartWidget(widget);
            }

            RefreshCenterWidget();
        }

        /// <summary>
        /// 按配置重新装配组件。
        ///
        /// 注意：这里**不能** Dispose 组件。这些组件是单例，数据管道（定时器、事件订阅）
        /// 在构造函数里建立，一旦 Dispose 就再也不会恢复——表现为改组件的开关后
        /// 时钟停走、CPU/内存/网络数值定格。启用状态由 <see cref="WidgetRegistry.Rebuild"/>
        /// 决定谁进入渲染集合，组件本身不需要销毁。
        /// </summary>
        public void ReloadWidgets()
        {
            _registry.Rebuild(Settings.Widgets);
            foreach (WidgetViewModelBase widget in _registry.Center.Concat(_registry.Right))
            {
                StartWidget(widget);
            }

            RefreshCenterWidget();
        }

        private void StartWidget(WidgetViewModelBase widget)
        {
            switch (widget)
            {
                case TextWidgetViewModel text:
                    text.Configure(
                        Settings.Api.Hitokoto.Enabled ? TextWidgetMode.Hitokoto : TextWidgetMode.Static,
                        "MyTopBar",
                        Settings.Api.Hitokoto.IntervalMinutes);
                    text.Start();
                    break;
                case WeatherWidgetViewModel weather:
                    weather.Configure(Settings.Api.QWeather);
                    weather.Start();
                    break;
                case LyricsWidgetViewModel lyrics:
                    lyrics.SetSourceFilter(Settings.Lyrics.SourceAppId);
                    lyrics.Start();
                    break;
            }
        }

        /// <summary>材质应用结果回传，供设置界面如实显示实际生效的材质。</summary>
        public void OnMaterialApplied(MaterialApplyResult result)
        {
            LastMaterialResult = result;
            MaterialStatus = result.UsedFallback
                ? $"{result.Applied}（{result.Detail}）"
                : result.Applied.ToString();
        }

        /// <summary>
        /// 拖动排序后把当前顺序写回配置并落盘。
        ///
        /// 以 <see cref="WidgetRegistry.All"/> 为基准而不是当前渲染集合：
        /// 被停用的组件不在 Center/Right 里，若只写回渲染集合就会把它们的配置条目抹掉，
        /// 而「不在配置里」的组件会被 <see cref="WidgetRegistry.Rebuild"/> 视为默认启用，
        /// 结果就是关掉的组件在拖动排序后自己回来了。
        /// 原有的 Options 也要原样保留。
        /// </summary>
        public void PersistWidgetOrder()
        {
            int order = 0;
            var kept = new List<WidgetSettings>();

            foreach (WidgetViewModelBase widget in _registry.All)
            {
                WidgetSettings entry = Settings.Widgets
                    .FirstOrDefault(s => string.Equals(s.Id, widget.Id, StringComparison.OrdinalIgnoreCase))
                    ?? new WidgetSettings { Id = widget.Id };

                entry.Enabled = widget.IsEnabled;

                // 只有当前参与渲染的组件才占用顺序号，停用的保留原有 Order。
                if (_registry.Center.Contains(widget) || _registry.Right.Contains(widget))
                {
                    entry.Order = order++;
                }

                kept.Add(entry);
            }

            // 丢弃已经不在注册表里的陈旧条目。
            Settings.Widgets.Clear();
            Settings.Widgets.AddRange(kept);

            _settings.Save();
        }

        /// <summary>在同一区域内把 source 移动到 target 的位置。</summary>
        public void MoveWidget(WidgetViewModelBase source, WidgetViewModelBase target)
        {
            ObservableCollection<WidgetViewModelBase>? zone = _registry.Center.Contains(source)
                ? _registry.Center
                : _registry.Right.Contains(source) ? _registry.Right : null;

            if (zone is null || !zone.Contains(target))
            {
                return;
            }

            int from = zone.IndexOf(source);
            int to = zone.IndexOf(target);
            if (from >= 0 && to >= 0 && from != to)
            {
                zone.Move(from, to);
            }
        }

        private void OnCurrentChanged(object? sender, ForegroundAppInfo? app) => UpdateFrom(app);

        private void UpdateFrom(ForegroundAppInfo? app)
        {
            CurrentAppName = string.IsNullOrWhiteSpace(app?.DisplayName) ? "..." : app.DisplayName;
            CurrentWindowTitle = app?.WindowTitle ?? string.Empty;
        }

        private void OnSystemVolumeChanged(float volume, bool muted) => Volume = volume;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _audioService.VolumeChanged -= OnSystemVolumeChanged;
            _foregroundWindowService.OnForegroundAppInfoChanged -= OnCurrentChanged;
            _foregroundWindowService.Stop();
            _metrics.Stop();
            _registry.DisposeAll();
            _settings.Flush();
            _disposed = true;
        }
    }
}
