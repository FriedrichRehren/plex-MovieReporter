using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using System.Reflection;
using MovieReporter.Models;
using System.Text.Json;

namespace MovieReporter
{
    internal class Program
    {
        private static DirectoryInfo _inputDirectory;
        private static DirectoryInfo _outputDirectory;
        private static string _outputFile;
        private static Format _format;

        private static void Main(string[] args)
        {
            // set default values
            _inputDirectory = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory;
            _outputDirectory = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory;
            _outputFile = "movies";
            _format = Format.fTXT;

            // process args
            ProcessArgs(args);

            List<Movie> movies = new List<Movie>();

            foreach (var dir in GetAllDirectorys(_inputDirectory))
            {
                var movie = ParseMovie(dir);

                if (movie != null)
                    movies.Add(movie);
            }

            ComposeOutput(_outputDirectory, _outputFile, _format, movies);
        }

        #region Get Movie Data

        private static IEnumerable<DirectoryInfo> GetAllDirectorys(DirectoryInfo directory, string searchPattern = "*") => directory.GetDirectories(searchPattern, SearchOption.TopDirectoryOnly);

        private static IEnumerable<Resolution> GetMovieResolutions(DirectoryInfo dir)
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
                else if (file.Name.Contains("2160p"))
                    res.Add(Resolution.UHDTV4K);
                else if (file.Name.Contains("4320p"))
                    res.Add(Resolution.UHDTV8K);
            }

