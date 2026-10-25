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

        [ObservableProperty]
        private MaterialOption _material;

        [ObservableProperty]
        private ThemeOption _selectedTheme;

        [ObservableProperty]
        private double _barHeight;

        [ObservableProperty]
        private string _qweatherKey = string.Empty;

        [ObservableProperty]
        private bool _hitokotoEnabled;

        /// <summary>可用材质列表，带中文说明，供下拉框绑定。</summary>
        public IReadOnlyList<MaterialOption> Materials { get; } =
        [
            new(MaterialKind.Mica, "Mica（云母，Win11 推荐）"),
            new(MaterialKind.Acrylic, "Acrylic（亚克力磨砂）"),
            new(MaterialKind.Blur, "Blur（经典模糊，Win11 会回退 Acrylic）"),
            new(MaterialKind.None, "None（纯色，不透明）")
        ];

        /// <summary>主题选项。</summary>
        public IReadOnlyList<ThemeOption> ThemeModes { get; } =
        [
            new(ThemeMode.System, "跟随系统"),
            new(ThemeMode.Light, "浅色"),
            new(ThemeMode.Dark, "深色")
        ];

        /// <summary>组件开关列表。</summary>
        public ObservableCollection<WidgetToggleItem> Widgets { get; } = new();
        public SettingsViewModel(SettingsService settings, ThemeService theme, Widgets.WidgetRegistry registry)
        {
            _settings = settings;
            _theme = theme;

            AppSettings current = settings.Current;
            _material = Materials.FirstOrDefault(m => m.Kind == current.Appearance.Material) ?? Materials[0];
            _selectedTheme = ThemeModes.FirstOrDefault(t => t.Mode == current.Appearance.Theme) ?? ThemeModes[0];
            _barHeight = current.Appearance.BarHeight;
            _qweatherKey = current.Api.QWeatherKey;
            _hitokotoEnabled = current.Api.Hitokoto.Enabled;

            foreach (Widgets.WidgetViewModelBase widget in registry.All)
            {
                WidgetSettings? saved = current.Widgets.FirstOrDefault(
                    w => string.Equals(w.Id, widget.Id, StringComparison.OrdinalIgnoreCase));
                bool enabled = saved?.Enabled ?? true;
                widget.IsEnabled = enabled;
                Widgets.Add(new WidgetToggleItem(widget.Id, widget.DisplayName, enabled, OnWidgetToggled));
            }
        }

        /// <summary>组件开关：写回配置并通知顶栏重建。</summary>
        private void OnWidgetToggled(string id, bool enabled)
        {
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
            if (value is null)
            {
                return;
            }

            _settings.Current.Appearance.Material = value.Kind;
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        partial void OnBarHeightChanged(double value)
        {
            _settings.Current.Appearance.BarHeight = Math.Clamp(value, 28, 40);
            _settings.Save();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }

        partial void OnSelectedThemeChanged(ThemeOption value)
        {
            if (value is null)
            {
                return;
            }

            _settings.Current.Appearance.Theme = value.Mode;
            _theme.Apply(value.Mode);
            _settings.Save();
        }

        partial void OnQweatherKeyChanged(string value)
        {
            _settings.Current.Api.QWeatherKey = value ?? string.Empty;
            _settings.Save();
        }

        partial void OnHitokotoEnabledChanged(bool value)
        {
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

        [ObservableProperty]
        private bool _isEnabled;

        public string Id { get; }
        public string DisplayName { get; }

        public WidgetToggleItem(string id, string displayName, bool isEnabled, Action<string, bool> onChanged)
        {
            Id = id;
            DisplayName = displayName;
            _isEnabled = isEnabled;
            _onChanged = onChanged;
        }

        partial void OnIsEnabledChanged(bool value) => _onChanged(Id, value);
    }
}
