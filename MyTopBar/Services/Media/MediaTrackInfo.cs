using System.Windows.Media.Imaging;

namespace MyTopBar.Services.Media
{
    /// <summary>当前媒体会话的快照。</summary>
    /// <param name="Artwork">专辑封面，无封面时为 null。</param>
    public readonly record struct MediaTrackInfo(
        string Title,
        string Artist,
        string Album,
        bool IsPlaying,
        TimeSpan Position,
        TimeSpan Duration,
        string SourceAppId,
        BitmapImage? Artwork = null);
}
