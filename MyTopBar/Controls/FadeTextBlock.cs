using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace MyTopBar.Controls
{
    /// <summary>
    /// 文本变化时淡入的 TextBlock。
    ///
    /// 用于歌词等频繁刷新的内容：250ms 定时器硬切文本会让中区显得"跳"，
    /// 这里在 Text 变化时做一次 150ms 的 0→1 透明度动画，观感平滑得多。
    ///
    /// 首次绑定（控件尚未 Loaded）不动画，避免启动时所有文本集体闪一下。
    /// </summary>
    public sealed class FadeTextBlock : TextBlock
    {
        static FadeTextBlock()
        {
            TextProperty.OverrideMetadata(
                typeof(FadeTextBlock),
                new FrameworkPropertyMetadata(string.Empty, OnTextChanged));
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var textBlock = (FadeTextBlock)d;

            // 尚未加载（首次绑定）时不做动画，直接显示。
            if (!textBlock.IsLoaded)
            {
                return;
            }

            string? oldText = e.OldValue as string;
            string? newText = e.NewValue as string;
            if (string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                return;
            }

            // 歌词行切换很快（最短 1-2 秒一行），动画要短，
            // 否则上一行动画没播完下一行又来了。
            var animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150))
            {
                // 动画结束后保持终值；BeginAnimation 会处理掉旧动画，不会堆积。
                FillBehavior = FillBehavior.HoldEnd
            };
            textBlock.BeginAnimation(OpacityProperty, animation);
        }
    }
}
