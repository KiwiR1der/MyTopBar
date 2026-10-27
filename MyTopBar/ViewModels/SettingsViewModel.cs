using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Models.Settings;
using MyTopBar.Services;
using MyTopBar.Widgets;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace MyTopBar.ViewModels
{
    /// <summary>
    /// 设置窗口。所有改动即时生效并落盘。
    /// </summary>
    public sealed partial class SettingsViewModel : ObservableObject
    {
        private readonly SettingsService _settings;
        private readonly ThemeService _theme;
        private readonly Widgets.LyricsWidgetViewModel _lyrics;

        /// <summary>
        /// 构造期间为 true。此时通过属性赋值只是为了把配置同步到界面，
        /// 不应再反向写回配置、触发 SettingsApplied 或落盘。
        /// </summary>
        private bool _loading = true;

        [ObservableProperty]
        private ThemeOption _selectedTheme;

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

        [ObservableProperty]
        private bool _hitokotoEnabled;

        /// <summary>主题选项（仅浅色 / 深色）。</summary>
        public IReadOnlyList<ThemeOption> ThemeModes { get; } =
        [
            new(ThemeMode.Light, "浅色"),
            new(ThemeMode.Dark, "深色")
        ];

        /// <summary>组件开关列表。</summary>
        public ObservableCollection<WidgetToggleItem> Widgets { get; } = new();

        /// <summary>Id → 组件实例，用于把开关状态同步到运行中的对象。</summary>
        private readonly Dictionary<string, Widgets.WidgetViewModelBase> _widgetById =
            new(StringComparer.OrdinalIgnoreCase);

        public SettingsViewModel(
            SettingsService settings,
            ThemeService theme,
            Widgets.WidgetRegistry registry,
            Widgets.LyricsWidgetViewModel lyrics)
        {
            _settings = settings;
            _theme = theme;
            _lyrics = lyrics;

            AppSettings current = settings.Current;
            _selectedTheme = ThemeModes.FirstOrDefault(t => t.Mode == current.Appearance.Theme) ?? ThemeModes[0];
            _barHeight = current.Appearance.BarHeight;

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

            InitMediaSources(current.Lyrics.SourceAppId, lyrics);

            foreach (Widgets.WidgetViewModelBase widget in registry.All)
            {
                WidgetSettings? saved = current.Widgets.FirstOrDefault(
                    w => string.Equals(w.Id, widget.Id, StringComparison.OrdinalIgnoreCase));
                bool enabled = saved?.Enabled ?? true;
                widget.IsEnabled = enabled;
                _widgetById[widget.Id] = widget;
                Widgets.Add(new WidgetToggleItem(widget.Id, widget.DisplayName, enabled, OnWidgetToggled));
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

        partial void OnBarHeightChanged(double value)
        {
            if (_loading)
            {
                return;
            }

            _settings.Current.Appearance.BarHeight = Math.Clamp(value, 28, 40);
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
            lyrics.AvailableMediaSourcesChanged += OnAvailableMediaSourcesChanged;
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
    }

    /// <summary>主题下拉项。</summary>
    public sealed record ThemeOption(ThemeMode Mode, string Display)
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

            return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? value
                : value;
        }
    }
}
