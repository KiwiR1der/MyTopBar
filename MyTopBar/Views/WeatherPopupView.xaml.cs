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
             * ViewModel 订阅了 WeatherDataService.DetailChanged，必须成对进出：
             * 卸载时退订（不留悬挂引用），重新装入时再订阅并立刻刷新一次当前数据。
             *
             * 注意：弹窗现在是独立 Window（Views/PopupWindow.cs），
             * 收起走 Hide() 不会触发 Unloaded，所以这里的 Unloaded 实际很少触发；
             * 真正的「打开」入口是 PopupWindow 每次 Show。因此 Activate() 必须幂等，
             * 见 WeatherPopupViewModel.Activate 的注释。
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
