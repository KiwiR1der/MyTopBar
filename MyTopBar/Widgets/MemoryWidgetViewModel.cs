using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Services;

namespace MyTopBar.Widgets
{
    /// <summary>内存占用，显示已用/总量。</summary>
    public sealed partial class MemoryWidgetViewModel : WidgetViewModelBase
    {
        private readonly SystemMetricsService _metrics;

        [ObservableProperty]
        private string _text = "--";

        /// <summary>矢量图标 key（见 GaugeIcons.xaml），与天气组件同一套机制。</summary>
        public string Icon => "memory";

        public MemoryWidgetViewModel(SystemMetricsService metrics)
        {
            _metrics = metrics;
            _metrics.Updated += OnMetricsUpdated;
        }

        public override string Id => "memory";

        public override string DisplayName => "内存";

        public override WidgetZone DefaultZone => WidgetZone.Right;

        private void OnMetricsUpdated(SystemMetricsSnapshot snapshot)
        {
            Text = $"{Format(snapshot.UsedMemoryBytes)}/{Format(snapshot.TotalMemoryBytes)}";
        }

        private static string Format(double bytes)
        {
            double gb = bytes / 1024d / 1024d / 1024d;
            return $"{gb:0.0}G";
        }

        protected override void DisposeCore()
        {
            _metrics.Updated -= OnMetricsUpdated;
        }
    }
}
