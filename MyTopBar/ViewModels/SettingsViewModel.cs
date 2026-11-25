using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Models.Settings;
using MyTopBar.Services;
using MyTopBar.Widgets;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;

namespace MyTopBar.ViewModels
{
    /// <summary>
    /// 设置窗口。所有改动即时生效并落盘。
    /// </summary>
    public sealed partial class SettingsViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;

        private readonly SettingsService _settings;
        private readonly ThemeService _theme;
        private readonly Widgets.LyricsWidgetViewModel _lyrics;
        private readonly Infrastructure.Interfaces.IDialogService _dialogs;
        private readonly FontService _fonts;

        /// <summary>
        /// 待退订的委托。transient VM 订阅单例服务时统一登记在这里，
        /// 由 <see cref="Dispose"/> 集中撤销，避免新增订阅时漏写退订。
        /// </summary>
        private readonly List<Action> _subscriptions = new();

        /// <summary>
        /// 构造期间为 true。此时通过属性赋值只是为了把配置同步到界面，
        /// 不应再反向写回配置、触发 SettingsApplied 或落盘。
        /// </summary>
        private bool _loading = true;

        [ObservableProperty]
        private ThemeOption _selectedTheme;

        [ObservableProperty]
        private TranslucencyOption _selectedTranslucency;

        [ObservableProperty]
        private double _barHeight;

        [ObservableProperty]
        private string _qweatherApiHost = string.Empty;

        [ObservableProperty]
        private string _qweatherApiKey = string.Empty;

        [ObservableProperty]
        private string _qweatherLatitude = "39.92";

        [ObservableProperty]
        private string _qweatherLongitude = "116.41";

        [ObservableProperty]
        private double _qweatherIntervalMinutes = 30;

        /// <summary>和风天气配置是否完整，供界面提示用。</summary>
        public bool IsQWeatherConfigured =>
            !string.IsNullOrWhiteSpace(QweatherApiHost)
            && !string.IsNullOrWhiteSpace(QweatherApiKey)
            && !string.IsNullOrWhiteSpace(QweatherLatitude)
            && !string.IsNullOrWhiteSpace(QweatherLongitude);

        /// <summary>
        /// 界面上的天气参数是否与已生效配置不一致。
        /// true 时「应用」按钮可用，并提示用户还有改动未提交。
        /// </summary>
        [ObservableProperty]
        private bool _hasPendingQWeatherChanges;

        /// <summary>上次应用天气参数的结果，显示在按钮旁边。</summary>
        [ObservableProperty]
        private string _qweatherStatus = string.Empty;

        /// <summary>上次结果是否成功，用于着色。</summary>
        [ObservableProperty]
        private bool _qweatherStatusIsError;

        /// <summary>城市搜索关键字。</summary>
        [ObservableProperty]
        private string _qweatherSearchKeyword = string.Empty;

        /// <summary>城市搜索结果，供下拉列表选择。</summary>
        public ObservableCollection<GeoLocation> QweatherSearchResults { get; } = new();

        /// <summary>已选城市名，显示出来让用户确认选对了。</summary>
        [ObservableProperty]
        private string _qweatherSelectedPlace = "未选择地点";

        /// <summary>城市搜索的状态提示（查询中 / 无结果 / 错误）。</summary>
        [ObservableProperty]
        private string _qweatherSearchStatus = string.Empty;

        /// <summary>搜索是否进行中，用于禁用按钮与显示提示。</summary>
        [ObservableProperty]
        private bool _isQweatherSearching;

        /// <summary>
        /// 可选的媒体来源。第一项固定为「跟随系统」，后面是当前检测到的播放器。
        /// 值为 null 或空表示不筛选。
        /// </summary>
        public ObservableCollection<MediaSourceOption> MediaSources { get; } = new();

        /// <summary>当前选中的媒体来源。</summary>
        [ObservableProperty]
        private MediaSourceOption? _selectedMediaSource;


        // ==========  字体  ==========

