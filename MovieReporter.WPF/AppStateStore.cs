using System.IO;
using System.Text.Json;

namespace MovieReporter.WPF;

public static class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MovieReporter",
        "ui-state.json");

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(StateFilePath))
            {
                return new AppState();
            }

            var json = File.ReadAllText(StateFilePath);
            return JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public static void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var directoryPath = Path.GetDirectoryName(StateFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(StateFilePath, json);
    }
}
