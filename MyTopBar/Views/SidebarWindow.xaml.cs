using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Interop;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Services;

namespace MyTopBar.Views
{
    /// <summary>
    /// 屏幕左侧边缘滑出的侧边栏。
    ///
    /// 窗口策略：
    ///   - 常驻创建但不显示。首次滑出时 Show()，之后靠宽度动画开合，
    ///     避免每次都要新建窗口（重建会丢视觉树、闪一下）
    ///   - 窗口覆盖整个屏幕宽度，但只有右侧 300px 是可交互面板，
    ///     左侧是透明的「点击收起」区域。这样点面板外面任何地方都能收起，
    ///     不需要再挂一个全局鼠标钩子
    ///   - 与顶栏同样带 WS_EX_NOACTIVATE：滑出时不抢当前窗口焦点
    /// </summary>
    public partial class SidebarWindow : Window, System.ComponentModel.INotifyPropertyChanged
    {
        private const double PanelWidth = 300;
        private const double SlideDurationMs = 220;

        private readonly ClipboardHistoryService _clips;
        private readonly ThemeService _theme;

        private bool _isOpen;
        private bool _isAnimating;
        private bool _pinned;

        /// <summary>动画版本号，用于丢弃被打断的旧动画回调。</summary>
        private int _animationVersion;

        /// <summary>顶栏高度（DIP）。面板从它下沿开始，不覆盖顶栏。</summary>
        public double BarHeightDip { get; set; } = 30;

