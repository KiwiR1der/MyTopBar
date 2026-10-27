using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Infrastructure.Interop;
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

        /// <summary>任一设置项改动后触发，供顶栏即时应用。</summary>
        public event EventHandler? SettingsApplied;

        public SettingsWindow()
        {
            InitializeComponent();

            _viewModel = App.Current.Services.GetRequiredService<ViewModels.SettingsViewModel>();
            DataContext = _viewModel;
            _viewModel.SettingsApplied += (_, _) => SettingsApplied?.Invoke(this, EventArgs.Empty);

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        /// <summary>
        /// 句柄刚创建：只做一次材质设置，
        /// CompositionTarget 留到 Loaded 再置透明。
        /// </summary>
        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            ApplyGlass(new WindowInteropHelper(this).Handle);
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
             * 延后一帧再应用。
             *
             * 直接在这里设 CompositionTarget.BackgroundColor 无效——
             * 实测背景仍是纯黑 RGB(0,0,0)。原因是窗口刚完成首次合成，
             * 此时改动会被渲染批次忽略；必须等当前批处理结束后再设，
             * 并配合一次重绘，透明客户区才会真正生效。
             *
             * 顶栏主窗口没这个问题，是因为它的 OnLoaded 里还做了
             * Start() / HookSortGestures() 等大量工作，天然把材质应用推迟了。
             */
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ApplyGlass(new WindowInteropHelper(this).Handle);
            }));
        }

        private void ApplyGlass(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            /*
             * 设成 Transparent 让 WPF 不画不透明底。
             *
             * 但只设这个还不够：窗口此刻已经被 WPF 合成过一次（黑色），
             * 必须再强制重绘一次，合成器才会用新的透明底重画。
             * 用 Dispatcher 投递到 Loaded 之后执行，避开当前渲染批次。
             */
            if (HwndSource.FromHwnd(handle) is HwndSource source && source.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            NativeMethods.DwmExtendFrameIntoClientArea(handle, ref margins);

            // 清掉可能残留的 DWM 原生材质，避免盖住合成层。
            int none = NativeMethods.DWMSBT_NONE;
            NativeMethods.DwmSetWindowAttribute(
                handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));

            bool isDark = App.Current.Services.GetRequiredService<ThemeService>().IsDark;
            int dark = isDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(
                handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            if (!NativeMethods.ApplyBlurBehind(handle))
            {
                AppLog.Error("设置窗口：毛玻璃应用失败");
            }

            // 触发一次重绘，让透明客户区真正生效。
            InvalidateVisual();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            SourceInitialized -= OnSourceInitialized;
            Loaded -= OnLoaded;
            Closed -= OnClosed;
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