        /// <summary>可选字体：「跟随系统」排在首位，其后是系统已安装字体。</summary>
        public IReadOnlyList<string> FontFamilies { get; }

        /// <summary>可选字号（WPF DIP）。</summary>
        public IReadOnlyList<double> FontSizes { get; } = [11, 12, 13, 14, 15, 16];

        /// <summary>可选正文字重。</summary>
        public IReadOnlyList<FontWeightOption> FontWeightOptions { get; } =
        [
            new(FontWeightPreset.Light, "细体 (Light)"),
            new(FontWeightPreset.Regular, "常规 (Regular)"),
            new(FontWeightPreset.Medium, "中等 (Medium)"),
            new(FontWeightPreset.Bold, "粗体 (Bold)"),
            new(FontWeightPreset.Black, "特粗 (Black)")
        ];

        [ObservableProperty]
        private string _selectedFontFamily;

        [ObservableProperty]
        private double _selectedFontSize;

        [ObservableProperty]
        private FontWeightOption _selectedFontWeight;

        /// <summary>当前缩放与换算结果的说明，显示在字号旁边。</summary>
        public string FontScaleHint { get; private set; } = string.Empty;
        [ObservableProperty]
        private bool _hitokotoEnabled;

        [ObservableProperty]
        private bool _launchAtStartup;

        /// <summary>主题选项（仅浅色 / 深色）。</summary>
        public IReadOnlyList<ThemeOption> ThemeModes { get; } =
        [
            new(ThemeMode.Light, "浅色"),
            new(ThemeMode.Dark, "深色")
        ];

        /// <summary>背景通透感档位。</summary>
        public IReadOnlyList<TranslucencyOption> BackdropTranslucencies { get; } =
        [
            new(BackdropTranslucency.UltraClear, "极透"),
            new(BackdropTranslucency.Clear, "通透"),
            new(BackdropTranslucency.Balanced, "均衡"),
            new(BackdropTranslucency.Frosted, "厚重"),
            new(BackdropTranslucency.Opaque, "不透明")
        ];

        /// <summary>当前通透感档位的实时说明。</summary>
        public string BackdropHint { get; private set; } = string.Empty;

        /// <summary>组件开关列表。</summary>
        public ObservableCollection<WidgetToggleItem> Widgets { get; } = new();

        /// <summary>Id → 组件实例，用于把开关状态同步到运行中的对象。</summary>
        private readonly Dictionary<string, Widgets.WidgetViewModelBase> _widgetById =
            new(StringComparer.OrdinalIgnoreCase);

