using System.Collections.Generic;

namespace MovieReporter.Models
{
    internal class Movie
    {
        public string Name { get; set; }
        public int TmdbId { get; set; }
        public IEnumerable<Resolution> Resolutions { get; set; }
    }
}