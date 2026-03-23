namespace MovieReporter.Core.Models;

public class Movie
{
    public required string Name { get; set; }
    public int? Year { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public IEnumerable<Resolution> Resolutions { get; set; } = Enumerable.Empty<Resolution>();
}
