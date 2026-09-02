using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTopBar.Infrastructure.Interfaces;
using MyTopBar.Models;
using MyTopBar.Services;
using System.Windows;
using System.Windows.Threading;

namespace MyTopBar.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IForegroundWindowService _foregroundWindowService;
        private readonly PowerService _powerService;

        private bool _disposed;

        private readonly DispatcherTimer _clockTimer = new DispatcherTimer();

        [ObservableProperty]
        private string _currentAppName = "...";

        [ObservableProperty]
        private string _currentWindowTitle = string.Empty;

        [ObservableProperty]
        private string _welcomeMessage = "Welcome to MyTopBar!";

        [ObservableProperty]
        private string _clockText = string.Empty;

        public MainViewModel(
            IForegroundWindowService foregroundWindowService,
            PowerService powerService)
        {
            _foregroundWindowService = foregroundWindowService;
            _powerService = powerService;
            _foregroundWindowService.OnForegroundAppInfoChanged += OnCurrentChanged;

            _clockTimer.Tick += OnClockTick;
            RefreshClock();
            _clockTimer.Start();
        }

        [RelayCommand]
        private void AboutThisPc()
        {
            _powerService.OpenSettings("ms-settings:about");
        }

        [RelayCommand]
        private void OpenSettings()
        {
            _powerService.OpenSettings("ms-settings:");
        }

        [RelayCommand]
        private void Shutdown()
        {
            MessageBoxResult result = MessageBox.Show("确定要关闭电脑吗？", "确认关机", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _powerService.Shutdown();
            }
        }

        [RelayCommand]
        private void Sleep()
        {
            MessageBoxResult result = MessageBox.Show("确定要让电脑进入睡眠吗？", "确认睡眠", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _powerService.Sleep();
            }
        }

        [RelayCommand]
        private void Restart()
        {
            MessageBoxResult result = MessageBox.Show("确定要重新启动电脑吗？", "确认重启", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _powerService.Restart();
            }
        }

        [RelayCommand]
        private void Exit()
        {
            MessageBoxResult result = MessageBox.Show("确定要退出 MyTopBar 吗？", "确认退出", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                App.Current.Shutdown();
            }
        }

        public void Start()
        {
            _foregroundWindowService.Start();
            UpdateFrom(
                _foregroundWindowService.Current);
        }

        private void OnCurrentChanged(
            object? sender,
            ForegroundAppInfo? app)
        {
            UpdateFrom(app);
        }

        private void UpdateFrom(ForegroundAppInfo? app)
        {
            CurrentAppName = string.IsNullOrWhiteSpace(app?.DisplayName)
                ? "..."
                : app.DisplayName;
            CurrentWindowTitle = app?.WindowTitle ?? string.Empty;
        }

        private void OnClockTick(object? sender, EventArgs e)
        {
            RefreshClock();
        }

        private void RefreshClock()
        {
            DateTime now = DateTime.Now;
            ClockText = now.ToString("M月d日 hh:mm");

            DateTime nextMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1);
            TimeSpan due = nextMinute - now;
            if (due < TimeSpan.FromMilliseconds(200))
            {
                due = TimeSpan.FromMinutes(1);
            }

            _clockTimer.Interval = due;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _clockTimer.Stop();

            _foregroundWindowService.OnForegroundAppInfoChanged -= OnCurrentChanged;
            _foregroundWindowService.Stop();
            _disposed = true;
        }
    }
}
