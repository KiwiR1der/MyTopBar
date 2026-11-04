using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace MyTopBar.ViewModels
{
    /// <summary>CPU / 内存详情弹窗共用的 ViewModel，通过 Kind 区分数据来源。</summary>
    public partial class SystemDetailViewModel : ObservableObject, IDisposable
    {
        public enum DetailKind { Cpu, Memory }

        private readonly SystemMetricsService _metrics;
        private readonly Dispatcher _dispatcher;
        private readonly DetailKind _kind;
        private bool _disposed;
        private int _refreshToken;

        [ObservableProperty] private string _title = string.Empty;
        [ObservableProperty] private string _headline = string.Empty;
        [ObservableProperty] private string _subtitle = string.Empty;
        [ObservableProperty] private bool _isLoading;

        public ObservableCollection<ProcessUsage> Processes { get; } = [];

        public SystemDetailViewModel(
            SystemMetricsService metrics,
            Dispatcher dispatcher,
            DetailKind kind)
        {
            _metrics = metrics;
            _dispatcher = dispatcher;
            _kind = kind;
            _title = kind == DetailKind.Cpu ? "CPU" : "内存";

            _metrics.Updated += OnMetricsUpdated;
            OnMetricsUpdated(_metrics.Latest);
        }

        /// <summary>弹窗打开时调用：刷新聚合指标，并异步采集进程 Top5。</summary>
        public void Activate()
        {
            if (_disposed)
            {
                return;
            }

            // 幂等订阅，配合 Unloaded 退订。
            _metrics.Updated -= OnMetricsUpdated;
            _metrics.Updated += OnMetricsUpdated;
            OnMetricsUpdated(_metrics.Latest);
            _ = LoadProcessesAsync();
        }

        public void Deactivate()
        {
            _metrics.Updated -= OnMetricsUpdated;
        }

        private void OnMetricsUpdated(SystemMetricsSnapshot s)
        {
            if (_disposed)
            {
                return;
            }

            if (_kind == DetailKind.Cpu)
            {
                Headline = $"{s.CpuPercent:0.0}%";
                Subtitle = $"{Environment.ProcessorCount} 逻辑核心 · 实时占用";
            }
            else
            {
                double usedGb = s.UsedMemoryBytes / 1024.0 / 1024.0 / 1024.0;
                double totalGb = s.TotalMemoryBytes / 1024.0 / 1024.0 / 1024.0;
                double freeGb = Math.Max(0, totalGb - usedGb);
                double percent = totalGb > 0 ? usedGb / totalGb * 100.0 : 0;

                Headline = $"{percent:0.0}%";
                Subtitle = $"已用 {usedGb:0.0} GB / 共 {totalGb:0.0} GB · 可用 {freeGb:0.0} GB";
            }
        }

        private async Task LoadProcessesAsync()
        {
            int token = Interlocked.Increment(ref _refreshToken);
            IsLoading = true;
            try
            {
                List<ProcessUsage> list = _kind == DetailKind.Cpu
                    ? await SystemDetailService.GetTopCpuProcessesAsync(5).ConfigureAwait(true)
                    : await Task.Run(() => SystemDetailService.GetTopMemoryProcesses(5)).ConfigureAwait(true);

                // 弹窗可能已关闭或又重新打开，丢弃过期结果。
                if (_disposed || token != _refreshToken)
                {
                    return;
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || token != _refreshToken)
                    {
                        return;
                    }

                    Processes.Clear();
                    foreach (ProcessUsage p in list)
                    {
                        Processes.Add(p);
                    }
                });
            }
            catch
            {
                // 进程枚举失败时保留旧列表。
            }
            finally
            {
                if (!_disposed && token == _refreshToken)
                {
                    IsLoading = false;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Deactivate();
        }
    }
}
