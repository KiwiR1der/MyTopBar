using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.ViewModels;

namespace MyTopBar.Views
{
    public partial class CalendarPopup : UserControl
    {
        public CalendarPopup()
        {
            DataContext = App.Current.Services.GetRequiredService<CalendarPopupViewModel>();
            InitializeComponent();
        }
    }
}
