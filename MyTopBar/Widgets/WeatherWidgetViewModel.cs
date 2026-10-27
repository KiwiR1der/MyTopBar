using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Models.Settings;
using MyTopBar.Services;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 右区天气组件：纯展示，数据由 <see cref="WeatherDataService"/> 提供。
    /// </summary>
    public sealed partial class WeatherWidgetViewModel : WidgetViewModelBase
    {
        private readonly WeatherDataService _data;

        [ObservableProperty]
        private string _text = "天气 --";

        [ObservableProperty]
        private string _icon = "🌡️";

        public override string Id => "weather";

        public override string DisplayName => "天气";

        public override WidgetZone DefaultZone => WidgetZone.Right;

        public override double MinWidth => 72;

        public WeatherWidgetViewModel(WeatherDataService data)
        {
            _data = data;
            _data.DataChanged += OnDataChanged;
            _text = _data.CurrentText;
            _icon = _data.CurrentIcon;
        }

        public void Configure(QWeatherSettings settings)
        {
            _data.Configure(settings);
        }

        public void Start()
        {
            _data.Start();
        }

        private void OnDataChanged(string text, string icon)
        {
            Text = text;
            Icon = icon;
        }

        protected override void DisposeCore()
        {
            _data.DataChanged -= OnDataChanged;
            _data.Stop();
        }
    }
}
