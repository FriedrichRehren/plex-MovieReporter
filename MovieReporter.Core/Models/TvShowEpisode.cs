namespace MovieReporter.Core.Models;

public class TvShowEpisode
{
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public required string FileName { get; set; }
    public Resolution? Resolution { get; set; }
}
