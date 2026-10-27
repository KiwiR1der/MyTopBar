using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;
using MyTopBar.Infrastructure.Interop;

namespace MyTopBar.Services
{
    /// <summary>一次采样的系统指标快照。</summary>
    public readonly record struct SystemMetricsSnapshot(
        double CpuPercent,
        double UsedMemoryBytes,
        double TotalMemoryBytes,
        double DownloadBytesPerSecond,
        double UploadBytesPerSecond);
    /// <summary>
    /// 单一定时器同时采样 CPU / 内存 / 网络，供右区组件共享。
    /// 已接入 GlobalTickService：不再自建 DispatcherTimer，改为订阅全局 Tick。
    /// </summary>
    public sealed class SystemMetricsService : IDisposable
    {
        private readonly GlobalTickService _tick;
        private IDisposable? _subscription;
        private readonly object _sync = new();

        private ulong _prevIdle;
        private ulong _prevKernel;
        private ulong _prevUser;
        private bool _hasCpuBaseline;

        private long _prevReceived;
        private long _prevSent;
        private bool _hasNetworkBaseline;
        private DateTime _prevSampleTime = DateTime.UtcNow;

        private bool _disposed;

        /// <summary>采样间隔，单位毫秒。</summary>
        public int IntervalMs
        {
            get => _intervalMs;
            set
            {
                int clamped = Math.Clamp(value, 200, 10000);
                if (_intervalMs == clamped)
                {
                    return;
                }

                _intervalMs = clamped;

                // 已在运行才重订阅；启动前的设置只改字段，
                // 让 Start() 先建立基线再订阅，避免首帧出现 0%/100% 跳变。
                if (_subscription is not null)
                {
                    Resubscribe();
                }
            }
        }

        private int _intervalMs = 1000;

        /// <summary>每次采样完成时触发，已在 UI 线程。</summary>
        public event Action<SystemMetricsSnapshot>? Updated;

        public SystemMetricsSnapshot Latest { get; private set; }

        public SystemMetricsService(GlobalTickService tick)
        {
            _tick = tick;
        }

        public void Start()
        {
            if (_disposed || _subscription is not null)
            {
                return;
            }

            // 建立基线，避免首帧出现 100% 之类的跳变。
            Sample(emit: false);
            Resubscribe();
        }

        public void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Resubscribe()
        {
            _subscription?.Dispose();
            _subscription = _tick.Subscribe(
                TimeSpan.FromMilliseconds(_intervalMs),
                () => Sample(emit: true));
        }

        private void Sample(bool emit)
        {
            if (_disposed)
            {
                return;
            }

            lock (_sync)
            {
                DateTime now = DateTime.UtcNow;
                double elapsed = Math.Max(0.001, (now - _prevSampleTime).TotalSeconds);
                _prevSampleTime = now;

                double cpu = ReadCpuPercent();
                (double usedMemory, double totalMemory) = ReadMemory();
                (double down, double up) = ReadNetwork(elapsed);

                Latest = new SystemMetricsSnapshot(cpu, usedMemory, totalMemory, down, up);
            }

            if (emit)
            {
                Updated?.Invoke(Latest);
            }
        }

        private double ReadCpuPercent()
        {
            if (!NativeMethods.GetSystemTimes(out NativeMethods.FILETIME idle, out NativeMethods.FILETIME kernel, out NativeMethods.FILETIME user))
            {
                return 0;
            }

            ulong idleValue = idle.Value;
            ulong kernelValue = kernel.Value;
            ulong userValue = user.Value;

            if (!_hasCpuBaseline)
            {
                _prevIdle = idleValue;
                _prevKernel = kernelValue;
                _prevUser = userValue;
                _hasCpuBaseline = true;
                return 0;
            }

            ulong idleDelta = idleValue - _prevIdle;
            ulong kernelDelta = kernelValue - _prevKernel;
            ulong userDelta = userValue - _prevUser;

            _prevIdle = idleValue;
            _prevKernel = kernelValue;
            _prevUser = userValue;

            // kernel 时间已经包含 idle，必须先减去再求占用率。
            ulong totalDelta = kernelDelta + userDelta;
            if (totalDelta == 0)
            {
                return 0;
            }

            double busy = (double)(totalDelta - Math.Min(idleDelta, totalDelta));
            double percent = busy / totalDelta * 100.0;
            return Math.Clamp(percent, 0, 100);
        }

        private static (double Used, double Total) ReadMemory()
        {
            var status = new NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
            };

            if (!NativeMethods.GlobalMemoryStatusEx(ref status))
            {
                return (0, 0);
            }

            double total = status.ullTotalPhys;
            double used = total - status.ullAvailPhys;
            return (used, total);
        }

        private (double Down, double Up) ReadNetwork(double elapsedSeconds)
        {
            long received = 0;
            long sent = 0;

            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!IsCountable(nic))
                    {
                        continue;
                    }

                    IPv4InterfaceStatistics stats = nic.GetIPv4Statistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
            }
            catch
            {
                return (0, 0);
            }

            if (!_hasNetworkBaseline)
            {
                _prevReceived = received;
                _prevSent = sent;
                _hasNetworkBaseline = true;
                return (0, 0);
            }

            long downDelta = Math.Max(0, received - _prevReceived);
            long upDelta = Math.Max(0, sent - _prevSent);
            _prevReceived = received;
            _prevSent = sent;

            return (downDelta / elapsedSeconds, upDelta / elapsedSeconds);
        }

        private static bool IsCountable(NetworkInterface nic)
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                return false;
            }

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                return false;
            }

            try
            {
                return nic.Supports(NetworkInterfaceComponent.IPv4);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }
    }
}
