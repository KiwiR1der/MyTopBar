namespace MyTopBar.Services.Media
{
    /// <summary>当前媒体会话的快照。</summary>
    public readonly record struct MediaTrackInfo(
        string Title,
        string Artist,
        string Album,
        bool IsPlaying,
        TimeSpan Position,
        TimeSpan Duration,
        string SourceAppId);
}
