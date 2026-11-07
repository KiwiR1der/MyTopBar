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
            var stopwatch = Stopwatch.StartNew();

            using (ProcessCollection processes = ProcessCollection.Capture())
            {
                foreach (Process p in processes.Items)
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
            }

            await Task.Delay(windowMs).ConfigureAwait(false);
            stopwatch.Stop();

            /*
             * 换算用实测墙钟而不是名义 windowMs：
             * Task.Delay 的精度误差（±15ms 级）会直接带进百分比，
             * 核数少时相对误差更明显。
             */
            double wallSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, windowMs / 1000.0 * 0.5);

            var result = new List<ProcessUsage>();
            int cores = Environment.ProcessorCount;

            using (ProcessCollection processes = ProcessCollection.Capture())
            {
                foreach (Process p in processes.Items)
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
                        double percent = deltaSeconds / wallSeconds * 100.0 / Math.Max(1, cores);
                        result.Add(new ProcessUsage(prev.Name, percent, $"{percent:0.0}%"));
                    }
                    catch
                    {
                        // 同上，进程可能已退出。
                    }
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
            using ProcessCollection processes = ProcessCollection.Capture();

            foreach (Process p in processes.Items)
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

        /// <summary>
        /// Process.GetProcesses() 的结果，负责统一释放句柄。
        ///
        /// 必需：每次枚举会拿到 200~400 个 Process 对象，每个都持有一个内核进程句柄。
        /// 这里早期只 try/catch 而不 Dispose，注释误以为 catch 能回收，
        /// 结果是每打开一次 CPU / 内存详情弹窗泄漏 600+ 个句柄，
        /// 长期运行会累积到句柄耗尽。
        ///
        /// 用 using 而不是让调用方 finally：调用方只需 foreach，忘记释放就会泄漏。
        /// </summary>
        private sealed class ProcessCollection : IDisposable
        {
            private ProcessCollection(Process[] items) => Items = items;

            public Process[] Items { get; }

            public static ProcessCollection Capture() => new(Process.GetProcesses());

            /// <summary>
            /// 逐个释放。原始 GetProcesses() 本身就可能抛（权限/资源不足），
            /// 那种情况下由调用方保留旧列表，这里不做补偿。
            /// </summary>
            public void Dispose()
            {
                foreach (Process p in Items)
                {
                    try
                    {
                        p.Dispose();
                    }
                    catch
                    {
                        // 单个句柄释放失败不影响其余。
                    }
                }
            }
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
