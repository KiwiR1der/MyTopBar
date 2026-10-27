using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MyTopBar.Widgets
{
    /// <summary>GeoAPI 返回的一个候选地点。</summary>
    public sealed record GeoLocation(
        string Name,
        string Id,
        string Latitude,
        string Longitude,
        string Admin1,
        string Admin2,
        string Country)
    {
        /// <summary>界面上显示的一行文字，带行政区划便于区分重名城市。</summary>
        public string Display
        {
            get
            {
                // 直辖市（北京/上海/天津/重庆）的 adm1 与 adm2 往往相同或高度重复，
                // 直接用会显示成「杭州 · 浙江省 杭州」这种啰嗦形式。
                bool sameRegion = string.Equals(Admin1, Admin2, StringComparison.Ordinal)
                    || Admin2.StartsWith(Admin1.Length > 0 ? Admin1 : "\u0000", StringComparison.Ordinal)
                    || Admin1.StartsWith(Admin2.Length > 0 ? Admin2 : "\u0000", StringComparison.Ordinal);

                string region = sameRegion
                    ? (string.IsNullOrWhiteSpace(Admin1) ? Admin2 : Admin1)
                    : string.Join(" ", new[] { Admin1, Admin2 }.Where(s => !string.IsNullOrWhiteSpace(s)));

                return string.IsNullOrWhiteSpace(region)
                    ? $"{Name}（{Country}）"
                    : $"{Name} · {region}";
            }
        }

        public override string ToString() => Display;
    }

    /// <summary>搜索结果：候选列表或一条可读的错误说明。</summary>
    public readonly record struct GeoLookupResult(
        IReadOnlyList<GeoLocation> Locations,
        string Error)
    {
        public bool Ok => Error.Length == 0;

        public static GeoLookupResult Fail(string error) => new(Array.Empty<GeoLocation>(), error);
    }

    /// <summary>
    /// GeoAPI 城市搜索。
    ///
    ///   GET https://{ApiHost}/geo/v2/city/lookup?location={关键字}&amp;number=10&amp;lang=zh
    ///
    /// 用于把「城市名」换成经纬度，避免让用户手填坐标。
    /// 与 WeatherProbe 一样，失败时返回可读原因而不是抛异常。
    /// </summary>
    public static class GeoLookup
    {
        /// <summary>查询城市，返回最多 10 个候选。</summary>
        public static async Task<GeoLookupResult> SearchAsync(
            string apiHost,
            string apiKey,
            string keyword,
            string language = "zh")
        {
            string host = (apiHost ?? string.Empty).Trim().TrimEnd('/');
            string key = (apiKey ?? string.Empty).Trim();
            string query = (keyword ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                return GeoLookupResult.Fail("请输入城市名称");
            }

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(key))
            {
                return GeoLookupResult.Fail("请先填写 API Host 和 API Key");
            }

            if (host.Contains('/') || host.Contains(' ') || !host.Contains('.'))
            {
                return GeoLookupResult.Fail($"API Host 格式不对：应为 xxx.qweatherapi.com，当前是「{host}」");
            }

            string url = $"https://{host}/geo/v2/city/lookup"
                + "?location=" + Uri.EscapeDataString(query)
                + "&number=10"
                + (string.IsNullOrWhiteSpace(language) ? string.Empty : "&lang=" + Uri.EscapeDataString(language.Trim()));

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
                    return GeoLookupResult.Fail(DescribeHttpError(response.StatusCode));
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return Parse(json);
            }
            catch (TaskCanceledException)
            {
                return GeoLookupResult.Fail("查询超时，请检查网络");
            }
            catch (HttpRequestException ex)
            {
                return GeoLookupResult.Fail($"无法连接：{ex.Message}");
            }
            catch (Exception ex)
            {
                return GeoLookupResult.Fail($"查询失败：{ex.Message}");
            }
        }

        private static GeoLookupResult Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string code = root.TryGetProperty("code", out JsonElement codeEl)
                    ? codeEl.GetString() ?? string.Empty
                    : string.Empty;

                // 文档：查无结果时 code 为 404，location 字段缺失。
                if (code == "404")
                {
                    return GeoLookupResult.Fail("没有找到这个地点，换个名称试试");
                }

                if (code != "200")
                {
                    return GeoLookupResult.Fail($"接口返回 code={code}，请检查 Key 与 API Host");
                }

                if (!root.TryGetProperty("location", out JsonElement list)
                    || list.ValueKind != JsonValueKind.Array)
                {
                    return GeoLookupResult.Fail("没有找到这个地点，换个名称试试");
                }

                var results = new List<GeoLocation>();
                foreach (JsonElement item in list.EnumerateArray())
                {
                    string name = Str(item, "name");
                    string lat = Str(item, "lat");
                    string lon = Str(item, "lon");

                    // 没有坐标的结果对天气查询没用，直接跳过。
                    if (string.IsNullOrWhiteSpace(name)
                        || string.IsNullOrWhiteSpace(lat)
                        || string.IsNullOrWhiteSpace(lon))
                    {
                        continue;
                    }

                    results.Add(new GeoLocation(
                        name,
                        Str(item, "id"),
                        lat,
                        lon,
                        Str(item, "adm1"),
                        Str(item, "adm2"),
                        Str(item, "country")));
                }

                return results.Count == 0
                    ? GeoLookupResult.Fail("没有找到这个地点，换个名称试试")
                    : new GeoLookupResult(results, string.Empty);
            }
            catch (Exception ex)
            {
                return GeoLookupResult.Fail($"响应解析失败：{ex.Message}");
            }
        }

        private static string Str(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        private static string DescribeHttpError(HttpStatusCode status) => status switch
        {
            HttpStatusCode.Unauthorized => "401 认证失败：API Key 无效，或与 API Host 不属于同一账号",
            HttpStatusCode.Forbidden => "403 拒绝访问：凭据可能被禁用，或 GeoAPI 未订阅",
            HttpStatusCode.NotFound => "404 找不到接口：API Host 可能填错",
            HttpStatusCode.TooManyRequests => "429 请求过于频繁：已达到套餐限额",
            _ => $"HTTP {(int)status} {status}"
        };

        /// <summary>
        /// 把经纬度格式化成天气接口要求的形式（最多两位小数）。
        /// GeoAPI 返回的是 "39.90499" 这种五位小数，直接用会被接口拒绝。
        /// </summary>
        public static string FormatCoordinate(string value)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                return Math.Round(d, 2).ToString("0.00", CultureInfo.InvariantCulture);
            }

            return value;
        }
    }
}
