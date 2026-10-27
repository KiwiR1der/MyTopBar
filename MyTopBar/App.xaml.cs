using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Infrastructure.Interfaces;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Services;
using MyTopBar.Services.Media;
using MyTopBar.ViewModels;
using MyTopBar.Widgets;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MyTopBar
{
    public partial class App : Application
    {
        /// <summary>
        /// 单实例互斥体。顶栏会注册为系统 AppBar 并占用工作区，
        /// 起两份会互相挤压、工作区被重复扣减，所以必须只允许一个实例。
        /// 用 Global\ 前缀，跨会话（多用户切换）也能互斥。
        /// </summary>
        private static Mutex? _singleInstanceMutex;

        public new static App Current => (App)Application.Current;

        public IServiceProvider Services { get; }

        public App()
        {
            Services = ConfigureServices();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 必须在任何窗口创建之前判断。
            _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\MyTopBar.SingleInstance", out bool createdNew);

            if (!createdNew)
            {
                AppLog.Info("检测到已有实例，本次启动退出。");
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;

                // 已有实例继续运行，本进程不创建任何窗口直接退出。
                Shutdown();
                return;
            }

            RegisterGlobalExceptionHandlers();
            AppLog.Info($"MyTopBar 启动 (pid={Environment.ProcessId})");

            base.OnStartup(e);
        }

        /// <summary>
        /// 全局兜底。顶栏是常驻进程，任何一个未处理异常都会让整条栏消失，
        /// 且用户没有控制台可看，所以这里既记日志也尽量不让进程死掉。
        /// </summary>
        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += (_, args) =>
            {
                AppLog.Error("UI 线程未处理异常", args.Exception);

                // 标记已处理，避免顶栏因为一次偶发异常（例如遍历视觉树时的竞争）直接退出。
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                AppLog.Error("未观察的任务异常", args.Exception);
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                AppLog.Error("应用域未处理异常（进程即将终止）", args.ExceptionObject as Exception);
            };
        }

        /// <summary>
        /// 创建共享的 HttpClient。
        ///
        /// AutomaticDecompression 是关键：请求时自动带 Accept-Encoding，
        /// 响应时自动解压 Gzip / Deflate / Brotli，业务代码拿到的就是明文 JSON。
        /// </summary>
        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton(Dispatcher.CurrentDispatcher);

            /*
             * 必须开启自动解压。
             *
             * 和风天气对所有响应都做了 Gzip 压缩（响应头 Content-Encoding: gzip），
             * 官方文档「构建 API 请求 - Gzip」一节明确要求客户端自行解压。
             * 不开这个开关时，ReadAsStringAsync 拿到的是压缩字节，
             * 按 UTF-8 解码得到乱码，JsonDocument.Parse 直接失败。
             */
            services.AddSingleton(CreateHttpClient());

            services.AddSingleton<SettingsService>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<IForegroundWindowService, ForegroundWindowService>();
            services.AddSingleton<PowerService>();
            services.AddSingleton<AudioService>();
            services.AddSingleton<GlobalTickService>();
            services.AddSingleton<SystemMetricsService>();
            services.AddSingleton<MediaSessionService>();
            services.AddSingleton<WidgetRegistry>();

            // 组件数据源（Step A：渲染与数据解耦）
            services.AddSingleton<ClockDataService>();
            services.AddSingleton<WeatherDataService>();
            services.AddSingleton<TextDataService>();

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

            if (_singleInstanceMutex is not null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch
                {
                    // 未持有互斥体（例如启动时已被判定为第二实例）时忽略。
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            AppLog.Info("MyTopBar 退出");
            base.OnExit(e);
        }
    }
}
