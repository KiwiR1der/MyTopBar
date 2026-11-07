namespace MyTopBar.Infrastructure.Interfaces
{
    /// <summary>
    /// 用户交互服务：确认框与轻提示。
    ///
    /// 存在意义：ViewModel 里直接 MessageBox.Show 会让本该可测的层依赖 WPF 的
    /// 模态窗口（单元测试会真的弹窗并阻塞）。这里把「问用户」抽象成接口，
    /// VM 只依赖接口，实现由 View 层提供。
    /// </summary>
    public interface IDialogService
    {
        /// <summary>询问是否继续。用户点「是」返回 true。</summary>
        /// <param name="message">正文。</param>
        /// <param name="title">标题。</param>
        bool Confirm(string message, string title);

        /// <summary>提示一条信息（不需要用户决策）。</summary>
        void ShowInfo(string message, string title);

        /// <summary>提示一条警告。</summary>
        void ShowWarning(string message, string title);
    }
}
