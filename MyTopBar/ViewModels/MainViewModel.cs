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
        /// <summary>中区组件集合。</summary>
        public ObservableCollection<WidgetViewModelBase> CenterWidgets => _registry.Center;

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

            // 中区默认顺序：歌词 → 天气 → 时钟 → 文本（与计划一致）
            _registry.Add(lyrics);
            _registry.Add(weather);
            _registry.Add(clock);
            _registry.Add(text);
            _registry.Add(cpu);
            _registry.Add(memory);
            _registry.Add(network);

            _foregroundWindowService.OnForegroundAppInfoChanged += OnCurrentChanged;
            Volume = _audioService.GetVolume();
            _audioService.VolumeChanged += OnSystemVolumeChanged;
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
        }

        /// <summary>按配置重新装配组件。</summary>
        public void ReloadWidgets()
        {
            foreach (WidgetViewModelBase widget in _registry.All)
            {
                widget.Dispose();
            }

            _registry.Rebuild(Settings.Widgets);
            foreach (WidgetViewModelBase widget in _registry.Center.Concat(_registry.Right))
            {
                StartWidget(widget);
            }
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
                    weather.Configure(Settings.Api.QWeatherKey, "101010100", 10);
                    weather.Start();
                    break;
                case LyricsWidgetViewModel lyrics:
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

        /// <summary>拖动排序后把当前顺序写回配置并落盘。</summary>
        public void PersistWidgetOrder()
        {
            Settings.Widgets.Clear();
            int order = 0;
            foreach (WidgetViewModelBase w in _registry.Center.Concat(_registry.Right))
            {
                Settings.Widgets.Add(new WidgetSettings { Id = w.Id, Enabled = w.IsEnabled, Order = order++ });
            }

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
