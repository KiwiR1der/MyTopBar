using System.IO;
using System.Security.Cryptography;
using System.Text;
using MyTopBar.Infrastructure.Common;

namespace MyTopBar.Services.Lyrics
{
    /// <summary>
    /// 歌词磁盘缓存。
    ///
    /// 路径：%AppData%\MyTopBar\lyrics\{key}.lrc
    /// key 是 "title\x01artist" 的 SHA256 十六进制，避免文件名非法字符。
    ///
    /// 缓存不设 TTL：LRC 文件本身不会变，磁盘占用极小（每首歌几 KB），
    /// 只在缓存超过 500 个文件时删除最旧的一半，防止无限增长。
    /// </summary>
    internal static class LyricsCache
    {
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MyTopBar",
            "lyrics");

        private const int MaxFiles = 500;

        /// <summary>
        /// 读取缓存。返回 null 表示未命中。
        /// </summary>
        public static LyricsCacheEntry? TryRead(string title, string artist)
        {
            try
            {
                string path = CachePath(title, artist);
                if (!File.Exists(path))
                {
                    return null;
                }

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 2)
                {
                    return null;
                }

                // 第一行是 "synced=true/false"，剩余行是 LRC 正文
                bool isSynced = string.Equals(lines[0], "synced=true", StringComparison.OrdinalIgnoreCase);
                string lrc = string.Join('\n', lines[1..]);

                // 刷新访问时间，让 LRU 清理能区分冷热文件
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow);

                return new LyricsCacheEntry(lrc, isSynced);
            }
            catch (Exception ex)
            {
                AppLog.Error("读取歌词缓存失败", ex);
                return null;
            }
        }

        /// <summary>
        /// 写入缓存。写入失败静默忽略，不影响正常播放。
        /// </summary>
        public static void Write(string title, string artist, string lrc, bool isSynced)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                TrimIfNeeded();

                string path = CachePath(title, artist);
                // 第一行写元数据，之后写 LRC 正文
                var sb = new StringBuilder();
                sb.AppendLine(isSynced ? "synced=true" : "synced=false");
                sb.Append(lrc);

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLog.Error("写入歌词缓存失败", ex);
            }
        }

        private static string CachePath(string title, string artist)
        {
            string key = title + "\x01" + artist;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            string hex = Convert.ToHexString(hash).ToLowerInvariant();
            return Path.Combine(CacheDirectory, hex + ".lrc");
        }

        /// <summary>
        /// 超过 500 个缓存文件时，按最后访问时间删除最旧的一半。
        /// </summary>
        private static void TrimIfNeeded()
        {
            try
            {
                string[] files = Directory.GetFiles(CacheDirectory, "*.lrc");
                if (files.Length < MaxFiles)
                {
                    return;
                }

                // 按最后访问时间升序（最旧在前）
                IEnumerable<string> toDelete = files
                    .OrderBy(f => File.GetLastAccessTimeUtc(f))
                    .Take(files.Length / 2);

                foreach (string f in toDelete)
                {
                    File.Delete(f);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("清理歌词缓存失败", ex);
            }
        }
    }

    internal readonly record struct LyricsCacheEntry(string Lrc, bool IsSynced);
}
