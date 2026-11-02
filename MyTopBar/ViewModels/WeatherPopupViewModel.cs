using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Services;
using System.Collections.ObjectModel;

namespace MyTopBar.ViewModels
{
    /// <summary>
    /// 天气弹窗 ViewModel：展示实时天气详情 + 未来预报。
    /// 数据全部来自 <see cref="WeatherDataService"/>，弹窗打开时手动刷新一次。
    /// </summary>
    public partial class WeatherPopupViewModel : ObservableObject, IDisposable
    {
        private readonly WeatherDataService _data;
        private bool _disposed;

        [ObservableProperty]
        private string _placeName = string.Empty;

        [ObservableProperty]
        private string _conditionText = string.Empty;

        [ObservableProperty]
        private string _icon = "🌡️";

        [ObservableProperty]
        private string _temperature = "--";

        [ObservableProperty]
        private string _highTemp = "--";

        [ObservableProperty]
        private string _lowTemp = "--";

        [ObservableProperty]
        private string _feelsLike = "--";

        [ObservableProperty]
        private string _humidity = "--";

        [ObservableProperty]
        private string _windInfo = "--";

        [ObservableProperty]
        private string _sunrise = "--";

        [ObservableProperty]
        private string _sunset = "--";

        public ObservableCollection<WeatherDataService.DailyForecast> Forecasts { get; } = [];

        public WeatherPopupViewModel(WeatherDataService data)
        {
            _data = data;
            _data.DetailChanged += OnDetailChanged;
            Refresh();
        }

        /// <summary>弹窗打开时调用，手动拉取最新数据。</summary>
        public void Refresh()
        {
            PlaceName = _data.PlaceName;
            ConditionText = _data.ConditionText;
            Icon = _data.CurrentIcon;
            Temperature = _data.Temperature;
            HighTemp = _data.HighTemp;
            LowTemp = _data.LowTemp;
            FeelsLike = _data.FeelsLike;
            Humidity = _data.Humidity;
            WindInfo = _data.WindInfo;
            Sunrise = _data.Sunrise;
            Sunset = _data.Sunset;

            Forecasts.Clear();
            foreach (WeatherDataService.DailyForecast f in _data.Forecasts)
            {
                Forecasts.Add(f);
            }
        }

        private void OnDetailChanged()
        {
            Refresh();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _data.DetailChanged -= OnDetailChanged;
            _disposed = true;
        }
    }
}
