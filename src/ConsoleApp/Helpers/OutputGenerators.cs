using PlexMovieReporter.Models;
using System.Text.Json;

namespace PlexMovieReporter.Helpers;

internal class OutputGenerators
{
    private readonly IEnumerable<Movie> _movies;

    internal OutputGenerators(IEnumerable<Movie> movies)
    {
        _movies = movies;
    }

    private int GetMovieNameMaxLength()
    {
        var length = 0;

        foreach (var movie in _movies)
            length = movie.Name.Length > length ? movie.Name.Length : length; // if previous max length is shorter set new length

        // return length
        return length;
    }

    internal string GenerateJson()
    {
        return JsonSerializer.Serialize(_movies);
    }

    internal object GenerateCsv()
    {
        throw new NotImplementedException();
    }

    internal IEnumerable<string> GenerateTxt(bool formatted)
    {
        // lines to write
        var lines = new List<string>();

        if (formatted)
        {
            // set length of columns
            var movieNameLength = GetMovieNameMaxLength();
            var imdbLength = 15; // maximal length of int
            var tmdbLength = 10; // maximal length of int
            var resolutionLength = 25;

            // set length of line
            var lineLength = movieNameLength + imdbLength + tmdbLength + resolutionLength * 6 + 18;

            // add header lines to collection
            lines.Add(new string('-', lineLength));

            lines.Add(string.Format(
                "{0,-" + movieNameLength + "} | " +
                "{1," + imdbLength + "} | " +
                "{2," + tmdbLength + "} | " +
                "{3,-" + resolutionLength + "} | " +
                "{4,-" + resolutionLength + "} | " +
                "{5,-" + resolutionLength + "} | " +
                "{6,-" + resolutionLength + "} | " +
                "{7,-" + resolutionLength + "} | " +
                "{8,-" + resolutionLength + "}",
                "Movie Name",
                "IMDb ID",
                "TMDb ID",
                MovieDictionary.MovieResolutionNames[Resolution.PAL],
                MovieDictionary.MovieResolutionNames[Resolution.HD],
                MovieDictionary.MovieResolutionNames[Resolution.FullHD],
                MovieDictionary.MovieResolutionNames[Resolution.QuadHD],
                MovieDictionary.MovieResolutionNames[Resolution.UHDTV4K],
                MovieDictionary.MovieResolutionNames[Resolution.UHDTV8K]));

            lines.Add(new string('-', lineLength));

            foreach (var movie in _movies)
                // add line to collection
                lines.Add(string.Format(
                    "{0,-" + movieNameLength + "} | " +
                    "{1," + imdbLength + "} | " +
                    "{2," + tmdbLength + "} | " +
                    "{3,-" + resolutionLength + "} | " +
                    "{4,-" + resolutionLength + "} | " +
                    "{5,-" + resolutionLength + "} | " +
                    "{6,-" + resolutionLength + "} | " +
                    "{7,-" + resolutionLength + "} | " +
                    "{8,-" + resolutionLength + "}",
                    movie.Name,
                    movie.ImdbId,
                    movie.TmdbId,
                    movie.Resolutions.Contains(Resolution.PAL) ? "true" : "",
                    movie.Resolutions.Contains(Resolution.HD) ? "true" : "",
                    movie.Resolutions.Contains(Resolution.FullHD) ? "true" : "",
                    movie.Resolutions.Contains(Resolution.QuadHD) ? "true" : "",
                    movie.Resolutions.Contains(Resolution.UHDTV4K) ? "true" : "",
                    movie.Resolutions.Contains(Resolution.UHDTV8K) ? "true" : ""));
        }
        else
        {
            // format each movie in own line
            foreach (var movie in _movies)
            {
                var resolutions = "";

                // get movie resolutions and format them
                foreach ((var res, int i) in movie.Resolutions.Select((res, i) => (res, i)))
                {
                    resolutions += MovieDictionary.MovieResolutionNames[res];

                    if (movie.Resolutions.Count() > 1)
                        if (i < movie.Resolutions.Count() - 1)
                            resolutions += " | ";
                }

                // add line to collection
                lines.Add($"{movie.Name} --- {resolutions}");
            }
        }

        return lines;
    }
}