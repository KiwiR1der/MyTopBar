using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.ViewModels;

namespace MyTopBar.Views
{
    public partial class WeatherPopupView : UserControl
    {
        private readonly WeatherPopupViewModel _viewModel;

        public WeatherPopupView()
        {
            _viewModel = App.Current.Services.GetRequiredService<WeatherPopupViewModel>();
            DataContext = _viewModel;
            InitializeComponent();

            /*
             * ViewModel 订阅了 WeatherDataService.DetailChanged，必须成对管理：
             *
             * Popup.StaysOpen=False 关闭时会把 Child 从视觉树摘掉 → Unloaded，
             * 再次打开又重新挂回 → Loaded。所以不能只在构造时订阅一次，
             * 否则弹窗关过一次之后数据就不再自动更新。
             *
             * 这里用 Loaded/Unloaded 成对进出：卸载时退订（不留悬挂引用），
             * 重新装入时再订阅并立刻刷新一次当前数据。
             */
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Activate();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Deactivate();
        }
    }
}
