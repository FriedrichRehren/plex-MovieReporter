using MovieReporter.Core.Models;

namespace MovieReporter.Core;

public static class MediaLibraryScanner
{
    public static MediaLibrary Scan(string sourceFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolderPath);

        var normalizedSourceFolderPath = Path.GetFullPath(sourceFolderPath);
        if (!Directory.Exists(normalizedSourceFolderPath))
        {
            throw new DirectoryNotFoundException($"The directory '{normalizedSourceFolderPath}' does not exist.");
        }

        var moviesFolderPath = FindCategoryFolder(normalizedSourceFolderPath, "Movies");
        var tvShowsFolderPath = FindCategoryFolder(normalizedSourceFolderPath, "TV Shows");

        if (moviesFolderPath is null && tvShowsFolderPath is null)
        {
            throw new InvalidOperationException(
                $"The source folder '{normalizedSourceFolderPath}' must contain a 'Movies' folder and/or a 'TV Shows' folder.");
        }

        return new MediaLibrary
        {
            Movies = moviesFolderPath is null
                ? Array.Empty<Movie>()
                : MovieCollectionScanner.Scan(moviesFolderPath),
            TvShows = tvShowsFolderPath is null
                ? Array.Empty<TvShow>()
                : TvShowCollectionScanner.Scan(tvShowsFolderPath)
        };
    }

    private static string? FindCategoryFolder(string sourceFolderPath, string folderName)
    {
        return Directory
            .EnumerateDirectories(sourceFolderPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), folderName, StringComparison.OrdinalIgnoreCase));
    }
}
