using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 中区时钟。对齐到整分刷新，避免每秒唤醒 UI 线程。
    /// </summary>
    public sealed partial class ClockWidgetViewModel : WidgetViewModelBase
    {
        private readonly DispatcherTimer _timer = new();

        [ObservableProperty]
        private string _clockText = string.Empty;

        public override string Id => "clock";

        public override string DisplayName => "时钟";

        public override WidgetZone DefaultZone => WidgetZone.Center;

        public override double MinWidth => 60;

        /// <summary>是否显示星期。</summary>
        public bool ShowWeekday { get; set; } = true;

        /// <summary>是否使用 24 小时制。</summary>
        public bool Use24Hour { get; set; } = true;

        public ClockWidgetViewModel()
        {
            _timer.Tick += OnTick;
            Refresh();
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e) => Refresh();

        private void Refresh()
        {
            DateTime now = DateTime.Now;
            string time = Use24Hour ? now.ToString("HH:mm") : now.ToString("h:mm");
            ClockText = ShowWeekday ? $"{GetWeekday(now)} {time}" : time;

            // 对齐到下一整分：只睡到分界点，而不是每秒轮询。
            DateTime nextMinute = new(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            nextMinute = nextMinute.AddMinutes(1);
            TimeSpan due = nextMinute - now;
            _timer.Interval = due < TimeSpan.FromMilliseconds(200) ? TimeSpan.FromMinutes(1) : due;
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

        protected override void DisposeCore()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
