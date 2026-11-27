using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CastMPV.Core
{
    public static class SubtitleCacheManager
    {
        private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".vtt", ".sub" };

        private static readonly HashSet<string> NoiseTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1080p", "720p", "480p", "2160p", "4k", "x264", "x265", "h264", "h265", "hevc",
            "webrip", "web-dl", "webdl", "bluray", "bdrip", "hdrip", "dvdrip", "aac", "ac3",
            "dts", "mp4", "mkv", "avi", "ts", "m3u8", "mpd", "yify", "rarbg", "extended", "proper"
        };

        static SubtitleCacheManager()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }
        }

        public static string CacheDirectory
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CastMPV",
                    "Subtitles");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string GetSafeCacheFilePath(string preferredName, string extension = ".srt")
        {
            string ext = string.IsNullOrWhiteSpace(extension) ? ".srt" : extension.Trim().ToLowerInvariant();
            if (!ext.StartsWith(".")) ext = "." + ext;
            if (!SubtitleExtensions.Contains(ext)) ext = ".srt";

            string baseName = Path.GetFileNameWithoutExtension(preferredName ?? "subtitle");
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "subtitle";

            foreach (char invalid in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(invalid, '_');

            baseName = Regex.Replace(baseName, @"\s+", " ").Trim();
            if (baseName.Length > 80) baseName = baseName.Substring(0, 80).Trim();
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "subtitle";

            string cacheRoot = Path.GetFullPath(CacheDirectory);
            string candidate = Path.GetFullPath(Path.Combine(cacheRoot, baseName + ext));
            if (!candidate.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved subtitle path escaped cache directory.");

            return candidate;
        }

        public static async Task<string> DownloadSubtitleToCacheAsync(
            string subtitleUrl,
            string titleHint,
            string userAgent = null,
            string referer = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(subtitleUrl))
                return null;

            if (!Uri.TryCreate(subtitleUrl.Trim(), UriKind.Absolute, out Uri parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            var req = (HttpWebRequest)WebRequest.Create(parsed);
            req.Method = "GET";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.UserAgent = string.IsNullOrWhiteSpace(userAgent)
                ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
                : userAgent.Trim();
            if (!string.IsNullOrWhiteSpace(referer))
                req.Referer = referer.Trim();

            using (ct.Register(() => { try { req.Abort(); } catch { } }))
            using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
            using (Stream stream = resp.GetResponseStream())
            {
                if (stream == null) return null;

                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms).ConfigureAwait(false);
                byte[] data = ms.ToArray();
                if (data.Length == 0) return null;

                // Decompress gzip payloads returned by some subtitle mirrors
                if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
                {
                    using var gzIn = new MemoryStream(data);
                    using var gz = new GZipStream(gzIn, CompressionMode.Decompress);
                    using var gzOut = new MemoryStream();
                    gz.CopyTo(gzOut);
                    data = gzOut.ToArray();
                }

                string rawExt = Path.GetExtension(parsed.LocalPath);
                if (string.IsNullOrWhiteSpace(rawExt) || !SubtitleExtensions.Contains(rawExt.ToLowerInvariant()))
                {
                    string ctype = resp.ContentType?.ToLowerInvariant() ?? string.Empty;
                    if (ctype.Contains("vtt")) rawExt = ".vtt";
                    else if (ctype.Contains("ass")) rawExt = ".ass";
                    else if (ctype.Contains("ssa")) rawExt = ".ssa";
                    else rawExt = ".srt";
                }

                string namePart = !string.IsNullOrWhiteSpace(titleHint)
                    ? titleHint
                    : Path.GetFileNameWithoutExtension(parsed.LocalPath);
                if (string.IsNullOrWhiteSpace(namePart))
                    namePart = "dlna_subtitle_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

                string dest = GetSafeCacheFilePath(namePart, rawExt);
                File.WriteAllBytes(dest, data);
                return dest;
            }
        }

        public static List<string> FindMatchingLocalSubtitles(string mediaUriOrPath, string mediaTitle)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. If playing a local file, inspect its containing directory first
            if (!string.IsNullOrWhiteSpace(mediaUriOrPath) && !mediaUriOrPath.Contains("://"))
            {
                try
                {
                    string fullPath = Path.GetFullPath(mediaUriOrPath);
                    string dir = Path.GetDirectoryName(fullPath);
                    string stem = Path.GetFileNameWithoutExtension(fullPath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        AddDirectoryMatches(dir, stem, results, seen, allowSingleFallback: false);
                        string subsSubDir = Path.Combine(dir, "Subs");
                        if (Directory.Exists(subsSubDir))
                            AddDirectoryMatches(subsSubDir, stem, results, seen, allowSingleFallback: true);
                    }
                }
                catch { }
            }

            // 2. Build search tokens from title / URL filename for cache + Downloads matching
            string matchKey = !string.IsNullOrWhiteSpace(mediaTitle)
                ? mediaTitle
                : ExtractUriFileStem(mediaUriOrPath);

            if (!string.IsNullOrWhiteSpace(matchKey))
            {
                AddDirectoryMatches(CacheDirectory, matchKey, results, seen, allowSingleFallback: false);

                string downloadsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
                if (Directory.Exists(downloadsDir))
                    AddDirectoryMatches(downloadsDir, matchKey, results, seen, allowSingleFallback: false);
            }

            return results;
        }

        private static void AddDirectoryMatches(
            string dir,
            string targetName,
            List<string> results,
            HashSet<string> seen,
            bool allowSingleFallback)
        {
            try
            {
                string[] files = Directory.GetFiles(dir)
                    .Where(f => SubtitleExtensions.Contains((Path.GetExtension(f) ?? string.Empty).ToLowerInvariant()))
                    .ToArray();

                if (files.Length == 0) return;

                // Exact stem or prefix match first
                foreach (string file in files)
                {
                    string fileStem = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
                    if (fileStem.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
                        fileStem.StartsWith(targetName + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seen.Add(file)) results.Add(file);
                    }
                }

                // Fuzzy token match (minimum 2 meaningful shared tokens or exact normalized containment)
                List<string> targetTokens = Tokenize(targetName);
                if (targetTokens.Count > 0)
                {
                    string normTarget = string.Join(" ", targetTokens);
                    var scored = new List<(string File, int Score)>();

                    foreach (string file in files)
                    {
                        if (seen.Contains(file)) continue;
                        List<string> fileTokens = Tokenize(Path.GetFileNameWithoutExtension(file));
                        if (fileTokens.Count == 0) continue;

                        string normFile = string.Join(" ", fileTokens);
                        int overlap = targetTokens.Intersect(fileTokens, StringComparer.OrdinalIgnoreCase).Count();
                        int threshold = Math.Min(2, targetTokens.Count);

                        if (normFile.Contains(normTarget) || normTarget.Contains(normFile) || overlap >= threshold)
                        {
                            scored.Add((file, overlap));
                        }
                    }

                    foreach (var item in scored.OrderByDescending(s => s.Score))
                    {
                        if (seen.Add(item.File)) results.Add(item.File);
                    }
                }

                if (allowSingleFallback && results.Count == 0 && files.Length == 1 && seen.Add(files[0]))
                {
                    results.Add(files[0]);
                }
            }
            catch { }
        }

        private static List<string> Tokenize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();
            return Regex.Matches(input.ToLowerInvariant(), @"[a-z0-9\u0600-\u06FF]+")
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(t => t.Length >= 2 && !NoiseTokens.Contains(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ExtractUriFileStem(string uri)
        {
            try
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed))
                    return Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(parsed.LocalPath));
            }
            catch { }
            return Path.GetFileNameWithoutExtension(uri ?? string.Empty);
        }
    }
}
