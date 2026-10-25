using System.Globalization;
using System.Text;

namespace MyTopBar.Services.Lyrics
{
    /// <summary>一行带时间轴歌词。</summary>
    public readonly record struct LyricLine(TimeSpan Time, string Text);

    /// <summary>
    /// 解析标准 LRC 文本。支持一行多时间戳、[offset:] 校正。
    /// 解析失败返回空列表，不抛异常。
    /// </summary>
    public static class LrcParser
    {
        public static IReadOnlyList<LyricLine> Parse(string? lrc)
        {
            var result = new List<LyricLine>();
            if (string.IsNullOrWhiteSpace(lrc))
            {
                return result;
            }

            int offsetMs = 0;
            string[] lines = lrc.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line[8..].TrimEnd(']');
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        offsetMs = parsed;
                    }
                    continue;
                }

                if (!TryReadTimeTags(line, out List<TimeSpan> times, out string text) || times.Count == 0)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                foreach (TimeSpan t in times)
                {
                    TimeSpan adjusted = t - TimeSpan.FromMilliseconds(offsetMs);
                    if (adjusted < TimeSpan.Zero)
                    {
                        adjusted = TimeSpan.Zero;
                    }

                    result.Add(new LyricLine(adjusted, text));
                }
            }

            result.Sort(static (a, b) => a.Time.CompareTo(b.Time));
            return result;
        }

        private static bool TryReadTimeTags(string line, out List<TimeSpan> times, out string text)
        {
            times = new List<TimeSpan>();
            text = string.Empty;
            int index = 0;

            while (index < line.Length && line[index] == '[')
            {
                int close = line.IndexOf(']', index);
                if (close < 0)
                {
                    break;
                }

                ReadOnlySpan<char> inner = line.AsSpan(index + 1, close - index - 1);
                if (!TryParseTag(inner, out TimeSpan time))
                {
                    break;
                }

                times.Add(time);
                index = close + 1;
            }

            if (times.Count == 0)
            {
                return false;
            }

            text = line[index..].Trim();
            return true;
        }

        /// <summary>解析 mm:ss.xx / mm:ss:xx / hh:mm:ss.xxx。</summary>
        private static bool TryParseTag(ReadOnlySpan<char> inner, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            int colon = inner.LastIndexOf(':');
            if (colon <= 0 || colon == inner.Length - 1)
            {
                return false;
            }

            ReadOnlySpan<char> minutesPart = inner[..colon];
            ReadOnlySpan<char> secondsPart = inner[(colon + 1)..];

            if (minutesPart.IndexOf(':') >= 0)
            {
                return false;
            }

            int fractionSeparator = secondsPart.IndexOfAny('.', ':');
            ReadOnlySpan<char> secPart = fractionSeparator >= 0 ? secondsPart[..fractionSeparator] : secondsPart;
            ReadOnlySpan<char> fracPart = fractionSeparator >= 0 ? secondsPart[(fractionSeparator + 1)..] : default;

            if (!int.TryParse(minutesPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) || minutes < 0)
            {
                return false;
            }

            if (!int.TryParse(secPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) || seconds < 0 || seconds > 59)
            {
                return false;
            }

            int milliseconds = 0;
            if (fracPart.Length > 0)
            {
                if (!int.TryParse(fracPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fraction) || fraction < 0)
                {
                    return false;
                }

                milliseconds = fracPart.Length switch
                {
                    1 => fraction * 100,
                    2 => fraction * 10,
                    3 => fraction,
                    _ => (int)Math.Round(fraction / Math.Pow(10, fracPart.Length - 3))
                };
            }

            time = new TimeSpan(0, 0, minutes, seconds, milliseconds);
            return true;
        }
    }
}