            // return hash set
            return res;
        }

        private static Movie ParseMovie(DirectoryInfo dir)
        {
            if (!dir.Name.Contains("{"))
                return null;

            // get name
            var name = dir.Name.Substring(0, dir.Name.IndexOf("{") - 1);
            // get imdb tag
            var imdbTag = dir.Name.Substring(dir.Name.IndexOf("{") + 1, dir.Name.IndexOf("}") - dir.Name.IndexOf("{") - 1);

            // return movie
            return new Movie
            {
                Name = name,
                TmdbId = Convert.ToInt32(string.Join("", imdbTag.Where(char.IsDigit))), // only convert chars
                Resolutions = GetMovieResolutions(dir)
            };
        }

        #endregion Get Movie Data

        #region Helpers

        private static int GetMovieNameMaxLength(IEnumerable<Movie> movies)
        {
            var length = 0;

            foreach (var movie in movies)
                length = movie.Name.Length > length ? movie.Name.Length : length; // if previous max length is shorter set new length

            // return length
            return length;
        }

        private static void ProcessArgs(string[] args)
        {
            foreach ((var arg, int i) in args.Select((arg, i) => (arg, i)))
            {
                switch (arg)
                {
                    case "-i":
                    case "-input":
                    case "--input":
                        try
                        {
                            _inputDirectory = new DirectoryInfo(args[i + 1]);
                        }
                        catch
                        {
                            Console.WriteLine($"{args[i + 1]} is not a directory. See -help for more information");
                            Environment.Exit(0xA0);
                        }
                        break;

                    case "-o":
                    case "-output":
                    case "--output":
                        try
                        {
                            _outputDirectory = new DirectoryInfo(args[i + 1]);
                        }
                        catch
                        {
                            Console.WriteLine($"{args[i + 1]} is not a directory. See -help for more information");
                            Environment.Exit(0xA0);
                        }
                        break;

                    case "-f":
                    case "-file":
                    case "--file":
                        _outputFile = args[i + 1];
                        break;

                    case "-F":
                    case "-format":
                    case "--format":
                        try
                        {
                            _format = (Format)Enum.Parse(typeof(Format), args[i + 1], true);
                        }
                        catch
                        {
                            Console.WriteLine($"{args[i + 1]} is not a valid format. See -help for more information");
                            Environment.Exit(0xA0);
                        }
                        break;

                    case "-h":
                    case "-help":
                    case "--help":
                        foreach (var line in Statics.Help) Console.WriteLine(line);
                        Environment.Exit(0);
                        break;

                    default:
                        if (arg.StartsWith("-"))
                        {
                            Console.WriteLine($"{arg} was not recognized. See -help for more information");
                            Environment.Exit(0xA0);
                        }
                        break;
                }
            }
        }

        private static void WriteTxt(string filePath, string line)
        {
            // delete file if exists
            if (File.Exists(filePath))
                File.Delete(filePath);

            // write lines to file
            File.WriteAllText(filePath, line);
        }

        private static void WriteTxt(string filePath, ICollection<string> lines)
        {
            // delete file if exists
            if (File.Exists(filePath))
                File.Delete(filePath);

            // write lines to file
            File.WriteAllLines(filePath, lines);
        }

        private static void WriteCSV(string filePath, object table)
        {
            // delete file if exists
            if (File.Exists(filePath))
                File.Delete(filePath);

            throw new NotImplementedException();
        }

        #endregion Helpers

        #region File Builders

        private static void ComposeOutput(DirectoryInfo directory, string fileName, Format format, ICollection<Movie> movies)
        {
            switch (format)
            {
                case Format.TXT:
                    WriteTxt(Path.Combine(directory.FullName, fileName + ".txt"), SerializeText(movies, false));
                    break;

                case Format.fTXT:
                    WriteTxt(Path.Combine(directory.FullName, fileName + ".txt"), SerializeText(movies, true));
                    break;

                case Format.CSV:
                    WriteCSV(Path.Combine(directory.FullName, fileName + ".csv"), SerializeTable(movies));
                    break;

                case Format.JSON:
                    WriteTxt(Path.Combine(directory.FullName, fileName + ".json"), JsonSerializer.Serialize(movies));
                    break;

                case Format.cJSON:
                    Console.Write(JsonSerializer.Serialize(movies));
                    break;
            }
        }

        private static ICollection<string> SerializeText(IEnumerable<Movie> movies, bool formatted)
        {
            // lines to write
            var lines = new List<string>();

            if (formatted)
            {
                // set length of columns
                var movieNameLength = GetMovieNameMaxLength(movies);
                var imdbLength = 10; // maximal length of int
                var resolutionLength = 25;

                // set length of line
                var lineLength = movieNameLength + imdbLength + resolutionLength * 5 + 15;

                // add header lines to collection
                lines.Add(new string('-', lineLength));
                lines.Add(string.Format("{0,-" + movieNameLength + "} | {1," + imdbLength + "} | {2,-" + resolutionLength + "} | {3,-" + resolutionLength + "} | {4,-" + resolutionLength + "} | {5,-" + resolutionLength + "} | {6,-" + resolutionLength + "}", "Movie Name", "IMDb ID", Statics.MovieResolutionNames[Resolution.PAL], Statics.MovieResolutionNames[Resolution.HD], Statics.MovieResolutionNames[Resolution.FullHD], Statics.MovieResolutionNames[Resolution.UHDTV4K], Statics.MovieResolutionNames[Resolution.UHDTV8K]));
                lines.Add(new string('-', lineLength));

                foreach (var movie in movies)
                    // add line to collection
                    lines.Add(string.Format("{0,-" + movieNameLength + "} | {1," + imdbLength + "} | {2,-" + resolutionLength + "} | {3,-" + resolutionLength + "} | {4,-" + resolutionLength + "} | {5,-" + resolutionLength + "} | {6,-" + resolutionLength + "}", movie.Name, movie.TmdbId, movie.Resolutions.Contains(Resolution.PAL) ? "true" : "", movie.Resolutions.Contains(Resolution.HD) ? "true" : "", movie.Resolutions.Contains(Resolution.FullHD) ? "true" : "", movie.Resolutions.Contains(Resolution.UHDTV4K) ? "true" : "", movie.Resolutions.Contains(Resolution.UHDTV8K) ? "true" : ""));
            }
            else
            {
                // format each movie in own line
                foreach (var movie in movies)
                {
                    var resolutions = "";

                    // get movie resolutions and format them
                    foreach ((var res, int i) in movie.Resolutions.Select((res, i) => (res, i)))
                    {
                        resolutions += Statics.MovieResolutionNames[res];

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

        private static object SerializeTable(ICollection<Movie> movies)
        {
            return null;
        }

        #endregion File Builders
    }
}