using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace CastMPV.Core
{
    public sealed class SubtitleSearchResult
    {
        public string Id { get; set; }
        public long FileId { get; set; }
        public string Title { get; set; }
        public string FileName { get; set; }
        public string Language { get; set; }
        public string Format { get; set; }
        public int Downloads { get; set; }
        public string DirectDownloadUrl { get; set; }
        public string Source { get; set; } // "OpenSubtitles v1" or "OpenSubtitles Legacy"
    }

    /// <summary>
    /// Zero-dependency OpenSubtitles search and download client supporting
    /// the official OpenSubtitles REST v1 API (with user Api-Key) and legacy fallback.
    /// </summary>
    public sealed class OpenSubtitlesClient
    {
        private const string OfficialBaseUrl = "https://api.opensubtitles.com/api/v1";
        private const string LegacyBaseUrl = "https://rest.opensubtitles.org/search";
        private const string AppUserAgent = "CastMPV v1.0";

        static OpenSubtitlesClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }
        }

        public async Task<List<SubtitleSearchResult>> SearchAsync(
            string query,
            string language,
            string apiKey,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SubtitleSearchResult>();

            string cleanQuery = query.Trim();
            string cleanLang = NormalizeLanguageCode(language);

            // 1. Official OpenSubtitles REST API v1 when an API key is provided
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                return await SearchOfficialV1Async(cleanQuery, cleanLang, apiKey.Trim(), ct).ConfigureAwait(false);
            }

            // 2. Public legacy fallback when no API key is configured yet
            try
            {
                List<SubtitleSearchResult> legacy = await SearchLegacyAsync(cleanQuery, cleanLang, ct).ConfigureAwait(false);
                if (legacy.Count > 0) return legacy;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "OpenSubtitles public search is currently unavailable without an API key (" + ex.Message + "). " +
                    "Please enter a free OpenSubtitles.com API key from opensubtitles.com/consumers.", ex);
            }

            throw new InvalidOperationException(
                "No results returned from legacy OpenSubtitles endpoint. For full results, enter a free API key from opensubtitles.com/consumers.");
        }

        public async Task<string> DownloadSubtitleAsync(
            SubtitleSearchResult item,
            string apiKey,
            CancellationToken ct = default)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            if (item.Source == "OpenSubtitles v1")
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("An OpenSubtitles.com API key is required to download this subtitle.");

                string payload = $"{{\"file_id\":{item.FileId}}}";
                byte[] respBytes = await SendHttpAsync(
                    "POST",
                    OfficialBaseUrl + "/download",
                    apiKey.Trim(),
                    payload,
                    ct).ConfigureAwait(false);

                XDocument doc = ParseJsonToXml(respBytes);
                string link = doc.Descendants("link").FirstOrDefault()?.Value;
                if (string.IsNullOrWhiteSpace(link))
                {
                    string msg = doc.Descendants("message").FirstOrDefault()?.Value ?? "Download link missing from API response.";
                    throw new InvalidOperationException(msg);
                }

                string preferredName = string.IsNullOrWhiteSpace(item.FileName) ? item.Title : item.FileName;
                return await SubtitleCacheManager.DownloadSubtitleToCacheAsync(link, preferredName, AppUserAgent, null, ct).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(item.DirectDownloadUrl))
                throw new InvalidOperationException("Subtitle download URL is empty.");

            string nameHint = string.IsNullOrWhiteSpace(item.FileName) ? item.Title : item.FileName;
            return await SubtitleCacheManager.DownloadSubtitleToCacheAsync(
                item.DirectDownloadUrl,
                nameHint,
                "TemporaryUserAgent",
                null,
                ct).ConfigureAwait(false);
        }

        private async Task<List<SubtitleSearchResult>> SearchOfficialV1Async(
            string query,
            string language,
            string apiKey,
            CancellationToken ct)
        {
            var url = new StringBuilder(OfficialBaseUrl);
            url.Append("/subtitles?query=").Append(Uri.EscapeDataString(query));

            // Automatically detect S01E02 pattern for sharper TV episode matching
            Match epMatch = Regex.Match(query, @"\bS(\d{1,2})E(\d{1,2})\b", RegexOptions.IgnoreCase);
            if (epMatch.Success)
            {
                if (int.TryParse(epMatch.Groups[1].Value, out int season))
                    url.Append("&season_number=").Append(season);
                if (int.TryParse(epMatch.Groups[2].Value, out int episode))
                    url.Append("&episode_number=").Append(episode);
            }

            if (!string.IsNullOrWhiteSpace(language) && language != "all")
                url.Append("&languages=").Append(Uri.EscapeDataString(language));

            byte[] bytes = await SendHttpAsync("GET", url.ToString(), apiKey, null, ct).ConfigureAwait(false);
            XDocument doc = ParseJsonToXml(bytes);

            var results = new List<SubtitleSearchResult>();
            foreach (XElement item in doc.Descendants("data").Elements("item"))
            {
                string id = item.Element("id")?.Value ?? Guid.NewGuid().ToString("N");
                XElement attrs = item.Element("attributes");
                if (attrs == null) continue;

                XElement firstFile = attrs.Element("files")?.Elements("item").FirstOrDefault();
                if (firstFile == null) continue;

                if (!long.TryParse(firstFile.Element("file_id")?.Value, out long fileId) || fileId <= 0)
                    continue;

                string fileName = firstFile.Element("file_name")?.Value ?? string.Empty;
                string release = attrs.Element("release")?.Value;
                string featureTitle = attrs.Element("feature_details")?.Element("title")?.Value;
                string displayTitle = !string.IsNullOrWhiteSpace(release)
                    ? release
                    : (!string.IsNullOrWhiteSpace(featureTitle) ? featureTitle : fileName);

                string lang = attrs.Element("language")?.Value ?? language;
                int.TryParse(attrs.Element("download_count")?.Value, out int downloads);
                string ext = Path.GetExtension(fileName)?.TrimStart('.').ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(ext)) ext = "SRT";

                results.Add(new SubtitleSearchResult
                {
                    Id = id,
                    FileId = fileId,
                    Title = displayTitle ?? "Subtitle",
                    FileName = string.IsNullOrWhiteSpace(fileName) ? (displayTitle + ".srt") : fileName,
                    Language = lang ?? "en",
                    Format = ext,
                    Downloads = downloads,
                    Source = "OpenSubtitles v1"
                });
            }

            return results.OrderByDescending(r => r.Downloads).ToList();
        }

        private async Task<List<SubtitleSearchResult>> SearchLegacyAsync(
            string query,
            string language,
            CancellationToken ct)
        {
            string url = $"{LegacyBaseUrl}/query-{Uri.EscapeDataString(query)}";
            if (!string.IsNullOrWhiteSpace(language) && language != "all")
            {
                string iso3 = ToIso639_2B(language);
                if (!string.IsNullOrWhiteSpace(iso3))
                    url += $"/sublanguageid-{iso3}";
            }

            byte[] bytes = await SendHttpAsync("GET", url, null, null, ct, "TemporaryUserAgent").ConfigureAwait(false);
            XDocument doc = ParseJsonToXml(bytes);

            var results = new List<SubtitleSearchResult>();
            foreach (XElement item in doc.Root?.Elements("item") ?? Enumerable.Empty<XElement>())
            {
                string dl = item.Element("SubDownloadLink")?.Value;
                if (string.IsNullOrWhiteSpace(dl)) continue;

                string fileName = item.Element("SubFileName")?.Value ?? "subtitle.srt";
                string release = item.Element("MovieReleaseName")?.Value;
                string movieName = item.Element("MovieName")?.Value;
                string title = !string.IsNullOrWhiteSpace(release) ? release : (!string.IsNullOrWhiteSpace(movieName) ? movieName : fileName);
                string lang = item.Element("ISO639")?.Value ?? item.Element("SubLanguageID")?.Value ?? "en";
                string format = (item.Element("SubFormat")?.Value ?? "srt").ToUpperInvariant();
                int.TryParse(item.Element("SubDownloadsCnt")?.Value, out int downloads);

                // Prefer direct uncompressed URL when SubDownloadLink is gzipped (.gz)
                string cleanUrl = dl.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? dl.Substring(0, dl.Length - 3)
                    : dl;

                results.Add(new SubtitleSearchResult
                {
                    Id = item.Element("IDSubtitleFile")?.Value ?? Guid.NewGuid().ToString("N"),
                    Title = title,
                    FileName = fileName,
                    Language = lang,
                    Format = format,
                    Downloads = downloads,
                    DirectDownloadUrl = cleanUrl,
                    Source = "OpenSubtitles Legacy"
                });
            }

            return results.OrderByDescending(r => r.Downloads).ToList();
        }

        private static async Task<byte[]> SendHttpAsync(
            string method,
            string url,
            string apiKey,
            string jsonBody,
            CancellationToken ct,
            string customUserAgent = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("Invalid OpenSubtitles URL.");
            }

            var req = (HttpWebRequest)WebRequest.Create(parsed);
            req.Method = method;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.Accept = "application/json";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.UserAgent = string.IsNullOrWhiteSpace(customUserAgent) ? AppUserAgent : customUserAgent;

            if (!string.IsNullOrWhiteSpace(apiKey))
                req.Headers["Api-Key"] = apiKey;

            if (!string.IsNullOrEmpty(jsonBody))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                req.ContentType = "application/json";
                req.ContentLength = bodyBytes.Length;
                using Stream reqStream = await req.GetRequestStreamAsync().ConfigureAwait(false);
                await reqStream.WriteAsync(bodyBytes, 0, bodyBytes.Length, ct).ConfigureAwait(false);
            }

            using (ct.Register(() => { try { req.Abort(); } catch { } }))
            {
                try
                {
                    using var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false);
                    using Stream stream = resp.GetResponseStream();
                    using var ms = new MemoryStream();
                    if (stream != null)
                        await stream.CopyToAsync(ms).ConfigureAwait(false);
                    return ms.ToArray();
                }
                catch (WebException wex) when (wex.Response is HttpWebResponse errResp)
                {
                    string errBody = string.Empty;
                    try
                    {
                        using Stream es = errResp.GetResponseStream();
                        if (es != null)
                        {
                            using var sr = new StreamReader(es, Encoding.UTF8);
                            errBody = sr.ReadToEnd();
                        }
                    }
                    catch { }

                    throw new InvalidOperationException(
                        $"HTTP {(int)errResp.StatusCode} ({errResp.StatusDescription}): {ExtractErrorMessage(errBody)}");
                }
            }
        }

        private static string ExtractErrorMessage(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return "Request rejected.";
            try
            {
                XDocument doc = ParseJsonToXml(Encoding.UTF8.GetBytes(rawJson));
                string msg = doc.Descendants("message").FirstOrDefault()?.Value
                    ?? doc.Descendants("error").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(msg)) return msg;
            }
            catch { }
            return rawJson.Length > 180 ? rawJson.Substring(0, 180) : rawJson;
        }

        private static XDocument ParseJsonToXml(byte[] jsonBytes)
        {
            using XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(
                jsonBytes,
                0,
                jsonBytes.Length,
                Encoding.UTF8,
                XmlDictionaryReaderQuotas.Max,
                null);
            return XDocument.Load(reader);
        }

        private static string NormalizeLanguageCode(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return "en";
            return language.Trim().ToLowerInvariant();
        }

        private static string ToIso639_2B(string twoLetter)
        {
            switch (twoLetter)
            {
                case "en": return "eng";
                case "ar": return "ara";
                case "es": return "spa";
                case "fr": return "fre";
                case "de": return "ger";
                case "it": return "ita";
                case "pt": return "por";
                case "tr": return "tur";
                case "ru": return "rus";
                case "zh": return "chi";
                case "ja": return "jpn";
                case "ko": return "kor";
                case "nl": return "dut";
                case "pl": return "pol";
                default: return string.Empty;
            }
        }
    }
}
