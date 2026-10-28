using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Infrastructure.Interop;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Services;

namespace MyTopBar.Views
{
    /// <summary>
    /// 设置窗口。
    ///
    /// 和顶栏一样使用毛玻璃背景：这里不能设 AllowsTransparency="True"
    /// （那会让窗口变成分层窗口，DWM 模糊失效），
    /// 而是走 CompositionTarget.BackgroundColor + DwmExtendFrameIntoClientArea
    /// + SetWindowCompositionAttribute 这条路。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly ViewModels.SettingsViewModel _viewModel;
        private readonly GlassEffect.GlassRegistration _glass;

        /// <summary>任一设置项改动后触发，供顶栏即时应用。</summary>
        public event EventHandler? SettingsApplied;

        public SettingsWindow()
        {
            InitializeComponent();

            _viewModel = App.Current.Services.GetRequiredService<ViewModels.SettingsViewModel>();
            DataContext = _viewModel;
            _viewModel.SettingsApplied += (_, _) => SettingsApplied?.Invoke(this, EventArgs.Empty);

            // 一键注册毛玻璃：挂 SourceInitialized / Loaded / ThemeChanged，
            // 主题明暗切换时 DWM 边框/阴影颜色会自动跟随（此前这里缺失该订阅）。
            _glass = GlassEffect.Register(
                this, App.Current.Services.GetRequiredService<ThemeService>());

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        /// <summary>
        /// 关键：必须在这里再把 CompositionTarget 置一次透明。
        ///
        /// 在 SourceInitialized 阶段设置会被 WPF 的首次合成覆盖，
        /// 结果是整个窗口变成纯黑（实测背景采样为 RGB(0,0,0)）。
        /// 顶栏主窗口也踩过同样的坑，见 MainWindow.OnLoaded 的注释。
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            /*
             * 延后一帧再补一次透明。
             *
             * 直接在 Loaded 里设 CompositionTarget.BackgroundColor 无效——
             * 实测背景仍是纯黑 RGB(0,0,0)。原因是窗口刚完成首次合成，
             * 此时改动会被渲染批次忽略；必须等当前批处理结束后再设，
             * 并配合一次重绘，透明客户区才会真正生效。
             *
             * 顶栏主窗口没这个问题，是因为它的 OnLoaded 里还做了
             * Start() / HookSortGestures() 等大量工作，天然把材质应用推迟了。
             */
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                GlassEffect.SetTransparentClientArea(this);
                InvalidateVisual();

                /*
                 * 抢前台。
                 *
                 * 本窗口由顶栏菜单打开，而顶栏带 WS_EX_NOACTIVATE 从不激活，
                 * 本进程因此不被系统认作前台进程，Show() 内部的激活请求会被
                 * 前台锁定规则静默拒绝 —— 表现为窗口能显示但落在其他窗口后面。
                 * 这里在渲染完成后显式把窗口推到最前。
                 */
                BringToFront();
            }));
        }

        /// <summary>
        /// 把窗口带到前台。
        ///
        /// 直接 Window.Activate() 会被 Windows 前台锁定规则拒绝
        /// （本进程从不被认作前台进程），因此统一走 ForceForeground。
        /// 供 OnLoaded 与外部（菜单命令重复打开时）调用。
        /// </summary>
        public void BringToFront()
        {
            NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Focus();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Loaded -= OnLoaded;
            Closed -= OnClosed;
            _glass.Dispose();
        }

        /// <summary>
        /// 城市搜索结果被选中时填入经纬度。
        ///
        /// ListBox 的 SelectionChanged 无法直接绑定命令，这里转发给 ViewModel，
        /// 保持"选中即应用坐标"的交互在代码里只有一处实现。
        /// </summary>
        private void OnCitySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListBox list)
            {
                return;
            }

            if (list.SelectedItem is Widgets.GeoLocation location)
            {
                _viewModel.SelectCityCommand.Execute(location);
            }
        }
    }
}
