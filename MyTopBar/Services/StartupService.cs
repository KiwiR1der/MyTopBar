using System.Reflection;
using Microsoft.Win32;

namespace MyTopBar.Services
{
    /// <summary>
    /// 开机自启：写当前用户的 Run 注册表键（HKCU\...\Run），无需管理员权限。
    ///
    /// 状态以注册表为唯一真实来源，不写进 settings.json：
    /// 用户可能在任务管理器「启动」页里禁用/启用，那只改注册表，不改我们的配置。
    /// 每次都读注册表，界面显示的一定是系统实际状态，两边不会脱节。
    /// </summary>
    public static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "MyTopBar";

        /// <summary>当前是否已注册开机自启。</summary>
        public static bool IsEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(AppName) is string path && !string.IsNullOrWhiteSpace(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>开启或关闭开机自启。返回操作后的实际状态（以注册表为准）。</summary>
        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key is null)
                {
                    return false;
                }

                if (enabled)
                {
                    string exe = GetExePath();
                    if (string.IsNullOrWhiteSpace(exe))
                    {
                        return false;
                    }
                    // 加引号，路径含空格也能正常启动。
                    key.SetValue(AppName, $"\"{exe}\"");
                }
                else
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }

                return IsEnabled();
            }
            catch
            {
                return IsEnabled();
            }
        }

        /// <summary>当前进程的 exe 绝对路径（非 dll）。</summary>
        private static string GetExePath()
        {
            // ProcessPath 指向真实的 exe（PublishSingleFile / 普通 exe 都是它）。
            string? p = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }

            // 兜底：程序集位置（通常是 dll，仅在没有 exe 时使用）。
            return Assembly.GetExecutingAssembly().Location;
        }
    }
}
