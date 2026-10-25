using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Services;

namespace MyTopBar.Widgets
{
    /// <summary>CPU 使用率，数值来自共享的 <see cref="SystemMetricsService"/>。</summary>
    public sealed partial class CpuWidgetViewModel : WidgetViewModelBase
    {
        private readonly SystemMetricsService _metrics;

        [ObservableProperty]
        private string _text = "CPU --";

        public CpuWidgetViewModel(SystemMetricsService metrics)
        {
            _metrics = metrics;
            _metrics.Updated += OnMetricsUpdated;
        }

        public override string Id => "cpu";

        public override string DisplayName => "CPU";

        public override WidgetZone DefaultZone => WidgetZone.Right;

        public override double MinWidth => 72;

        private void OnMetricsUpdated(SystemMetricsSnapshot snapshot)
        {
            Text = $"CPU {snapshot.CpuPercent:0}%";
        }

        protected override void DisposeCore()
        {
            _metrics.Updated -= OnMetricsUpdated;
        }
    }
}
