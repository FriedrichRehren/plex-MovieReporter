namespace PlexMovieReporter.Models;

public class Movie
{
    public string Name { get; set; }
    public string ImdbId { get; set; }
    public string TmdbId { get; set; }
    public IEnumerable<Resolution> Resolutions { get; set; }
}