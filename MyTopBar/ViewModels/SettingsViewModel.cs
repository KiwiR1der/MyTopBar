using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Models.Settings;
using MyTopBar.Services;
using System.Collections.ObjectModel;
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

        /// <summary>
        /// 构造期间为 true。此时通过属性赋值只是为了把配置同步到界面，
        /// 不应再反向写回配置、触发 SettingsApplied 或落盘。
        /// </summary>
        private bool _loading = true;

        [ObservableProperty]
        private MaterialOption _material;

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

        [ObservableProperty]
        private bool _hitokotoEnabled;

        /// <summary>可用材质列表，带中文说明，供下拉框绑定。</summary>
        public IReadOnlyList<MaterialOption> Materials { get; } =
        [
            new(MaterialKind.Mica, "Mica（云母，取壁纸配色）"),
            new(MaterialKind.Acrylic, "Acrylic（亚克力，随背景模糊）"),
            new(MaterialKind.MicaAlt, "Mica Alt（云母，对比更强）"),
            new(MaterialKind.None, "None（纯色，不透明）")
        ];

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

        public SettingsViewModel(SettingsService settings, ThemeService theme, Widgets.WidgetRegistry registry)
        {
            _settings = settings;
            _theme = theme;

            AppSettings current = settings.Current;
            _material = Materials.FirstOrDefault(m => m.Kind == current.Appearance.Material) ?? Materials[0];
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

            _hitokotoEnabled = current.Api.Hitokoto.Enabled;

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

        partial void OnMaterialChanged(MaterialOption value)
        {
            if (_loading || value is null)
            {
                return;
            }

            _settings.Current.Appearance.Material = value.Kind;
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

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

            _settings.Current.Api.QWeather.ApiHost = (value ?? string.Empty).Trim();
            OnPropertyChanged(nameof(IsQWeatherConfigured));
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        partial void OnQweatherApiKeyChanged(string value)
        {
            if (_loading)
            {
                return;
            }

            _settings.Current.Api.QWeather.ApiKey = (value ?? string.Empty).Trim();
            OnPropertyChanged(nameof(IsQWeatherConfigured));
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        partial void OnQweatherLatitudeChanged(string value)
        {
            if (_loading)
            {
                return;
            }

            _settings.Current.Api.QWeather.Latitude = (value ?? string.Empty).Trim();
            OnPropertyChanged(nameof(IsQWeatherConfigured));
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        partial void OnQweatherLongitudeChanged(string value)
        {
            if (_loading)
            {
                return;
            }

            _settings.Current.Api.QWeather.Longitude = (value ?? string.Empty).Trim();
            OnPropertyChanged(nameof(IsQWeatherConfigured));
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        partial void OnQweatherIntervalMinutesChanged(double value)
        {
            if (_loading)
            {
                return;
            }

            _settings.Current.Api.QWeather.IntervalMinutes = (int)Math.Clamp(value, 5, 240);
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
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
    }

    /// <summary>材质下拉项：把枚举包装成中文可读文本。</summary>
    public sealed record MaterialOption(MaterialKind Kind, string Display)
    {
        public override string ToString() => Display;
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
}
