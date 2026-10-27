using System.IO;
using System.Text;

namespace MyTopBar.Infrastructure.Common
{
    /// <summary>
    /// 极简落地日志：写 %AppData%\MyTopBar\logs\yyyy-MM-dd.log。
    /// 顶栏是常驻进程，出问题时用户没法看控制台，只能翻文件。
    /// 写日志本身不允许抛异常，否则会把"记录崩溃"变成"引发崩溃"。
    ///
    /// 日志轮转：只保留最近 7 天的日志文件，避免磁盘无限增长。
    /// </summary>
    public static class AppLog
    {
        private static readonly object Gate = new();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MyTopBar",
            "logs");

        /// <summary>日志保留天数。</summary>
        private const int RetentionDays = 7;

        public static void Info(string message) => Write("INFO", message, null);

        public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

        private static void Write(string level, string message, Exception? exception)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    CleanupOldLogs();

                    string path = Path.Combine(LogDirectory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");

                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                      .Append(" [").Append(level).Append("] ")
                      .Append(message)
                      .AppendLine();

                    if (exception is not null)
                    {
                        sb.AppendLine(exception.ToString());
                    }

                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 磁盘满、目录不可写等情况下静默放弃，绝不向上抛。
            }
        }

        /// <summary>删除超过保留天数的旧日志。</summary>
        private static void CleanupOldLogs()
        {
            try
            {
                string cutoff = DateTime.Now.AddDays(-RetentionDays).ToString("yyyy-MM-dd");
                foreach (string file in Directory.EnumerateFiles(LogDirectory, "*.log"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (string.CompareOrdinal(name, cutoff) < 0)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // 清理失败不影响写日志。
            }
        }
    }
}
