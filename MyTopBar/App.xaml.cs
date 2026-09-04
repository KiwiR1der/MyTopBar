using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Interfaces;
using MyTopBar.Services;
using MyTopBar.ViewModels;
using System.Windows;
using System.Windows.Threading;

namespace MyTopBar
{
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current;

        public IServiceProvider Services { get; }

        public App()
        {
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton(Dispatcher.CurrentDispatcher);
            services.AddSingleton<IForegroundWindowService, ForegroundWindowService>();
            services.AddSingleton<PowerService>();
            services.AddSingleton<AudioService>();

            services.AddTransient<MainViewModel>();
            services.AddTransient<CalendarPopupViewModel>();
            services.AddTransient<AudioPopupViewModel>();

            return services.BuildServiceProvider();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }
    }
}
