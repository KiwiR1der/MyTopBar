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

        /// <summary>同一轮显示内被重复调用时的去重窗口。</summary>
        private static readonly TimeSpan ActivateDedupWindow = TimeSpan.FromMilliseconds(300);

        private DateTime _lastActivateUtc;
        private bool _activated;

        [ObservableProperty]
        private string _placeName = string.Empty;

        [ObservableProperty]
        private string _conditionText = string.Empty;

        [ObservableProperty]
        private string _icon = "qingtian";

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
        /// 弹窗每次显示时调用：幂等重新订阅并刷新一次。
        ///
        /// 幂等是必须的。弹窗已是独立 Window（Views/PopupWindow.cs），
        /// <c>Open()</c> 里先 <c>Show()</c> 再 <c>Activate()</c>，
        /// 而 Activate 会走 SetWindowPos 重新激活，WPF 因此连发两次 Loaded。
        /// 之前这里无条件 Refresh()，导致每打开一次天气弹窗就走两遍数据 ——
        /// 天气来自按次计费的 API，重复请求是真金白银。
        /// </summary>
        public void Activate()
        {
            if (_disposed)
            {
                return;
            }

            Attach();

            // 同一轮显示里的重复 Loaded 只刷新一次。
            // 用户关掉再打开会间隔几百毫秒以上，不会被误吞。
            if (_activated && DateTime.UtcNow - _lastActivateUtc < ActivateDedupWindow)
            {
                return;
            }

            _activated = true;
            _lastActivateUtc = DateTime.UtcNow;
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
            _activated = false;
        }

        /// <summary>把数据源的当前快照同步到界面属性。</summary>
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
