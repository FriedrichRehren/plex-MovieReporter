using PlexMovieReporter.Helpers;
using PlexMovieReporter.Models;

namespace PlexMovieReporter.Commands
{
    internal class GenerateOutputCommand
    {
        private readonly IEnumerable<Movie> _movies;
        private readonly DirectoryInfo _outputDirectory;
        private readonly string _fileName;

        private Dictionary<Format, object> _generatedOutputs;

        internal GenerateOutputCommand(IEnumerable<Movie> movies, DirectoryInfo outputDirectory, string filename)
        {
            _movies = movies;
            _outputDirectory = outputDirectory;
            _fileName = filename;

            _generatedOutputs = new();
        }

        public void Generate(Format format)
        {
            var generators = new OutputGenerators(_movies);

            switch (format)
            {
                case Format.TXT:
                    _generatedOutputs.Add(format, generators.GenerateTxt(false));
                    break;

                case Format.fTXT:
                    _generatedOutputs.Add(format, generators.GenerateTxt(true));
                    break;

                case Format.CSV:
                    _generatedOutputs.Add(format, generators.GenerateCsv());
                    break;

                case Format.JSON:
                    _generatedOutputs.Add(format, generators.GenerateJson());
                    break;

                case Format.cJSON:
                    _generatedOutputs.Add(format, generators.GenerateJson());
                    break;
            }
        }

        public void Save()
        {
            foreach ((var format, var data) in _generatedOutputs)
            {
                string filePath;

                switch (format)
                {
                    case Format.TXT:
                    case Format.fTXT:
                        filePath = Path.Combine(_outputDirectory.FullName, _fileName + ".txt");
                        FileHandlers.WriteTxt(filePath, (ICollection<string>)data);
                        break;

                    case Format.CSV:
                        filePath = Path.Combine(_outputDirectory.FullName, _fileName + ".csv");
                        FileHandlers.WriteCSV(filePath, (string)data);
                        break;

                    case Format.JSON:
                        filePath = Path.Combine(_outputDirectory.FullName, _fileName + ".json");
                        FileHandlers.WriteTxt(filePath, (string)data);
                        break;

                    case Format.cJSON:
                        Console.WriteLine(data);
                        break;
                }
            }
        }
    }
}