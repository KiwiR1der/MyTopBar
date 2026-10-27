using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using MyTopBar.Models.Settings;

namespace MyTopBar.Widgets
{
    /// <summary>天气请求的结果。Message 直接显示给用户，所以要写人话。</summary>
    public readonly record struct WeatherProbeResult(bool Ok, string Message);

    /// <summary>
    /// 天气接口的独立探测。
    ///
    /// 与 <see cref="WeatherWidgetViewModel"/> 的区别：那个是常驻自动刷新，
    /// 失败时静默保留旧值；这个是用户点「应用」时的一次性调用，
    /// 必须把原因说清楚，否则配错了完全不知道哪里出问题。
    /// </summary>
    public static class WeatherProbe
    {
        /// <summary>
        /// 请求一次实时天气，返回可读结果。
        /// 不抛异常——所有失败都转成 Message。
        /// </summary>
        public static async Task<WeatherProbeResult> ProbeAsync(QWeatherSettings settings)
        {
            string host = (settings.ApiHost ?? string.Empty).Trim().TrimEnd('/');
            string key = (settings.ApiKey ?? string.Empty).Trim();
            string lat = (settings.Latitude ?? string.Empty).Trim();
            string lon = (settings.Longitude ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(key))
            {
                return new WeatherProbeResult(false, "API Host 或 API Key 为空");
            }

            // Host 只接受域名形式。填成凭据 ID 是很容易犯的错，这里提前拦住。
            if (host.Contains('/') || host.Contains(' ') || !host.Contains('.'))
            {
                return new WeatherProbeResult(false,
                    $"API Host 格式不对：应为 xxx.qweatherapi.com，当前是「{host}」");
            }

            string url = $"https://{host}/weather/v1/current/{lat}/{lon}";
            if (!string.IsNullOrWhiteSpace(settings.Language))
            {
                url += "?lang=" + Uri.EscapeDataString(settings.Language.Trim());
            }

            try
            {
                using var handler = new HttpClientHandler
                {
                    AutomaticDecompression =
                        DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
                };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("X-QW-Api-Key", key);

                using HttpResponseMessage response = await http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return new WeatherProbeResult(false, DescribeHttpError(response.StatusCode));
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                string? display = ParseDisplay(json);
                return display is null
                    ? new WeatherProbeResult(false, "响应里没有天气数据，请检查经纬度是否在服务覆盖范围内")
                    : new WeatherProbeResult(true, $"成功：{display}");
            }
            catch (TaskCanceledException)
            {
                return new WeatherProbeResult(false, "请求超时，请检查网络");
            }
            catch (HttpRequestException ex)
            {
                return new WeatherProbeResult(false, $"无法连接：{ex.Message}");
            }
            catch (Exception ex)
            {
                return new WeatherProbeResult(false, $"请求失败：{ex.Message}");
            }
        }

        /// <summary>把 HTTP 状态码翻译成用户能看懂的原因。</summary>
        private static string DescribeHttpError(HttpStatusCode status) => status switch
        {
            HttpStatusCode.Unauthorized =>
                "401 认证失败：API Key 无效，或与 API Host 不属于同一账号",
            HttpStatusCode.Forbidden =>
                "403 拒绝访问：凭据可能被禁用，或该服务未订阅",
            HttpStatusCode.NotFound =>
                "404 找不到接口：API Host 可能填错，或路径不存在",
            HttpStatusCode.TooManyRequests =>
                "429 请求过于频繁：已达到套餐限额",
            HttpStatusCode.BadRequest =>
                "400 参数错误：请检查经纬度格式",
            _ => $"HTTP {(int)status} {status}"
        };

        /// <summary>解析出「天气 温度」文本，失败返回 null。</summary>
        private static string? ParseDisplay(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string text = string.Empty;
                if (root.TryGetProperty("condition", out JsonElement condition)
                    && condition.TryGetProperty("text", out JsonElement textEl))
                {
                    text = textEl.GetString() ?? string.Empty;
                }

                if (!root.TryGetProperty("temperature", out JsonElement temperature)
                    || !temperature.TryGetProperty("value", out JsonElement valueEl)
                    || !valueEl.TryGetDouble(out double value))
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return $"{text} {Math.Round(value).ToString(CultureInfo.InvariantCulture)}°";
            }
            catch
            {
                return null;
            }
        }
    }
}
