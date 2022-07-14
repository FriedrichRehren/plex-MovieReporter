using PlexMovieReporter.Commands;
using PlexMovieReporter.Helpers;

var arguments = new Arguments();
arguments.Process(args);

var movieCmd = new MovieCommand();
var movies = movieCmd.ProcessMovies(arguments.InputDirectory);
var outputGen = new GenerateOutputCommand(movies, arguments.OutputDirectory, arguments.OutputFile);

//foreach (var format in arguments.Formats)
//    outputGen.Generate(format);

outputGen.Generate(arguments.Format);

outputGen.Save();