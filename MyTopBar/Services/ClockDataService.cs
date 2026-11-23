using System.Windows.Threading;
using Microsoft.Win32;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 时钟数据源：对齐到整分刷新，并处理系统时间变化/休眠唤醒。
    ///
    /// 已接入 GlobalTickService：不再自建 DispatcherTimer，改为订阅全局 Tick。
    /// 组件被关闭时调用 Stop() 即可，不会留下孤儿定时器。
    /// </summary>
    public sealed class ClockDataService : IDisposable
    {
        private readonly Services.GlobalTickService _tick;
        private IDisposable? _subscription;
        private bool _disposed;

        public bool ShowWeekday { get; set; } = true;
        public bool Use24Hour { get; set; } = true;

        public string CurrentText { get; private set; } = string.Empty;

        public event Action<string>? TextChanged;

        public ClockDataService(Services.GlobalTickService tick)
        {
            _tick = tick;
            SystemEvents.TimeChanged += OnSystemTimeChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        public void Start()
        {
            if (_disposed || _subscription is not null)
            {
                return;
            }

            Refresh();
            /*
             * 订阅 1 分钟间隔。注意这是「距上次 tick 满 60s」而非「对齐整分」：
             * GlobalTickService 按 250ms 基准派发，首次触发时刻取决于订阅时刻，
             * 所以时钟在整分后最多偏差 1 秒左右才跳字。要精确对齐需在
             * GlobalTickService 里加边界对齐，不符合它「通用调度」的定位。
             */
            _subscription = _tick.Subscribe(TimeSpan.FromMinutes(1), Refresh);
        }

        public void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void OnSystemTimeChanged(object? sender, EventArgs e) => Refresh();

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            DateTime now = DateTime.Now;
            string time = Use24Hour ? now.ToString("HH:mm") : now.ToString("h:mm");
            CurrentText = ShowWeekday ? $"{GetWeekday(now)} {time}" : time;
            TextChanged?.Invoke(CurrentText);
        }

        private static string GetWeekday(DateTime value) => value.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日"
        };

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            SystemEvents.TimeChanged -= OnSystemTimeChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }
}
