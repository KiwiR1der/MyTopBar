using System.Windows.Threading;

namespace MyTopBar.Services
{
    /// <summary>
    /// 全局 Tick 服务：统一调度各组件的刷新，减少 DispatcherTimer 数量。
    ///
    /// 设计：
    /// - 只有一个 DispatcherTimer 在跑（1 秒基准）
    /// - 按订阅者要求的间隔分发 Tick（每 250ms / 1s / 5s / 自定义）
    /// - 各 DataService 订阅自己需要的频率，不再自建 DispatcherTimer
    ///
    /// 收益：
    /// - 7 个组件原本最多 5 个 DispatcherTimer（时钟/歌词/天气/一言/系统指标）
    /// - 现在只有 1 个，UI 线程的定时器回调次数大幅减少
    /// - 组件启停时只需取消订阅，不会留下孤儿定时器
    /// </summary>
    public sealed class GlobalTickService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly object _gate = new();
        private readonly List<Subscription> _subscriptions = new();
        private bool _disposed;

        private sealed class Subscription
        {
            public required Action Callback { get; init; }
            public required TimeSpan Interval { get; init; }
            public DateTime LastTick { get; set; } = DateTime.MinValue;
        }

        public GlobalTickService(Dispatcher dispatcher)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(250) // 最细粒度，满足歌词刷新
            };
            _timer.Tick += OnTick;
        }

        /// <summary>
        /// 订阅 Tick。callback 会在 UI 线程执行。
        /// </summary>
        /// <param name="interval">期望的刷新间隔（实际按 250ms 粒度对齐）。</param>
        /// <param name="callback">回调函数。</param>
        /// <returns>取消订阅的句柄。</returns>
        public IDisposable Subscribe(TimeSpan interval, Action callback)
        {
            if (_disposed)
            {
                return NullSubscription.Instance;
            }

            var subscription = new Subscription
            {
                Callback = callback,
                Interval = interval
            };

            lock (_gate)
            {
                _subscriptions.Add(subscription);
            }

            EnsureTimerRunning();
            return new SubscriptionHandle(this, subscription);
        }

        private void EnsureTimerRunning()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            List<Subscription> due;

            lock (_gate)
            {
                due = _subscriptions
                    .Where(s => now - s.LastTick >= s.Interval)
                    .ToList();

                foreach (Subscription s in due)
                {
                    s.LastTick = now;
                }
            }

            foreach (Subscription s in due)
            {
                try
                {
                    s.Callback();
                }
                catch
                {
                    // 单个订阅者异常不影响其他订阅者
                }
            }

            // 没有订阅者时暂停定时器，省 CPU
            lock (_gate)
            {
                if (_subscriptions.Count == 0)
                {
                    _timer.Stop();
                }
            }
        }

        private void Unsubscribe(Subscription subscription)
        {
            lock (_gate)
            {
                _subscriptions.Remove(subscription);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;

            lock (_gate)
            {
                _subscriptions.Clear();
            }
        }

        private sealed class SubscriptionHandle : IDisposable
        {
            private readonly GlobalTickService _owner;
            private readonly Subscription _subscription;
            private bool _disposed;

            public SubscriptionHandle(GlobalTickService owner, Subscription subscription)
            {
                _owner = owner;
                _subscription = subscription;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner.Unsubscribe(_subscription);
            }
        }

        private sealed class NullSubscription : IDisposable
        {
            public static readonly NullSubscription Instance = new();
            public void Dispose() { }
        }
    }
}
