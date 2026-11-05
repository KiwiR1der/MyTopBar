using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.ViewModels;

namespace MyTopBar.Views
{
    /// <summary>
    /// 媒体控制浮窗。
    ///
    /// 与音量弹窗同理：Popup.StaysOpen=False 关闭时会把 Child 摘出视觉树触发 Unloaded，
    /// 再次打开又重新 Loaded，所以进度推进的订阅必须跟着 Loaded/Unloaded 成对进出。
    /// </summary>
    public partial class MediaControlPopupView : UserControl
    {
        private readonly MediaControlViewModel _viewModel;

        public MediaControlPopupView()
        {
            _viewModel = App.Current.Services.GetRequiredService<MediaControlViewModel>();
            DataContext = _viewModel;
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => _viewModel.Activate();

        private void OnUnloaded(object sender, RoutedEventArgs e) => _viewModel.Deactivate();

        /// <summary>
        /// 开始拖动滑块：通知 VM 暂停 Tick 回写，
        /// 否则每 250ms 一次的位置刷新会把滑块拽回播放位置。
        /// </summary>
        private void OnSeekStarted(object sender, DragStartedEventArgs e) => _viewModel.BeginSeek();

        /// <summary>
        /// 拖动结束：把最终位置下发给播放器。
        /// 不支持 SMTC 跳转的播放器会返回 false，VM 里会把滑块拉回真实位置。
        /// </summary>
        private async void OnSeekCompleted(object sender, DragCompletedEventArgs e)
        {
            await _viewModel.SeekAsync(ProgressSlider.Value);
        }
    }
}
