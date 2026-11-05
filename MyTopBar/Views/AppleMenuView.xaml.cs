using System.Windows.Controls;

namespace MyTopBar.Views
{
    /// <summary>
    /// Apple 菜单内容。数据上下文由 MainWindow 的 MainViewModel 提供，
    /// 这里不自行取服务，靠 PopupWindow 显示前继承。
    /// </summary>
    public partial class AppleMenuView : UserControl
    {
        public AppleMenuView()
        {
            InitializeComponent();
        }
    }
}
