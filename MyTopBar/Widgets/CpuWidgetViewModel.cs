using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Services;

namespace MyTopBar.Widgets
{
    /// <summary>CPU 使用率，数值来自共享的 <see cref="SystemMetricsService"/>。</summary>
    public sealed partial class CpuWidgetViewModel : WidgetViewModelBase
    {
        private readonly SystemMetricsService _metrics;

        [ObservableProperty]
        private string _text = "--";

        /// <summary>矢量图标 key（见 GaugeIcons.xaml），与天气组件同一套机制。</summary>
        public string Icon => "cpu";

        public CpuWidgetViewModel(SystemMetricsService metrics)
        {
            _metrics = metrics;
            _metrics.Updated += OnMetricsUpdated;
        }

        public override string Id => "cpu";

        public override string DisplayName => "CPU";

        public override WidgetZone DefaultZone => WidgetZone.Right;

        private void OnMetricsUpdated(SystemMetricsSnapshot snapshot)
        {
            Text = $"{snapshot.CpuPercent:0}%";
        }

        protected override void DisposeCore()
        {
            _metrics.Updated -= OnMetricsUpdated;
        }
    }
}
