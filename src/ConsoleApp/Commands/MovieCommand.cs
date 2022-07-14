using PlexMovieReporter.Models;

namespace PlexMovieReporter.Commands;

internal class MovieCommand
{
    private IEnumerable<DirectoryInfo> GetAllDirectorys(DirectoryInfo directory, string searchPattern = "*") =>
        directory.GetDirectories(searchPattern, SearchOption.TopDirectoryOnly);

    private IEnumerable<Resolution> GetMovieResolutions(DirectoryInfo dir)
    {
        // define hash set to avoid duplicity
        var res = new HashSet<Resolution>();

        foreach (var file in dir.GetFiles())
        {
            if (file.Name.Contains("576p"))
                res.Add(Resolution.PAL);
            else if (file.Name.Contains("720p"))
                res.Add(Resolution.HD);
            else if (file.Name.Contains("1080p"))
                res.Add(Resolution.FullHD);
            else if (file.Name.Contains("1440p"))
                res.Add(Resolution.QuadHD);
            else if (file.Name.Contains("2160p"))
                res.Add(Resolution.UHDTV4K);
            else if (file.Name.Contains("4320p"))
                res.Add(Resolution.UHDTV8K);
        }

        // return hash set
        return res;
    }

    private Movie ProcessMovieDir(DirectoryInfo dir)
    {
        if (!dir.Name.Contains("{"))
            return null;

        var name = dir.Name.Substring(0, dir.Name.IndexOf("{") - 1);
        var imdbTag = "";
        var tmdbTag = "";
        var tag = dir.Name.Substring(dir.Name.IndexOf("{") + 1, dir.Name.IndexOf("}") - dir.Name.IndexOf("{") - 1);

        if (tag.Contains("imdb"))
            imdbTag = tag.Substring(tag.IndexOf("-") + 1);
        else if (tag.Contains("tmdb"))
            tmdbTag = tag.Substring(tag.IndexOf("-") + 1);

        return new Movie()
        {
            Name = name,
            ImdbId = imdbTag,
            TmdbId = tmdbTag,
            Resolutions = GetMovieResolutions(dir)
        };
    }

    internal IEnumerable<Movie> ProcessMovies(DirectoryInfo dir)
    {
        var movies = new HashSet<Movie>();

        foreach (var movieDir in GetAllDirectorys(dir))
        {
            var movie = ProcessMovieDir(movieDir);

            if (movie != null)
                movies.Add(movie);
        }

        return movies;
    }
}