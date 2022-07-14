using PlexMovieReporter.Models;
using System.Reflection;

namespace PlexMovieReporter.Helpers;

internal class Arguments
{
    internal DirectoryInfo InputDirectory { get; private set; }
    internal DirectoryInfo OutputDirectory { get; private set; }
    internal string OutputFile { get; private set; }
    internal Format Format { get; private set; }

    internal Arguments()
    {
        var exePath = Assembly.GetExecutingAssembly().Location;

        // set default values
        InputDirectory = new DirectoryInfo(exePath);
        OutputDirectory = new DirectoryInfo(exePath);
        OutputFile = "movies";
        Format = Format.fTXT;
    }

    internal void Process(string[] args)
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
                        InputDirectory = new DirectoryInfo(args[i + 1]);
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
                        OutputDirectory = new DirectoryInfo(args[i + 1]);
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
                    OutputFile = args[i + 1];
                    break;

                case "-F":
                case "-format":
                case "--format":
                    try
                    {
                        Format = (Format)Enum.Parse(typeof(Format), args[i + 1], true);
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
                    foreach (var line in Commands.HelpCommand.StringList)
                        Console.WriteLine(line);

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
}