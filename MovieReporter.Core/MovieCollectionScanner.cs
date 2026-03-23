using MovieReporter.Core.Models;

namespace MovieReporter.Core;

public static class MovieCollectionScanner
{
    public static IReadOnlyCollection<Movie> Scan(string rootFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolderPath);

        if (!Directory.Exists(rootFolderPath))
        {
            throw new DirectoryNotFoundException($"The directory '{rootFolderPath}' does not exist.");
        }

        var movies = Directory
            .EnumerateDirectories(rootFolderPath, "*", SearchOption.TopDirectoryOnly)
            .Select(TryBuildMovie)
            .Where(movie => movie is not null)
            .Cast<Movie>()
            .OrderBy(movie => movie.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(movie => movie.Year ?? int.MaxValue)
            .ToArray();

        return movies;
    }

    private static Movie? TryBuildMovie(string movieFolderPath)
    {
        var folderName = Path.GetFileName(movieFolderPath);

        if (!MediaScanConventions.TryParseMediaFolderName(folderName, out var folderDetails))
        {
            return null;
        }

        var resolutions = Directory
            .EnumerateFiles(movieFolderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(filePath => MediaScanConventions.SupportedVideoExtensions.Contains(Path.GetExtension(filePath)))
            .Select(filePath => MediaScanConventions.TryParseResolution(Path.GetFileName(filePath), out var resolution) ? resolution : (Resolution?)null)
            .Where(resolution => resolution.HasValue)
            .Select(resolution => resolution!.Value)
            .Distinct()
            .OrderBy(resolution => (int)resolution)
            .ToArray();

        return new Movie
        {
            Name = folderDetails.Title,
            Year = folderDetails.Year,
            ImdbId = folderDetails.TagType.Equals("imdb", StringComparison.OrdinalIgnoreCase) ? folderDetails.TagId : null,
            TmdbId = folderDetails.TagType.Equals("tmdb", StringComparison.OrdinalIgnoreCase) ? folderDetails.TagId : null,
            Resolutions = resolutions
        };
    }
}
