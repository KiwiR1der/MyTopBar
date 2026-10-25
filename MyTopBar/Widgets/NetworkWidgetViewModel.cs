using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Services;

namespace MyTopBar.Widgets
{
    /// <summary>实时网络吞吐，显示下行/上行。</summary>
    public sealed partial class NetworkWidgetViewModel : WidgetViewModelBase
    {
        private readonly SystemMetricsService _metrics;

        [ObservableProperty]
        private string _text = "↓ -- ↑ --";

        public NetworkWidgetViewModel(SystemMetricsService metrics)
        {
            _metrics = metrics;
            _metrics.Updated += OnMetricsUpdated;
        }

        public override string Id => "network";

        public override string DisplayName => "网络";

        public override WidgetZone DefaultZone => WidgetZone.Right;

        public override double MinWidth => 104;

        private void OnMetricsUpdated(SystemMetricsSnapshot snapshot)
        {
            Text = $"↓{Format(snapshot.DownloadBytesPerSecond)} ↑{Format(snapshot.UploadBytesPerSecond)}";
        }

        internal static string Format(double bytesPerSecond)
        {
            const double k = 1024d;
            if (bytesPerSecond < k)
            {
                return $"{bytesPerSecond:0}B/s";
            }

            if (bytesPerSecond < k * k)
            {
                return $"{bytesPerSecond / k:0.0}K/s";
            }

            if (bytesPerSecond < k * k * k)
            {
                return $"{bytesPerSecond / k / k:0.0}M/s";
            }

            return $"{bytesPerSecond / k / k / k:0.0}G/s";
        }

        protected override void DisposeCore()
        {
            _metrics.Updated -= OnMetricsUpdated;
        }
    }
}
