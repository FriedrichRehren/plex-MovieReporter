namespace PlexMovieReporter.Commands;

internal static class HelpCommand
{
    internal static List<string> StringList
    {
        get
        {
            return new()
            {
                // input
                string.Format("-{0,-7} (-{1}) | {2}","input", "i", "specify the plex movie library folder (default is directory of executable)"),
                // output
                string.Format("-{0,-7} (-{1}) | {2}","output", "o", "specify the output folder (default is directory of executable)"),
                // file
                string.Format("-{0,-7} (-{1}) | {2}","file", "f", "specify the name of the output file (defualt is movies)"),
                // format
                string.Format("-{0,-7} (-{1}) | {2}","format", "F", "specify the output file type. Examples are:"),
                string.Format(new string(' ',16) + "{0,-7} | {1}", "TXT", "unformated text in .txt format"),
                string.Format(new string(' ',16) + "{0,-7} | {1}", "fTXT", "DEFAULT - table-like formated text in .txt format"),
                string.Format(new string(' ',16) + "{0,-7} | {1}", "CSV", "table in .csv format"),
                string.Format(new string(' ',16) + "{0,-7} | {1}", "JSON", "file in .json format"),
                string.Format(new string(' ',16) + "{0,-7} | {1}", "cJSON", "string in json format - this will not generate a file, but output in console"),
                // help
                string.Format("-{0,-7} (-{1}) | {2}","help", "h", "display this dialog")
            };
        }
    }
}