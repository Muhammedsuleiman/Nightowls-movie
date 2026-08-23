using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace NightOwls.Services;

public static partial class TitleParser
{
    private static readonly HashSet<string> JunkTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // resolution / source
        "1080p", "720p", "2160p", "480p", "360p", "240p", "540p", "4k", "uhd", "hdr", "hdr10", "sdr", "hd", "sd",
        "bluray", "blu-ray", "brrip", "bdrip", "bd", "dvdrip", "dvdscr", "webrip", "web-dl", "webdl", "web",
        "hdtv", "hdrip", "hc", "hdts", "camrip", "cam", "tc", "dvd", "remux",
        // codecs / audio
        "x264", "x265", "h264", "h265", "hevc", "avc", "xvid", "divx", "av1",
        "aac", "aac2", "ac3", "eac3", "dts", "dtshd", "truehd", "atmos", "flac", "mp3", "opus",
        "5ch", "7ch", "10bit", "8bit",
        // release groups / scene tags
        "yify", "yts", "rarbg", "ettv", "eztv", "fgt", "amzn", "nf", "ddp", "dd", "proper", "repack",
        "extended", "unrated", "remastered", "internal", "replica", "proxy", "dual", "audio", "multi",
        "subbed", "subs", "sub", "hq", "psa", "ozr", "high", "wide", "complete", "season",
        "downloaded", "uploaded", "rip", "copy", "full", "movie",
        // sites seen in the wild
        "netnaija", "nkiri", "fzmovies", "fzstudios", "o2tvseries", "o2tv", "tvshows4mobile", "tvs4mobile",
        "kisstvseries", "kimoitv", "animepahe", "subsplease", "eraws", "seriezloaded", "naijavault",
        "naijaprey", "naiprey", "9jarocks", "worldmoviecentral", "kimoi", "pahe", "mkvcage", "moviesverse",
        "thefastdownloaders", "fastdownload", "hippo", "pahein",
        // domain leftovers after separator-splitting
        "com", "net", "org", "xyz", "www", "ng", "mx", "co", "site"
    };

    [GeneratedRegex(@"\b(19\d{2}|20\d{2})\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\b[Ss]\d{1,2}\s?[\._\- ]?\s?(?:[EeXx]\d{1,3})\b|\bSeason\s+\d+\b|\bEpisode\s+\d+\b")]
    private static partial Regex SeasonEpisodeRegex();

    [GeneratedRegex(@"^[0-9a-fA-F]{16,}$")]
    private static partial Regex HexHashRegex();

    private static readonly HashSet<string> AudioCodecs = new(StringComparer.OrdinalIgnoreCase)
    { "aac", "aac2", "ac3", "eac3", "dts", "hevc", "x264", "x265", "h264", "h265", "flac", "mp3", "opus" };

    private static readonly HashSet<string> TrailingFiller = new(StringComparer.OrdinalIgnoreCase)
    { "downloaded", "uploaded", "from", "free", "direct", "link", "rip", "copy" };

    public static (string Title, int? Year) Parse(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        try { name = Uri.UnescapeDataString(name); } catch { }
        name = name.Replace('_', ' ').Replace('.', ' ').Replace('-', ' ');
        name = Regex.Replace(name, @"\s+", " ").Trim(' ', '-', '_');

        int? year = null;
        var yearMatch = YearRegex().Match(name);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int y))
            year = y;

        // Drop everything from a season/episode marker onward (episodes keep the show name as title).
        var seMatch = SeasonEpisodeRegex().Match(name);
        if (seMatch.Success && seMatch.Index > 0)
            name = name[..seMatch.Index];

        // Strip bracketed/parenthesised groups entirely (site tags, quality tags, dupes of the year…).
        name = Regex.Replace(name, @"[\(\[\{][^\)\]\}]*[\)\]\}]", " ");
        name = Regex.Replace(name, @"\s+", " ").Trim(' ', '-', '_');

        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = new List<string>();
        bool yearSkipped = false;
        bool lastWasDigit = false;
        for (int i = 0; i < tokens.Length; i++)
        {
            string rawToken = tokens[i];
            string token = rawToken.Trim('-','_','[',']','(',')','~');
            if (token.Length == 0) continue;

            if (year.HasValue && !yearSkipped &&
                token.Equals(year.Value.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
            {
                yearSkipped = true;
                lastWasDigit = false;
                continue;
            }
            if (JunkTokens.Contains(token)) { lastWasDigit = false; continue; }
            if (Regex.IsMatch(token, @"^\d{3,4}[pP]$")) { lastWasDigit = false; continue; }
            if (HexHashRegex().IsMatch(token)) { lastWasDigit = false; continue; }
            if (rawToken.StartsWith("otv-", StringComparison.OrdinalIgnoreCase)) { lastWasDigit = false; continue; }
            if (Regex.IsMatch(rawToken, @"\.(com|net|org|xyz|ng|tv|co|mx)$", RegexOptions.IgnoreCase)) { lastWasDigit = false; continue; }

            // Drop lone digits that belong to audio channel tags ("aac 5 1"), keep real titles ("6 Hours Away").
            if (Regex.IsMatch(token, @"^\d$"))
            {
                string nextRaw = i + 1 < tokens.Length ? tokens[i + 1] : "";
                bool prevIsCodec = i > 0 && AudioCodecs.Contains(tokens[i - 1]);
                if (lastWasDigit || prevIsCodec ||
                    (nextRaw.Length > 0 && Regex.IsMatch(nextRaw.Trim(), @"^\d$")))
                {
                    lastWasDigit = true;
                    continue;
                }
            }

            lastWasDigit = Regex.IsMatch(token, @"^\d+$");
            kept.Add(token);
        }

        // Trim trailing filler words left over from scene naming.
        while (kept.Count > 1 && TrailingFiller.Contains(kept[^1], StringComparer.OrdinalIgnoreCase))
            kept.RemoveAt(kept.Count - 1);

        if (kept.Count == 0)
        {
            string fallback = Path.GetFileNameWithoutExtension(fileName);
            try { fallback = Uri.UnescapeDataString(fallback); } catch { }
            return (string.IsNullOrWhiteSpace(fallback) ? fileName : fallback.Trim(), year);
        }

        string title = TitleCase(string.Join(' ', kept));
        return (title.Trim(), year);
    }

    private static string TitleCase(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string w = parts[i];
            if (w.Length <= 4 && w == w.ToUpperInvariant()) continue; // keep short acronyms
            parts[i] = char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
        }
        return string.Join(' ', parts);
    }
}
