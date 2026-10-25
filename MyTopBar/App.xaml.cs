using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyTopBar.Infrastructure.Interfaces;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Services;
using MyTopBar.Services.Media;
using MyTopBar.ViewModels;
using MyTopBar.Widgets;
using System.Net.Http;
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
            services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });

            services.AddSingleton<SettingsService>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<IForegroundWindowService, ForegroundWindowService>();
            services.AddSingleton<PowerService>();
            services.AddSingleton<AudioService>();
            services.AddSingleton<SystemMetricsService>();
            services.AddSingleton<MediaSessionService>();
            services.AddSingleton<WidgetRegistry>();

            services.AddSingleton<ClockWidgetViewModel>();
            services.AddSingleton<LyricsWidgetViewModel>();
            services.AddSingleton<WeatherWidgetViewModel>();
            services.AddSingleton<TextWidgetViewModel>();
            services.AddSingleton<CpuWidgetViewModel>();
            services.AddSingleton<MemoryWidgetViewModel>();
            services.AddSingleton<NetworkWidgetViewModel>();

            services.AddTransient<SettingsViewModel>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<CalendarPopupViewModel>();
            services.AddTransient<AudioPopupViewModel>();

            return services.BuildServiceProvider();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (Services.GetService<SettingsService>() is { } settings)
            {
                settings.Flush();
            }

            if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }
    }
}
