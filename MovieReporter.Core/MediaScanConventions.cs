using MovieReporter.Core.Models;
using System.Text.RegularExpressions;

namespace MovieReporter.Core;

internal static partial class MediaScanConventions
{
    internal static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi",
        ".m4v",
        ".mkv",
        ".mov",
        ".mp4",
        ".mpeg",
        ".mpg",
        ".wmv"
    };

    internal static bool TryParseMediaFolderName(string folderName, out ParsedMediaFolder folderDetails)
    {
        var match = MediaFolderPattern().Match(folderName);

        if (!match.Success)
        {
            folderDetails = default;
            return false;
        }

        int? year = null;
        if (match.Groups["year"].Success)
        {
            year = int.Parse(match.Groups["year"].Value);
        }

        folderDetails = new ParsedMediaFolder(
            match.Groups["title"].Value.Trim(),
            year,
            match.Groups["tagType"].Value,
            match.Groups["tagId"].Value);

        return true;
    }

    internal static bool TryParseResolution(string fileName, out Resolution resolution)
    {
        var match = ResolutionPattern().Match(fileName);

        if (!match.Success
            || !int.TryParse(match.Groups["height"].Value, out var height)
            || !Enum.IsDefined(typeof(Resolution), height))
        {
            resolution = default;
            return false;
        }

        resolution = (Resolution)height;
        return true;
    }

    internal static bool TryParseSeasonNumber(string directoryName, out int seasonNumber)
    {
        var match = SeasonFolderPattern().Match(directoryName);

        if (!match.Success || !int.TryParse(match.Groups["season"].Value, out seasonNumber))
        {
            seasonNumber = default;
            return false;
        }

        return true;
    }

    internal static bool TryParseEpisodeReference(
        string fileName,
        int? fallbackSeasonNumber,
        out ParsedEpisodeReference episodeReference)
    {
        if (TryParseEpisodeReferenceFromMatch(SeasonEpisodePattern().Match(fileName), out episodeReference)
            || TryParseEpisodeReferenceFromMatch(XNotationEpisodePattern().Match(fileName), out episodeReference))
        {
            return true;
        }

        if (fallbackSeasonNumber.HasValue)
        {
            var episodeOnlyMatch = EpisodeOnlyPattern().Match(fileName);
            if (episodeOnlyMatch.Success
                && TryParseEpisodeNumbers(episodeOnlyMatch.Groups["tail"].Value, out var episodeNumbers))
            {
                episodeReference = new ParsedEpisodeReference(fallbackSeasonNumber.Value, episodeNumbers);
                return true;
            }
        }

        episodeReference = default;
        return false;
    }

    private static bool TryParseEpisodeReferenceFromMatch(Match match, out ParsedEpisodeReference episodeReference)
    {
        if (!match.Success
            || !int.TryParse(match.Groups["season"].Value, out var seasonNumber)
            || !TryParseEpisodeNumbers(match.Groups["tail"].Value, out var episodeNumbers))
        {
            episodeReference = default;
            return false;
        }

        episodeReference = new ParsedEpisodeReference(seasonNumber, episodeNumbers);
        return true;
    }

    private static bool TryParseEpisodeNumbers(string episodeTokenText, out int[] episodeNumbers)
    {
        episodeNumbers = EpisodeNumberPattern()
            .Matches(episodeTokenText)
            .Cast<Match>()
            .Select(match => int.Parse(match.Value))
            .Distinct()
            .OrderBy(number => number)
            .ToArray();

        if (episodeNumbers.Length == 0)
        {
            return false;
        }

        if (episodeNumbers.Length == 2 && episodeTokenText.Contains('-', StringComparison.Ordinal))
        {
            var startEpisodeNumber = episodeNumbers[0];
            var endEpisodeNumber = episodeNumbers[1];

            if (endEpisodeNumber > startEpisodeNumber)
            {
                episodeNumbers = Enumerable.Range(startEpisodeNumber, endEpisodeNumber - startEpisodeNumber + 1).ToArray();
            }
        }

        return true;
    }

    [GeneratedRegex(@"^(?<title>.+?)(?:\s+\((?<year>\d{4})\))?(?:\s+\{(?<tagType>tmdb|imdb)-(?<tagId>[^}]+)\})?$", RegexOptions.IgnoreCase)]
    private static partial Regex MediaFolderPattern();

    [GeneratedRegex(@"\[(?<height>\d{3,4})p[\]\}]", RegexOptions.IgnoreCase)]
    private static partial Regex ResolutionPattern();

    [GeneratedRegex(@"(?<!\d)(?:season[ ._-]*|s)(?<season>\d{1,2})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolderPattern();

    [GeneratedRegex(@"(?<!\d)[Ss](?<season>\d{1,2})(?<tail>(?:[ ._-]*[Ee]\d{1,3}|-[Ee]?\d{1,3})+)(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisodePattern();

    [GeneratedRegex(@"(?<!\d)(?<season>\d{1,2})x(?<tail>(?:\d{1,3}|-\d{1,3}|x\d{1,3})+)(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex XNotationEpisodePattern();

    [GeneratedRegex(@"(?<!\d)(?<tail>(?:[Ee]\d{1,3}|-[Ee]?\d{1,3})+)(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeOnlyPattern();

    [GeneratedRegex(@"\d{1,3}", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeNumberPattern();

    internal readonly record struct ParsedMediaFolder(string Title, int? Year, string TagType, string TagId);
    internal readonly record struct ParsedEpisodeReference(int SeasonNumber, IReadOnlyCollection<int> EpisodeNumbers);
}
