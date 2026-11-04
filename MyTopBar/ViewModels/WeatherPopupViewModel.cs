using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        [ObservableProperty]
        private bool _isRefreshing;

        [ObservableProperty]
        private string _refreshStatus = string.Empty;

        public ObservableCollection<WeatherDataService.DailyForecast> Forecasts { get; } = [];

        public WeatherPopupViewModel(WeatherDataService data)
        {
            _data = data;
            Attach();
            // 首次构造即填一次数据：某些宿主下 Loaded 可能延迟或不触发，
            // 避免弹窗短暂显示全 "--"。
            Refresh();
        }

        /// <summary>
        /// 重新订阅数据源并刷新一次。
        ///
        /// Popup 的 StaysOpen=False 会在关闭时把 Child 从视觉树摘掉并触发 Unloaded，
        /// 再次打开又会 Loaded。若在 Unloaded 里只做退订而不提供重新订阅的入口，
        /// 第二次打开弹窗后数据就不会再自动更新。所以这里做成可重复调用。
        /// </summary>
        public void Activate()
        {
            if (_disposed)
            {
                return;
            }

            Attach();
            Refresh();
        }

        private void Attach()
        {
            _data.DetailChanged -= OnDetailChanged;
            _data.DetailChanged += OnDetailChanged;
            _data.RefreshCompleted -= OnRefreshCompleted;
            _data.RefreshCompleted += OnRefreshCompleted;
        }

        [RelayCommand]
        private void RefreshWeather()
        {
            if (IsRefreshing || _disposed)
            {
                return;
            }

            IsRefreshing = true;
            RefreshStatus = "正在刷新…";
            _data.RefreshNow();
        }

        private void OnRefreshCompleted(bool success)
        {
            IsRefreshing = false;
            RefreshStatus = success ? "已更新" : "刷新失败，稍后重试";
            if (success)
            {
                Refresh();
            }
        }

        /// <summary>弹窗卸载时退订，避免数据源继续持有已不在屏幕上的 ViewModel。</summary>
        public void Deactivate()
        {
            _data.DetailChanged -= OnDetailChanged;
            _data.RefreshCompleted -= OnRefreshCompleted;
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
            Deactivate();
            _disposed = true;
        }
    }
}
