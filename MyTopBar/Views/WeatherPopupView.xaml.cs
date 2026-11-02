using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.ViewModels;

namespace MyTopBar.Views
{
    public partial class WeatherPopupView : UserControl
    {
        public WeatherPopupView()
        {
            DataContext = App.Current.Services.GetRequiredService<WeatherPopupViewModel>();
            InitializeComponent();
        }
    }
}