        public SettingsViewModel(
            SettingsService settings,
            ThemeService theme,
            FontService fonts,
            WidgetRegistry registry,
            LyricsWidgetViewModel lyrics,
            Infrastructure.Interfaces.IDialogService dialogs)
        {
            _settings = settings;
            _theme = theme;
            _fonts = fonts;
            _lyrics = lyrics;
            _dialogs = dialogs;

            // 字体列表要在读取配置前准备好，否则下拉框初始值匹配不上。
            FontFamilies = FontService.GetInstalledFontNames();

            AppSettings current = settings.Current;
            _selectedTheme = ThemeModes.FirstOrDefault(t => t.Mode == current.Appearance.Theme) ?? ThemeModes[0];
            _selectedTranslucency = BackdropTranslucencies.FirstOrDefault(t => t.Translucency == current.Appearance.Translucency) ?? BackdropTranslucencies[1];
            _barHeight = current.Appearance.BarHeight;
            UpdateBackdropHint();

            // 字体：配置为空表示跟随系统默认字体。
            string savedFont = string.IsNullOrWhiteSpace(current.Appearance.FontFamily)
                ? FontService.SystemDefaultFontName
                : current.Appearance.FontFamily;
            _selectedFontFamily = FontFamilies.Contains(savedFont) ? savedFont : FontService.SystemDefaultFontName;

            double savedPixels = current.Appearance.FontSizePixels;
            _selectedFontSize = FontSizes.Contains(savedPixels) ? savedPixels : 13;

            _selectedFontWeight = FontWeightOptions.FirstOrDefault(w => w.Weight == current.Appearance.FontWeight)
                ?? FontWeightOptions[1];

            UpdateScaleHint();

            QWeatherSettings qw = current.Api.QWeather;
            /*
             * 通过属性赋值而不是字段，让 [ObservableProperty] 的 setter 触发
             * PropertyChanged。窗口在 DataContext 设置之后才初始化控件，
             * 直接写字段不会通知界面，文本框会显示为空。
             */
            QweatherApiHost = qw.ApiHost;
            QweatherApiKey = qw.ApiKey;
            QweatherLatitude = qw.Latitude;
            QweatherLongitude = qw.Longitude;
            QweatherIntervalMinutes = qw.IntervalMinutes;

            // 已保存过城市名就直接显示，否则退回显示坐标。
            QweatherSelectedPlace = string.IsNullOrWhiteSpace(qw.PlaceName)
                ? $"未选择地点（当前坐标 {qw.Latitude}, {qw.Longitude}）"
                : qw.PlaceName;

            _hitokotoEnabled = current.Api.Hitokoto.Enabled;
            _launchAtStartup = StartupService.IsEnabled();

            InitMediaSources(current.Lyrics.SourceAppId, lyrics);

            /*
             * 遍历所有插件（含未启用的），从注册表找已缓存的实例。
             * 注册表现在只创建一次实例并缓存，Rebuild 不再销毁，
             * 所以这里总能找到实例（除非插件构造失败）。
             */
            foreach (Widgets.IWidgetPlugin plugin in registry.AllPlugins)
            {
                WidgetSettings? saved = current.Widgets.FirstOrDefault(
                    w => string.Equals(w.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));
                bool enabled = saved?.Enabled ?? true;

                // 同步到运行时实例
                Widgets.WidgetViewModelBase? widget = registry.All.FirstOrDefault(
                    w => string.Equals(w.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));
                if (widget is not null)
                {
                    widget.IsEnabled = enabled;
                    _widgetById[plugin.Id] = widget;
                }

                Widgets.Add(new WidgetToggleItem(plugin.Id, plugin.DisplayName, enabled, OnWidgetToggled));
            }

            // 构造完成，之后的属性变化才是用户操作，需要写回配置。
            _loading = false;
        }

