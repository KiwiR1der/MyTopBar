using System.IO;

namespace MyTopBar.Infrastructure.Common
{
    /// <summary>
    /// 程序数据的根目录：exe 所在目录（便携式布局）。
    ///
    /// 为什么集中在这里定义：settings.json、logs\、clips\、lyrics\ 原先各自
    /// 拼一遍 %AppData%\MyTopBar\，路径一旦要搬家就得改好几处，很容易漏掉
    /// 其中一个（例如日志还写在旧位置，排查时翻错目录）。
    /// 现在只有一个来源，改动点唯一。
    ///
    /// 用 AppContext.BaseDirectory 而不是 Assembly.Location / Directory.GetCurrentDirectory()：
    /// 前者在单文件发布、以及通过快捷方式启动时都稳定指向 exe 目录；
    /// GetCurrentDirectory() 是进程工作目录，从资源管理器双击和从别处调用结果不同，
    /// 会导致配置和日志在用户不知情时分落到两个地方。
    ///
    /// 代价：程序目录必须可写。放在 Program Files 下会写不进去，
    /// 这是便携式布局的固有取舍，谁写失败谁负责静默降级（配置回默认、日志丢弃）。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>程序所在目录，所有数据的根。</summary>
        public static string BaseDirectory { get; } = Normalize(AppContext.BaseDirectory);

        /// <summary>配置文件完整路径。</summary>
        public static string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

        /// <summary>日志目录。</summary>
        public static string LogDirectory => Path.Combine(BaseDirectory, "logs");

        /// <summary>剪贴板图片目录。</summary>
        public static string ClipDirectory => Path.Combine(BaseDirectory, "clips");

        /// <summary>歌词缓存目录。</summary>
        public static string LyricsDirectory => Path.Combine(BaseDirectory, "lyrics");

        /// <summary>
        /// 去掉末尾分隔符。AppContext.BaseDirectory 结尾带 '\'，
        /// 直接 Path.Combine 虽然没问题，但拼出来的字符串会被用户看到
        /// （设置窗口展示日志目录），多一个反斜杠不好看。
        /// </summary>
        private static string Normalize(string directory)
        {
            try
            {
                return Path.TrimEndingDirectorySeparator(directory);
            }
            catch (Exception)
            {
                return directory;
            }
        }
    }
}
