namespace PlexMovieReporter.Helpers;

internal static class FileHandlers
{
    internal static void WriteTxt(string filePath, string line)
    {
        // delete file if exists
        if (File.Exists(filePath))
            File.Delete(filePath);

        // write lines to file
        File.WriteAllText(filePath, line);
    }

    internal static void WriteTxt(string filePath, ICollection<string> lines)
    {
        // delete file if exists
        if (File.Exists(filePath))
            File.Delete(filePath);

        // write lines to file
        File.WriteAllLines(filePath, lines);
    }

    internal static void WriteCSV(string filePath, object table)
    {
        // delete file if exists
        if (File.Exists(filePath))
            File.Delete(filePath);

        throw new NotImplementedException();
    }
}