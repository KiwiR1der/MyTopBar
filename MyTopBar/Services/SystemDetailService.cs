using System.Diagnostics;

namespace MyTopBar.Services
{
    /// <summary>单个进程的资源占用条目。</summary>
    public sealed record ProcessUsage(string Name, double Value, string Display);

    /// <summary>
    /// 按需采集的 CPU / 内存详情。
    ///
    /// 不挂到全局 Tick 上：枚举进程 + 计算 CPU 差值比较重，
    /// 只在弹窗打开时调用一次，关掉弹窗就没有任何开销。
    /// </summary>
    public static class SystemDetailService
    {
        /// <summary>
        /// 采集进程 CPU 占用 Top N。用前后两次 TotalProcessorTime 的差值除以墙钟时间。
        /// 异步执行，避免 Thread.Sleep 阻塞 UI 线程。
        /// </summary>
        public static async Task<List<ProcessUsage>> GetTopCpuProcessesAsync(int count = 5, int windowMs = 400)
        {
            var first = new Dictionary<int, (string Name, TimeSpan Cpu)>();
            foreach (Process p in SafeEnumerate())
            {
                try
                {
                    first[p.Id] = (SafeName(p), p.TotalProcessorTime);
                }
                catch
                {
                    // 进程退出 / 拒绝访问，跳过。
                }
            }

            await Task.Delay(windowMs).ConfigureAwait(false);

            var result = new List<ProcessUsage>();
            int cores = Environment.ProcessorCount;
            foreach (Process p in SafeEnumerate())
            {
                try
                {
                    if (!first.TryGetValue(p.Id, out var prev))
                    {
                        continue;
                    }

                    double deltaSeconds = (p.TotalProcessorTime - prev.Cpu).TotalSeconds;
                    if (deltaSeconds <= 0)
                    {
                        continue;
                    }

                    // delta / wallTime 得到「占用了几核」，换算成百分比。
                    double percent = deltaSeconds / (windowMs / 1000.0) * 100.0 / Math.Max(1, cores);
                    result.Add(new ProcessUsage(prev.Name, percent, $"{percent:0.0}%"));
                }
                catch
                {
                    // 同上，进程可能已退出。
                }
            }

            return result
                .OrderByDescending(x => x.Value)
                .Take(count)
                .ToList();
        }

        /// <summary>采集进程内存占用 Top N（工作集），单位 MB。</summary>
        public static List<ProcessUsage> GetTopMemoryProcesses(int count = 5)
        {
            var result = new List<ProcessUsage>();
            foreach (Process p in SafeEnumerate())
            {
                try
                {
                    double mb = p.WorkingSet64 / 1024.0 / 1024.0;
                    if (mb < 1)
                    {
                        continue;
                    }
                    result.Add(new ProcessUsage(SafeName(p), mb, FormatMb(mb)));
                }
                catch
                {
                    // 进程退出 / 拒绝访问，跳过。
                }
            }

            return result
                .OrderByDescending(x => x.Value)
                .Take(count)
                .ToList();
        }

        public static string FormatMb(double mb) =>
            mb >= 1024 ? $"{mb / 1024.0:0.00} GB" : $"{mb:0} MB";

        private static IEnumerable<Process> SafeEnumerate()
        {
            Process[] all;
            try
            {
                all = Process.GetProcesses();
            }
            catch
            {
                return [];
            }

            // 逐个 Dispose 交给调用方的 foreach 处理不了，这里改用数组并让调用方 try/catch。
            return all;
        }

        private static string SafeName(Process p)
        {
            try
            {
                string name = p.ProcessName;
                return string.IsNullOrWhiteSpace(name) ? $"PID {p.Id}" : name;
            }
            catch
            {
                return $"PID {p.Id}";
            }
        }
    }
}