        /// <summary>组件开关：写回配置并通知顶栏重建。</summary>
        private void OnWidgetToggled(string id, bool enabled)
        {
            // 运行时对象必须同步，否则 PersistWidgetOrder 读到的仍是旧状态，
            // 会把刚被关掉的组件又写成启用。
            if (_widgetById.TryGetValue(id, out Widgets.WidgetViewModelBase? widget))
            {
                widget.IsEnabled = enabled;
            }

            WidgetSettings? saved = _settings.Current.Widgets.FirstOrDefault(
                w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
            if (saved is null)
            {
                _settings.Current.Widgets.Add(new WidgetSettings { Id = id, Enabled = enabled, Order = _settings.Current.Widgets.Count });
            }
            else
            {
                saved.Enabled = enabled;
            }

            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>设置变化时通知主窗口立即应用（无需重启）。</summary>
        public event EventHandler? SettingsApplied;

        /// <summary>
        /// 切换字体。立即写入配置并应用到全局资源，
        /// 界面通过 DynamicResource 自动重刷，无需重启。
        /// </summary>
        partial void OnSelectedFontFamilyChanged(string value)
        {
            if (_loading || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            _settings.Current.Appearance.FontFamily =
                string.Equals(value, FontService.SystemDefaultFontName, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : value;

            _fonts.Apply(value, SelectedFontSize, SelectedFontWeight.Weight);
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>切换字号（WPF DIP，高分屏按系统缩放自动高清光栅化）。</summary>
        partial void OnSelectedFontSizeChanged(double value)
        {
            if (_loading || value <= 0)
            {
                return;
            }

            _settings.Current.Appearance.FontSizePixels = value;
            _fonts.Apply(SelectedFontFamily, value, SelectedFontWeight.Weight);
            _settings.Save();
            UpdateScaleHint();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>切换正文字重，立即生效并持久化。</summary>
        partial void OnSelectedFontWeightChanged(FontWeightOption value)
        {
            if (_loading || value is null)
            {
                return;
            }

            _settings.Current.Appearance.FontWeight = value.Weight;
            _fonts.Apply(SelectedFontFamily, SelectedFontSize, value.Weight);
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>刷新「当前缩放 → 实际物理像素」的说明文字。</summary>
        private void UpdateScaleHint()
        {
            double scale = FontService.GetSystemScale();
            double physicalPx = Math.Round(SelectedFontSize * scale, 1);
            FontScaleHint = $"当前缩放 {scale * 100:0}%，{SelectedFontSize:0} DIP ≈ {physicalPx:0.#} 物理像素";
            OnPropertyChanged(nameof(FontScaleHint));
        }

        partial void OnBarHeightChanged(double value)
        {
            if (_loading)
            {
                return;
            }

            _settings.Current.Appearance.BarHeight = BarHeightRange.Clamp(value);
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        partial void OnSelectedThemeChanged(ThemeOption value)
        {
            if (_loading || value is null)
            {
                return;
            }

            _settings.Current.Appearance.Theme = value.Mode;
            _theme.Apply(value.Mode);
            _settings.Save();
            UpdateBackdropHint();
        }

        partial void OnSelectedTranslucencyChanged(TranslucencyOption value)
        {
            if (_loading || value is null)
            {
                return;
            }

            _settings.Current.Appearance.Translucency = value.Translucency;
            _theme.ApplyTranslucency(value.Translucency);
            _settings.Save();
            UpdateBackdropHint();
        }

        private void UpdateBackdropHint()
        {
            BackdropTranslucency translucency = SelectedTranslucency.Translucency;
            bool isDark = SelectedTheme.Mode == ThemeMode.Dark;

            System.Windows.Media.Color barColor = ThemeService.GetAcrylicTintColor(isDark, translucency, isBar: true);
            int opacityPercent = (int)Math.Round(barColor.A / 255.0 * 100.0);
            BackdropHint = $"顶栏磨砂遮罩约 {opacityPercent}%";

            OnPropertyChanged(nameof(BackdropHint));
        }

        partial void OnQweatherApiHostChanged(string value)
        {
            if (_loading)
            {
                return;
            }

            RefreshQWeatherPending();
        }

        partial void OnQweatherApiKeyChanged(string value)
        {
            if (_loading)
            {
                return;
            }

            RefreshQWeatherPending();
        }

        partial void OnQweatherLatitudeChanged(string value)
        {
            if (_loading)
            {
                return;
            }

            // 只更新「待应用」状态，不写配置、不触发请求。
            // 天气参数由「应用」按钮统一提交，见 ApplyQWeatherCommand。
            RefreshQWeatherPending();
        }

        partial void OnQweatherLongitudeChanged(string value)
        {
            if (_loading)
            {
                return;
            }

            RefreshQWeatherPending();
        }

        partial void OnQweatherIntervalMinutesChanged(double value)
        {
            if (_loading)
            {
                return;
            }

            RefreshQWeatherPending();
        }

        partial void OnHitokotoEnabledChanged(bool value)
        {
            if (_loading)
            {
                return;
            }

            _settings.Current.Api.Hitokoto.Enabled = value;
            _settings.Save();
        }

        /// <summary>
        /// 开机自启：状态以注册表为准，不写进 settings.json。
        /// 用户在任务管理器「启动」页改动后，下次打开设置会读到真实状态。
        /// </summary>
        partial void OnLaunchAtStartupChanged(bool value)
        {
            if (_loading)
            {
                return;
            }

            // 写注册表后以实际结果回写属性，失败时（如权限问题）界面状态不会假成功。
            bool actual = StartupService.SetEnabled(value);
            if (actual != value)
            {
                _launchAtStartup = actual;
                OnPropertyChanged(nameof(LaunchAtStartup));
            }
        }

        /// <summary>
        /// 按关键字搜索城市。用 GeoAPI 把城市名换成经纬度，
        /// 免去手填坐标。
        /// </summary>
        [RelayCommand]
        private async Task SearchCityAsync()
        {
            if (IsQweatherSearching)
            {
                return;
            }

            string keyword = (QweatherSearchKeyword ?? string.Empty).Trim();
            if (keyword.Length == 0)
            {
                QweatherSearchStatus = "请输入城市名称，例如「杭州」「Tokyo」";
                return;
            }

            IsQweatherSearching = true;
            QweatherSearchStatus = "查询中…";
            QweatherSearchResults.Clear();

            try
            {
                GeoLookupResult result = await GeoLookup
                    .SearchAsync(QweatherApiHost, QweatherApiKey, keyword, "zh")
                    .ConfigureAwait(true);

                if (!result.Ok)
                {
                    QweatherSearchStatus = result.Error;
                    return;
                }

                foreach (GeoLocation location in result.Locations)
                {
                    QweatherSearchResults.Add(location);
                }

                QweatherSearchStatus = $"找到 {result.Locations.Count} 个结果，点击选择";
            }
            finally
            {
                IsQweatherSearching = false;
            }
        }

        /// <summary>选中某个搜索结果：填回经纬度并记录名称。</summary>
        [RelayCommand]
        private void SelectCity(GeoLocation? location)
        {
            if (location is null)
            {
                return;
            }

            // GeoAPI 返回五位小数，天气接口要求最多两位，这里统一收敛。
            QweatherLatitude = GeoLookup.FormatCoordinate(location.Latitude);
            QweatherLongitude = GeoLookup.FormatCoordinate(location.Longitude);
            QweatherSelectedPlace = location.Display;

            QweatherSearchStatus = $"已选择 {location.Display}";
            RefreshQWeatherPending();
        }

        /// <summary>
        /// 初始化媒体来源下拉框。
        ///
        /// 第一项固定是「跟随系统」，之后是当前检测到的会话来源。
        /// 保存过的来源即使当前没在播放也要保留在列表里，
        /// 否则用户配好之后重启应用就看不到自己选的那一项了。
        /// </summary>
        private void InitMediaSources(string savedSourceId, Widgets.LyricsWidgetViewModel lyrics)
        {
            RebuildMediaSources(lyrics.AvailableMediaSources, savedSourceId);

            // 播放器打开/关闭时刷新列表，让用户不必重启应用就能选到新来源。
            // 注意：lyrics 是 DI 单例，本 VM 是 transient，订阅必须在 Dispose 里退订，
            // 否则每打开一次设置窗口就会有一个 VM 被单例永久持有（内存泄漏）。
            lyrics.AvailableMediaSourcesChanged += OnAvailableMediaSourcesChanged;
            _subscriptions.Add(() => lyrics.AvailableMediaSourcesChanged -= OnAvailableMediaSourcesChanged);
        }

        private void OnAvailableMediaSourcesChanged(IReadOnlyList<string> sources)
        {
            string current = SelectedMediaSource?.AppId ?? string.Empty;
            RebuildMediaSources(sources, current);
        }

        private void RebuildMediaSources(IReadOnlyList<string> sources, string selectedId)
        {
            MediaSources.Clear();

            MediaSources.Add(MediaSourceOption.FollowSystem);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in sources)
            {
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                {
                    MediaSources.Add(new MediaSourceOption(id, MediaSourceOption.FriendlyName(id)));
                }
            }

            // 已保存但当前未检测到的来源补一项，避免选项"消失"。
            if (!string.IsNullOrWhiteSpace(selectedId) && !seen.Contains(selectedId))
            {
                MediaSources.Add(new MediaSourceOption(selectedId, MediaSourceOption.FriendlyName(selectedId)));
            }

            SelectedMediaSource = MediaSources.FirstOrDefault(
                s => string.Equals(s.AppId, selectedId, StringComparison.OrdinalIgnoreCase))
                ?? MediaSources[0];
        }

        partial void OnSelectedMediaSourceChanged(MediaSourceOption? value)
        {
            if (_loading)
            {
                return;
            }

            string id = value?.AppId ?? string.Empty;
            _settings.Current.Lyrics.SourceAppId = id;
            _settings.Save();

            // 立刻让歌词组件改用新来源，不必重启。
            _lyrics.SetSourceFilter(id);
        }

        /// <summary>
        /// 判断界面上的值是否与已生效配置不同。
        /// 逐字段比较而不是监听每个输入框，避免"删掉又打回去"这种操作被误判。
        /// </summary>
        private void RefreshQWeatherPending()
        {
            QWeatherSettings saved = _settings.Current.Api.QWeather;

            bool pending =
                !string.Equals((QweatherApiHost ?? string.Empty).Trim(), saved.ApiHost, StringComparison.Ordinal)
                || !string.Equals((QweatherApiKey ?? string.Empty).Trim(), saved.ApiKey, StringComparison.Ordinal)
                || !string.Equals((QweatherLatitude ?? string.Empty).Trim(), saved.Latitude, StringComparison.Ordinal)
                || !string.Equals((QweatherLongitude ?? string.Empty).Trim(), saved.Longitude, StringComparison.Ordinal)
                || (int)Math.Clamp(QweatherIntervalMinutes, 5, 240) != saved.IntervalMinutes
                || (!QweatherSelectedPlace.StartsWith("未选择地点")
                    && !string.Equals(QweatherSelectedPlace, saved.PlaceName, StringComparison.Ordinal));

            HasPendingQWeatherChanges = pending;
            OnPropertyChanged(nameof(IsQWeatherConfigured));
        }

        /// <summary>
        /// 把界面上的天气参数写入配置并立刻请求一次。
        /// 结果回显到按钮旁，不再静默丢弃。
        /// </summary>
        [RelayCommand]
        private async Task ApplyQWeatherAsync()
        {
            if (!IsQWeatherConfigured)
            {
                QweatherStatusIsError = true;
                QweatherStatus = "请先填齐 API Host、API Key 和经纬度";
                return;
            }

            if (!double.TryParse(QweatherLatitude, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                || !double.TryParse(QweatherLongitude, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                QweatherStatusIsError = true;
                QweatherStatus = "经纬度必须是数字，例如 39.92";
                return;
            }

            QWeatherSettings target = _settings.Current.Api.QWeather;
            target.ApiHost = (QweatherApiHost ?? string.Empty).Trim();
            target.ApiKey = (QweatherApiKey ?? string.Empty).Trim();
            target.Latitude = (QweatherLatitude ?? string.Empty).Trim();
            target.Longitude = (QweatherLongitude ?? string.Empty).Trim();
            target.IntervalMinutes = (int)Math.Clamp(QweatherIntervalMinutes, 5, 240);

            // 只有通过搜索选过地点才记住名字，手填坐标时保持原值。
            if (QweatherSelectedPlace is { Length: > 0 } && !QweatherSelectedPlace.StartsWith("未选择地点"))
            {
                target.PlaceName = QweatherSelectedPlace;
            }

            _settings.Save();

            HasPendingQWeatherChanges = false;
            QweatherStatusIsError = false;
            QweatherStatus = "正在请求…";

            // 立刻按新参数请求一次，把真实结果回显给用户。
            WeatherProbeResult result = await WeatherProbe.ProbeAsync(target).ConfigureAwait(true);

            QweatherStatusIsError = !result.Ok;
            QweatherStatus = result.Message;

            if (result.Ok)
            {
                // 成功后让顶栏按新配置刷新。
                SettingsApplied?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 用资源管理器打开日志目录，方便排查问题。
        /// 目录不存在时先创建（确保首次点击也能打开）。
        /// </summary>
        [RelayCommand]
        private void OpenLogDirectory()
        {
            // 和 AppLog 用同一个来源，避免「按钮打开的目录」和「日志实际写入的目录」不一致。
            string logDir = AppPaths.LogDirectory;

            try
            {
                Directory.CreateDirectory(logDir);
                Process.Start("explorer.exe", logDir);
            }
            catch (Exception ex)
            {
                Infrastructure.Common.AppLog.Error("打开日志目录失败", ex);
                _dialogs.ShowWarning($"无法打开日志目录：{logDir}", "MyTopBar");
            }
        }

        /// <summary>
        /// 释放对外订阅。
        ///
        /// 本 VM 由 DI 以 transient 注册（每开一次设置窗口一个），
        /// 却订阅了单例服务的事件；不退订就等于把整个 VM（及其 ObservableCollection）
        /// 挂在单例上永不回收。由 SettingsWindow.OnClosed 调用。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (Action unsubscribe in _subscriptions)
            {
                unsubscribe();
            }

            _subscriptions.Clear();
        }
    }

    /// <summary>主题下拉项。</summary>
    public sealed record ThemeOption(ThemeMode Mode, string Display)
    {
        public override string ToString() => Display;
    }

    /// <summary>背景通透感下拉项。</summary>
    public sealed record TranslucencyOption(BackdropTranslucency Translucency, string Display)
    {
        public override string ToString() => Display;
    }

    /// <summary>正文字重下拉项。</summary>
    public sealed record FontWeightOption(FontWeightPreset Weight, string Display)
    {
        public override string ToString() => Display;
    }
    /// <summary>设置界面里的单个组件开关。</summary>
    public sealed partial class WidgetToggleItem : ObservableObject
    {
        private readonly Action<string, bool> _onChanged;

        /// <summary>
        /// 用 bool? 承接 CheckBox.IsChecked（它是 bool?）。
        /// 若用 bool 绑定，取消勾选时 WPF 会传 null，属性可能不更新，
        /// 导致界面显示已关闭但配置里仍是 true —— 这正是组件开关错乱的成因。
        /// </summary>
        [ObservableProperty]
        private bool? _isEnabled;

        public string Id { get; }
        public string DisplayName { get; }

        public WidgetToggleItem(string id, string displayName, bool isEnabled, Action<string, bool> onChanged)
        {
            Id = id;
            DisplayName = displayName;
            _isEnabled = isEnabled;
            _onChanged = onChanged;
        }

        partial void OnIsEnabledChanged(bool? value)
        {
            // null 视为未勾选，避免把 null 写入配置。
            _onChanged(Id, value ?? false);
        }
    }

    /// <summary>
    /// 媒体来源下拉项。AppId 为空表示跟随系统当前会话。
    /// </summary>
    public sealed record MediaSourceOption(string AppId, string Display)
    {
        /// <summary>「跟随系统」这一项。</summary>
        public static MediaSourceOption FollowSystem { get; } = new(string.Empty, "跟随系统当前会话");

        public override string ToString() => Display;

        /// <summary>
        /// 把 SMTC 的 SourceAppUserModelId 转成可读名称。
        ///
        /// AUMID 形如：
        ///   chrome.exe
        ///   SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify
        ///   Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic
        ///   C:\Program Files\...\cloudmusic.exe
        /// 取感叹号前的包名、去掉版本后缀，比原样显示友好得多。
        /// </summary>
        public static string FriendlyName(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
            {
                return FollowSystem.Display;
            }

            string value = appId.Trim();

            // 取包名部分（! 之后是应用内入口名，对用户没意义）。
            int bang = value.IndexOf('!');
            if (bang > 0)
            {
                value = value[..bang];
            }

            // 去掉包名里的发布者哈希后缀：Microsoft.ZuneMusic_8wekyb3d8bbwe
            int underscore = value.LastIndexOf('_');
            if (underscore > 0 && underscore < value.Length - 1)
            {
                value = value[..underscore];
            }

            // 完整路径只留文件名。
            if (value.Contains('\\') || value.Contains('/'))
            {
                int slash = value.LastIndexOfAny(['\\', '/']);
                if (slash >= 0 && slash < value.Length - 1)
                {
                    value = value[(slash + 1)..];
                }
            }

            return value;
        }
    }
}
