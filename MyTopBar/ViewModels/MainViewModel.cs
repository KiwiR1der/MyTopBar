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
        private readonly IDialogService _dialogs;

        private readonly LyricsWidgetViewModel _lyrics;
        private readonly TextWidgetViewModel _text;
        private bool _disposed;

        [ObservableProperty]
        private string _currentAppName = "...";

        [ObservableProperty]
        private string _currentWindowTitle = string.Empty;

        [ObservableProperty]
        private float _volume;

        /// <summary>
        /// 音量按钮的矢量图标 key。
        /// 与右区 CPU / 内存 / 网络组件同一套图标体系（见 GaugeIcons.xaml），
        /// 由 GaugeIconConverter 转成 ImageSource。
        /// </summary>
        public string VolumeIcon => "volume";

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
            IDialogService dialogs,
            LyricsWidgetViewModel lyrics,
            TextWidgetViewModel text)
        {
            _foregroundWindowService = foregroundWindowService;
            _powerService = powerService;
            _audioService = audioService;
            _settings = settings;
            _metrics = metrics;
            _registry = registry;
            _dialogs = dialogs;
            Settings = settings.Current;

            /*
             * 中区互斥逻辑需要歌词/文本组件的引用。
             * 直接从 DI 注入（它们是单例），而不是从注册表查——
             * 注册表按配置装配，被禁用的组件不在渲染集合里，
             * 但中区互斥逻辑仍需要引用它们来判断 HasActiveMedia。
             */
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

        /// <summary>
        /// 请求打开 MyTopBar 设置窗口。
        ///
        /// VM 不再自己 new / Show 窗口：那会让 VM 依赖 WPF 窗口类型、无法测试。
        /// 这里只发事件，由 View 层（MainWindow）负责实际的窗口生命周期。
        /// </summary>
        [RelayCommand]
        private void OpenBarSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>View 层据此打开设置窗口。</summary>
        public event EventHandler? SettingsRequested;

        /// <summary>
        /// 设置窗口改动后立即作用到顶栏；具体应用由 View 层订阅。
        /// 由 View 层在设置窗口关闭前调用。
        /// </summary>
        public void ApplySettingsChange()
        {
            _metrics.IntervalMs = Settings.Metrics.IntervalMs;

            // 组件启停/顺序变化后重建集合，让顶栏立即反映设置。
            ReloadWidgets();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>外观/组件布局变化，View 层据此重设高度、重启边缘检测。</summary>
        public event EventHandler? AppearanceChanged;

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

        /// <summary>交给对话框服务，VM 不直接依赖 MessageBox。</summary>
        private bool Confirm(string message, string title) => _dialogs.Confirm(message, title);

        /// <summary>窗口加载后启动各数据源。</summary>
        public void Start()
        {
            _foregroundWindowService.Start();
            UpdateFrom(_foregroundWindowService.Current);

            _metrics.IntervalMs = Settings.Metrics.IntervalMs;
            _metrics.Start();

            _registry.Rebuild(Settings.Widgets);

            // 启动时既配置又启动：这是唯一的「立刻取一次数」入口。
            foreach (WidgetViewModelBase widget in _registry.Center.Concat(_registry.Right))
            {
                ConfigureWidget(widget);
                StartWidget(widget);
            }

            RefreshCenterWidget();
        }

        /// <summary>
        /// 按配置重新装配组件（设置窗口里改了开关或拖动排序后调用）。
        ///
        /// 注意：这里**不能** Dispose 组件。这些组件是单例，数据管道（定时器、事件订阅）
        /// 在构造函数里建立，一旦 Dispose 就再也不会恢复——表现为改组件的开关后
        /// 时钟停走、CPU/内存/网络数值定格。启用状态由 <see cref="WidgetRegistry.Rebuild"/>
        /// 决定谁进入渲染集合，组件本身不需要销毁。
        ///
        /// 也不负责「让组件重新取数」：
        ///   - 时钟/指标/歌词走各自已建立的 Tick 订阅，本来就在跑；
        ///   - 天气和一言只由 <see cref="ApplyWidgetOptions"/> 在配置项真的变化时才重新请求。
        /// 之前这里无条件调 <see cref="StartWidget"/>，导致拖动一下顶栏高度滑杆
        /// 也会发一次按次计费的天气请求。
        /// </summary>
        public void ReloadWidgets()
        {
            _registry.Rebuild(Settings.Widgets);
            ApplyWidgetOptions();
            RefreshCenterWidget();
        }

        /// <summary>
        /// 把配置交给各组件的数据源，并按需启动。
        ///
        /// 每个分支都必须幂等且**只做配置**：DataService 只有在配置真正变化时
        /// 才需要重新请求网络，这部分判断在 DataService 内部完成。
        /// </summary>
        private void ApplyWidgetOptions()
        {
            foreach (WidgetViewModelBase widget in _registry.Center.Concat(_registry.Right))
            {
                ConfigureWidget(widget);
            }
        }

        /// <summary>
        /// 按插件 ID 把配置交给组件的数据源。
        ///
        /// 这里**只配置、不启动**：启动只发生在 <see cref="Start"/> 里。
        /// 「启动」在天气/一言的 DataService 里意味着立刻发一次网络请求，
        /// 如果每次改设置都走一遍，拖一下高度滑杆也会打一次 API。
        /// </summary>
        private void ConfigureWidget(WidgetViewModelBase widget)
        {
            switch (widget.Id)
            {
                case "text":
                    if (widget is TextWidgetViewModel text)
                    {
                        text.Configure(
                            Settings.Api.Hitokoto.Enabled ? TextWidgetMode.Hitokoto : TextWidgetMode.Static,
                            "MyTopBar",
                            Settings.Api.Hitokoto.IntervalMinutes);
                    }
                    break;
                case "weather":
                    if (widget is WeatherWidgetViewModel weather)
                    {
                        weather.Configure(Settings.Api.QWeather);
                    }
                    break;
                case "lyrics":
                    if (widget is LyricsWidgetViewModel lyrics)
                    {
                        lyrics.SetSourceFilter(Settings.Lyrics.SourceAppId);
                    }
                    break;
                case "clock":
                    // 时钟 DataService 在构造时已 Start，无需额外配置。
                    break;
            }
        }

        /// <summary>
        /// 按插件 ID 启动组件，让天气/一言立刻取一次数据。
        ///
        /// 只应在 <see cref="Start"/>（进程启动）时调用。反复调用会让
        /// DataService 每次都打一次网络请求，而天气是按次计费的。
        /// </summary>
        private void StartWidget(WidgetViewModelBase widget)
        {
            switch (widget.Id)
            {
                case "text":
                    (widget as TextWidgetViewModel)?.Start();
                    break;
                case "weather":
                    (widget as WeatherWidgetViewModel)?.Start();
                    break;
                case "lyrics":
                    (widget as LyricsWidgetViewModel)?.Start();
                    break;
            }
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
