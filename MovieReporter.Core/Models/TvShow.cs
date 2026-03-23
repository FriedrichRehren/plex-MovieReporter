namespace MovieReporter.Core.Models;

public class TvShow
{
    public required string Name { get; set; }
    public int? Year { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public int SeasonCount { get; set; }
    public int EpisodeCount { get; set; }
    public IEnumerable<Resolution> Resolutions { get; set; } = Enumerable.Empty<Resolution>();
    public IReadOnlyCollection<TvShowSeason> Seasons { get; set; } = Array.Empty<TvShowSeason>();
    public int UnparsedEpisodeFileCount { get; set; }

    public int IndexedEpisodeCount => Seasons.Sum(season => season.Episodes.Count);
    public int MissingEpisodeCount => Seasons.Sum(season => season.MissingEpisodeNumbers.Count);
    public int DuplicateEpisodeCount => Seasons.Sum(season => season.DuplicateEpisodeNumbers.Count);
}
