using MovieReporter.Core.Models;

namespace MovieReporter.Core;

public static class TvShowCollectionScanner
{
    public static IReadOnlyCollection<TvShow> Scan(string rootFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolderPath);

        if (!Directory.Exists(rootFolderPath))
        {
            throw new DirectoryNotFoundException($"The directory '{rootFolderPath}' does not exist.");
        }

        return Directory
            .EnumerateDirectories(rootFolderPath, "*", SearchOption.TopDirectoryOnly)
            .Select(TryBuildTvShow)
            .Where(tvShow => tvShow is not null)
            .Cast<TvShow>()
            .OrderBy(tvShow => tvShow.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tvShow => tvShow.Year ?? int.MaxValue)
            .ToArray();
    }

    private static TvShow? TryBuildTvShow(string tvShowFolderPath)
    {
        var folderName = Path.GetFileName(tvShowFolderPath);

        if (!MediaScanConventions.TryParseMediaFolderName(folderName, out var folderDetails))
        {
            return null;
        }

        var episodeFileInspections = Directory
            .EnumerateFiles(tvShowFolderPath, "*", SearchOption.AllDirectories)
            .Where(filePath => MediaScanConventions.SupportedVideoExtensions.Contains(Path.GetExtension(filePath)))
            .Select(filePath => InspectEpisodeFile(tvShowFolderPath, filePath))
            .ToArray();

        var resolutions = episodeFileInspections
            .Select(inspection => inspection.Resolution)
            .Where(resolution => resolution.HasValue)
            .Select(resolution => resolution!.Value)
            .Distinct()
            .OrderBy(resolution => (int)resolution)
            .ToArray();

        var seasons = BuildSeasons(episodeFileInspections);

        return new TvShow
        {
            Name = folderDetails.Title,
            Year = folderDetails.Year,
            ImdbId = folderDetails.TagType.Equals("imdb", StringComparison.OrdinalIgnoreCase) ? folderDetails.TagId : null,
            TmdbId = folderDetails.TagType.Equals("tmdb", StringComparison.OrdinalIgnoreCase) ? folderDetails.TagId : null,
            SeasonCount = CountSeasons(episodeFileInspections),
            EpisodeCount = episodeFileInspections.Length,
            Resolutions = resolutions,
            Seasons = seasons,
            UnparsedEpisodeFileCount = episodeFileInspections.Count(inspection => inspection.EpisodeNumbers.Count == 0)
        };
    }

    private static EpisodeFileInspection InspectEpisodeFile(string tvShowFolderPath, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var seasonContainerName = GetSeasonContainerName(tvShowFolderPath, filePath);
        var seasonHint = seasonContainerName is not null
            && MediaScanConventions.TryParseSeasonNumber(seasonContainerName, out var seasonNumber)
            ? seasonNumber
            : (int?)null;

        var hasEpisodeReference = MediaScanConventions.TryParseEpisodeReference(fileName, seasonHint, out var episodeReference);
        var episodeNumbers = hasEpisodeReference
            ? episodeReference.EpisodeNumbers.OrderBy(number => number).ToArray()
            : Array.Empty<int>();

        return new EpisodeFileInspection(
            fileName,
            seasonContainerName,
            hasEpisodeReference ? episodeReference.SeasonNumber : seasonHint,
            episodeNumbers,
            MediaScanConventions.TryParseResolution(fileName, out var resolution) ? resolution : (Resolution?)null);
    }

    private static TvShowSeason[] BuildSeasons(IEnumerable<EpisodeFileInspection> episodeFileInspections)
    {
        var seasons = episodeFileInspections
            .Where(inspection => inspection.SeasonNumber.HasValue)
            .GroupBy(inspection => inspection.SeasonNumber!.Value)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var episodeGroups = group
                    .SelectMany(inspection => inspection.EpisodeNumbers.Select(episodeNumber => new EpisodeOccurrence(
                        episodeNumber,
                        inspection.FileName,
                        inspection.Resolution)))
                    .GroupBy(occurrence => occurrence.EpisodeNumber)
                    .OrderBy(occurrenceGroup => occurrenceGroup.Key)
                    .ToArray();

                var episodes = episodeGroups
                    .Select(occurrenceGroup =>
                    {
                        var preferredOccurrence = occurrenceGroup
                            .OrderByDescending(occurrence => occurrence.Resolution.HasValue)
                            .ThenByDescending(occurrence => occurrence.Resolution.HasValue ? (int)occurrence.Resolution!.Value : -1)
                            .First();

                        return new TvShowEpisode
                        {
                            SeasonNumber = group.Key,
                            EpisodeNumber = occurrenceGroup.Key,
                            FileName = preferredOccurrence.FileName,
                            Resolution = preferredOccurrence.Resolution
                        };
                    })
                    .ToArray();

                var duplicateEpisodeNumbers = episodeGroups
                    .Where(occurrenceGroup => occurrenceGroup.Count() > 1)
                    .Select(occurrenceGroup => occurrenceGroup.Key)
                    .ToArray();

                return new TvShowSeason
                {
                    Number = group.Key,
                    Episodes = episodes,
                    MissingEpisodeNumbers = GetMissingEpisodeNumbers(episodes.Select(episode => episode.EpisodeNumber)),
                    DuplicateEpisodeNumbers = duplicateEpisodeNumbers,
                    UnparsedFileCount = group.Count(inspection => inspection.EpisodeNumbers.Count == 0)
                };
            })
            .ToArray();

        return seasons;
    }

    private static int[] GetMissingEpisodeNumbers(IEnumerable<int> episodeNumbers)
    {
        var orderedEpisodeNumbers = episodeNumbers
            .Distinct()
            .OrderBy(episodeNumber => episodeNumber)
            .ToArray();

        if (orderedEpisodeNumbers.Length == 0)
        {
            return [];
        }

        var firstExpectedEpisodeNumber = orderedEpisodeNumbers.Contains(0) ? 0 : 1;
        var lastEpisodeNumber = orderedEpisodeNumbers[^1];

        return Enumerable
            .Range(firstExpectedEpisodeNumber, lastEpisodeNumber - firstExpectedEpisodeNumber + 1)
            .Except(orderedEpisodeNumbers)
            .ToArray();
    }

    private static int CountSeasons(IEnumerable<EpisodeFileInspection> episodeFileInspections)
    {
        var seasonContainerCount = episodeFileInspections
            .Select(inspection => inspection.SeasonContainerName ?? ".")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var parsedSeasonCount = episodeFileInspections
            .Where(inspection => inspection.SeasonNumber.HasValue)
            .Select(inspection => inspection.SeasonNumber!.Value)
            .Distinct()
            .Count();

        return Math.Max(seasonContainerCount, parsedSeasonCount);
    }

    private static string? GetSeasonContainerName(string tvShowFolderPath, string filePath)
    {
        var relativeDirectoryPath = Path.GetDirectoryName(Path.GetRelativePath(tvShowFolderPath, filePath)) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(relativeDirectoryPath))
        {
            return null;
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return relativeDirectoryPath
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    private readonly record struct EpisodeFileInspection(
        string FileName,
        string? SeasonContainerName,
        int? SeasonNumber,
        IReadOnlyCollection<int> EpisodeNumbers,
        Resolution? Resolution);

    private readonly record struct EpisodeOccurrence(int EpisodeNumber, string FileName, Resolution? Resolution);
}