        /// <summary>面板开合状态变化，供 EdgeTriggerService 同步 HoldOpen。</summary>
        public event EventHandler<bool>? OpenStateChanged;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public SidebarWindow()
        {
            _clips = App.Current.Services.GetRequiredService<ClipboardHistoryService>();
            _theme = App.Current.Services.GetRequiredService<ThemeService>();

            DataContext = _clips;
            InitializeComponent();

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closed += OnClosed;

            // 列表增删时刷新空状态提示。
            _clips.Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasItems));
        }

        /// <summary>绑定给 XAML 的空状态判断。</summary>
        public bool HasItems => _clips.Items.Count > 0;

        /// <summary>是否钉住。钉住后不随光标离开收起。</summary>
        public bool IsPinned
        {
            get => _pinned;
            set
            {
                _pinned = value;
                OnPropertyChanged(nameof(IsPinned));
            }
        }

        public bool IsOpen => _isOpen;

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;

            if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            // 与顶栏一致：不抢焦点，也不出现在 Alt+Tab 里。
            NativeMethods.MakeNonActivating(handle);

            GlassEffect.Apply(handle, _theme.IsDark, roundCorners: false);
            RepositionToScreen();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            GlassEffect.SetTransparentClientArea(this);
            InvalidateVisual();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            SourceInitialized -= OnSourceInitialized;
            Loaded -= OnLoaded;
            Closed -= OnClosed;
        }

        /// <summary>
        /// 把窗口贴在光标所在屏幕的左边缘，宽度等于面板宽度。
        ///
        /// 早期版本让窗口铺满整个屏幕、左侧透明区负责「点外面收起」。
        /// 但一个铺满屏幕的置顶窗口会被 Shell 判定为全屏应用，
        /// 触发 ABN_FULLSCREENAPP 把顶栏藏起来——那是误判。
        /// 改成只有面板宽度后，全屏判定不再被触发。
        /// 「点外部收起」改由 EdgeTriggerService 依据光标离开面板区域处理。
        /// </summary>
        private void RepositionToScreen()
        {
            if (!NativeMethods.TryGetMonitorRectAtCursor(out NativeMethods.RECT monitor))
            {
                return;
            }

            double scale = 1.0;
            if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget is not null)
            {
                scale = source.CompositionTarget.TransformToDevice.M11;
                if (scale <= 0)
                {
                    scale = 1.0;
                }
            }

            /*
             * 贴着顶栏下沿开始，不覆盖顶栏。
             *
             * 顶栏是常驻 AppBar，覆盖它会遮住时钟/指标，而且两者同为 Topmost 时
             * Z 序会随点击变化——点顶栏后顶栏压住面板一段，点面板又反过来，
             * 视觉上反复跳动。让出顶栏高度后两者互不重叠，关系稳定。
             */
            double scaleDip = BarHeightDip;
            double top = monitor.Top / scale + scaleDip;

            Left = monitor.Left / scale;
            Top = top;
            Width = PanelWidth;
            Height = Math.Max(160, (monitor.Bottom - monitor.Top) / scale - scaleDip);
        }

        /// <summary>滑出面板。</summary>
        public void Open()
        {
            if (_isOpen || _isAnimating)
            {
                return;
            }

            if (!IsVisible)
            {
                Show();
            }

            /*
             * 定位必须放在 Show() 之后。
             *
             * RepositionToScreen 需要 TransformToDevice 拿 DPI 缩放，
             * 而窗口隐藏期间 PresentationSource 为 null，缩放会退回 1.0。
             * 在 125%/150% 缩放下定位就会整体偏移，表现为「面板没盖住顶栏」。
             * Show() 之后 PresentationSource 才有效。
             */
            RepositionToScreen();

            /*
             * 顶栏和侧边栏都是 Topmost，Z 序按「谁最后被置顶」决定。
             *
             * 顶栏带 WS_EX_NOACTIVATE，点它时 Shell 会把它提到置顶组最前，
             * 于是顶栏压住面板左段——表现为「面板没盖住顶栏，顶栏只显示右半截」。
             * 这里在每次展开时主动把面板重推到置顶组最前，保证覆盖关系稳定。
             */
            BringToTopmost();

            _isOpen = true;
            Animate(PanelWidth, 1.0);
            OpenStateChanged?.Invoke(this, true);
        }

        /// <summary>把窗口提到置顶组最前，且不激活。</summary>
        private void BringToTopmost()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        /// <summary>收起面板。</summary>
        public void Collapse()
        {
            if (!_isOpen)
            {
                return;
            }

            /*
             * 注意这里**不检查** _isAnimating。
             *
             * 展开动画有 220ms，用户完全可能在这段时间内就点了记录。
             * 早期版本写成 if (!_isOpen || _isAnimating) return，
             * 结果那些点击被静默丢弃，面板收不起来，只能点右上角关闭按钮。
             * Collapse 本来就该能打断展开动画——这是用户的明确意图。
             */
            _isOpen = false;
            Animate(0, 0);

            OpenStateChanged?.Invoke(this, false);
        }


        private void Animate(double targetWidth, double targetOpacity)
        {
            _isAnimating = true;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(SlideDurationMs);

            var widthAnimation = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
            var opacityAnimation = new DoubleAnimation(targetOpacity, duration) { EasingFunction = ease };

            /*
             * 动画版本号。
             *
             * 展开动画被打断（用户马上又收起）时，旧的 Completed 回调仍会触发。
             * 若不加版本校验，旧回调可能在新动画进行中执行 Hide()，
             * 表现为面板刚滑出来就消失、或该收的时候收不干净。
             */
            int version = ++_animationVersion;

            widthAnimation.Completed += (_, _) =>
            {
                if (version != _animationVersion)
                {
                    return;
                }

                _isAnimating = false;

                // 收起动画结束后隐藏窗口，避免透明置顶窗口一直挂着。
                if (!_isOpen)
                {
                    Hide();
                }
            };

            Panel.BeginAnimation(WidthProperty, widthAnimation);
            PanelContent.BeginAnimation(OpacityProperty, opacityAnimation);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Collapse();

        private void OnClearClick(object sender, RoutedEventArgs e) => _clips.Clear();

        /// <summary>点某条记录：写回剪贴板并收起面板。</summary>
        private void OnClipItemClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ClipItem item })
            {
                _clips.CopyBack(item);
                e.Handled = true;

                /*
                 * 点记录一律收起面板，不看 pin 状态。
                 *
                 * 用户的意图很明确：选了一条内容，接下来要去粘贴。
                 * 面板留着反而挡住目标窗口。pin 只用于「光标离开时不自动收」，
                 * 不该妨碍这一次显式的选择动作。
                 */
                Collapse();
            }
        }
    }
}
