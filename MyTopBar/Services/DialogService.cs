using System.Windows;
using MyTopBar.Infrastructure.Interfaces;

namespace MyTopBar.Services
{
    /// <summary>
    /// <see cref="IDialogService"/> 的 WPF 实现。
    ///
    /// 这是整个项目里唯一允许出现 MessageBox 的地方，其余层次一律通过接口调用，
    /// 便于替换为无 UI 的测试替身。
    /// </summary>
    public sealed class DialogService : IDialogService
    {
        private const string DefaultTitle = "MyTopBar";

        public bool Confirm(string message, string title)
        {
            /*
             * 顶栏窗口带 WS_EX_NOACTIVATE，从不持有焦点，
             * 直接用 MessageBox.Show(message, title, ...) 时无 owner，
             * 对话框可能出现在其他程序窗口后面（用户以为点击无效）。
             * 这里尝试附到当前活动窗口上，取不到再退回无 owner 的重载。
             */
            Window? owner = ResolveOwner();

            MessageBoxResult result = owner is null
                ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        public void ShowInfo(string message, string title)
            => Show(message, title, MessageBoxImage.Information);

        public void ShowWarning(string message, string title)
            => Show(message, title, MessageBoxImage.Warning);

        private static void Show(string message, string title, MessageBoxImage image)
        {
            Window? owner = ResolveOwner();
            string safeTitle = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title;

            if (owner is null)
            {
                MessageBox.Show(message, safeTitle, MessageBoxButton.OK, image);
            }
            else
            {
                MessageBox.Show(owner, message, safeTitle, MessageBoxButton.OK, image);
            }
        }

        /// <summary>
        /// 找一个合适的 owner 窗口：活动窗口 → 主窗口 → 任意可见窗口。
        /// 都没有（例如启动早期）时返回 null，由调用方退回无 owner 重载。
        /// </summary>
        private static Window? ResolveOwner()
        {
            if (Application.Current is not { } app)
            {
                return null;
            }

            Window? active = app.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive && w.IsVisible);

            if (active is not null)
            {
                return active;
            }

            if (app.MainWindow is { IsVisible: true } main)
            {
                return main;
            }

            return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);
        }
    }
}
