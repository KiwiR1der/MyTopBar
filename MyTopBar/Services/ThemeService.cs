using Microsoft.Win32;
using MyTopBar.Models.Settings;
using System.Windows;
using System.Windows.Media;
namespace MyTopBar.Services
{
    /// <summary>
    /// 解析最终的明暗模式，并在系统主题变化时通知界面换色。
    /// </summary>
    public sealed class ThemeService : IDisposable
    {
        private ThemeMode _mode = ThemeMode.System;
        private bool _disposed;

        /// <summary>当前是否为深色。</summary>
        public bool IsDark { get; private set; }

        /// <summary>明暗模式生效时触发。</summary>
        public event EventHandler<bool>? ThemeChanged;
        public void Apply(ThemeMode mode)
        {
            ThrowIfDisposed();
            _mode = mode;
            bool isDark = ResolveIsDark(mode);
            bool changed = isDark != IsDark;
            IsDark = isDark;
            ApplyBrushes(isDark);
            if (changed)
            {
                ThemeChanged?.Invoke(this, isDark);
            }
        }

        /// <summary>把顶栏前景/分隔色按明暗切换，供 DynamicResource 自动刷新。</summary>
        public static void ApplyBrushes(bool isDark)
        {
            Application app = Application.Current;
            if (app is null)
            {
                return;
            }

            SetBrush(app.Resources, "BarForegroundBrush", isDark ? "#FFFFFFFF" : "#E6000000");
            SetBrush(app.Resources, "BarSecondaryForegroundBrush", isDark ? "#B3FFFFFF" : "#99000000");
            SetBrush(app.Resources, "BarSeparatorBrush", isDark ? "#33FFFFFF" : "#1F000000");
        }

        private static void SetBrush(ResourceDictionary resources, string key, string color)
        {
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        /// <summary>跟随系统时，重新读取注册表并广播变化。</summary>
        public void RefreshFromSystem()
        {
            if (_mode != ThemeMode.System || _disposed)
            {
                return;
            }

            bool isDark = ResolveIsDark(ThemeMode.System);
            if (isDark == IsDark)
            {
                return;
            }

            IsDark = isDark;
            ThemeChanged?.Invoke(this, isDark);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }


        private static bool ResolveIsDark(ThemeMode mode)
        {
            if (mode == ThemeMode.Dark)
            {
                return true;
            }

            if (mode == ThemeMode.Light)
            {
                return false;
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object? value = key?.GetValue("AppsUseLightTheme");
                return value is int i && i == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
