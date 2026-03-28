using MovieReporter.Core.Models;

namespace MovieReporter.WPF;

public sealed class AppState
{
    public string? SourceFolderPath { get; init; }
    public string? OutputFolderPath { get; init; }
    public string? OutputFileName { get; init; }
    public Format[] SelectedFormats { get; init; } = [];
    public string? SelectedResultTab { get; init; }
}
