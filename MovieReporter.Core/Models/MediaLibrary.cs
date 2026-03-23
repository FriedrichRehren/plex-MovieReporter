namespace MovieReporter.Core.Models;

public sealed class MediaLibrary
{
    public IReadOnlyCollection<Movie> Movies { get; init; } = Array.Empty<Movie>();
    public IReadOnlyCollection<TvShow> TvShows { get; init; } = Array.Empty<TvShow>();
}
