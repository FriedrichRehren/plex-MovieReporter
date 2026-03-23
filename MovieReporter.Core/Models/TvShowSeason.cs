namespace MovieReporter.Core.Models;

public class TvShowSeason
{
    public int Number { get; set; }
    public IReadOnlyCollection<TvShowEpisode> Episodes { get; set; } = Array.Empty<TvShowEpisode>();
    public IReadOnlyCollection<int> MissingEpisodeNumbers { get; set; } = Array.Empty<int>();
    public IReadOnlyCollection<int> DuplicateEpisodeNumbers { get; set; } = Array.Empty<int>();
    public int UnparsedFileCount { get; set; }
}
