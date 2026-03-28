using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MovieReporter.Core;
using MovieReporter.Core.Models;

namespace MovieReporter.Web.Components.Pages;

public partial class Home : ComponentBase
{
    private const string UiStateStorageKey = "movieReporter.web.uiState";
    private const string SourceLibraryEnvironmentVariable = "MOVIE_REPORTER_SOURCE_LIBRARY";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static readonly Resolution[] OrderedResolutions =
    [
        Resolution.PAL,
        Resolution.HD,
        Resolution.FullHD,
        Resolution.QuadHD,
        Resolution.UHDTV4K,
        Resolution.UHDTV8K
    ];

    private static readonly Format[] AvailableFormats = Enum.GetValues<Format>();

    private static readonly StatusPalette NeutralPalette = new("#EEE5D7", "#D0C0AA", "#5E625C");
    private static readonly StatusPalette SuccessPalette = new("#DAEFE1", "#90C0A2", "#29533C");
    private static readonly StatusPalette AttentionPalette = new("#F7E2D6", "#D69A7A", "#8B4727");
    private static readonly StatusPalette WarningPalette = new("#F5E8C8", "#D8B66D", "#72511B");
    private static readonly StatusPalette DangerPalette = new("#F4D9D5", "#D99086", "#8A3029");

    private readonly List<MovieRow> _movieRows = [];
    private readonly List<TvShowRow> _tvShowRows = [];
    private readonly List<MovieRow> _filteredMovieRows = [];
    private readonly List<TvShowRow> _filteredTvShowRows = [];
    private readonly List<string> _exportedFiles = [];
    private readonly HashSet<Format> _selectedFormats = [Format.JSON, Format.XLSX];
    private readonly HashSet<Resolution> _selectedResolutions = [];

    private MediaLibrary _scannedLibrary = new();
    private TvShowRow? _selectedTvShowRow;
    private string? _lastScannedSourceFolder;
    private bool _isBusy;
    private bool _lastOperationFailed;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;
    private string OutputFileName { get; set; } = GetDefaultOutputFileName();
    private string SearchText { get; set; } = string.Empty;
    private string StatusText { get; set; } = $"Scan the library using the {SourceLibraryEnvironmentVariable} environment variable.";
    private ResultTab ActiveResultTab { get; set; } = ResultTab.Movies;
    private string? ConfiguredSourceFolderPath { get; set; }

    private bool IsBusy => _isBusy;
    private bool LastOperationFailed => _lastOperationFailed;
    private IReadOnlyList<MovieRow> FilteredMovieRows => _filteredMovieRows;
    private IReadOnlyList<TvShowRow> FilteredTvShowRows => _filteredTvShowRows;
    private IReadOnlyList<string> ExportedFiles => _exportedFiles;
    private TvShowRow? SelectedTvShowRow => _selectedTvShowRow;

    private string MovieCountDisplay => FormatSummaryCount(_filteredMovieRows.Count, _movieRows.Count);

    private string MovieResolutionCountDisplay => FormatSummaryCount(
        _filteredMovieRows.Sum(movie => movie.AvailableResolutions.Count),
        _movieRows.Sum(movie => movie.AvailableResolutions.Count));

    private string TvShowCountDisplay => FormatSummaryCount(_filteredTvShowRows.Count, _tvShowRows.Count);

    private string TvSeasonCountDisplay => FormatSummaryCount(
        _filteredTvShowRows.Sum(tvShow => tvShow.Source.SeasonCount),
        _tvShowRows.Sum(tvShow => tvShow.Source.SeasonCount));

    private string TvEpisodeCountDisplay => FormatSummaryCount(
        _filteredTvShowRows.Sum(tvShow => tvShow.Source.EpisodeCount),
        _tvShowRows.Sum(tvShow => tvShow.Source.EpisodeCount));

    private string TvResolutionCountDisplay => FormatSummaryCount(
        _filteredTvShowRows.Sum(tvShow => tvShow.AvailableResolutions.Count),
        _tvShowRows.Sum(tvShow => tvShow.AvailableResolutions.Count));

    private string FilterSummaryText => BuildFilterSummaryText();
    private StatusPalette SelectedShowPalette => _selectedTvShowRow?.InspectionPalette ?? NeutralPalette;
    private string SelectedShowTitle => _selectedTvShowRow is null
        ? "Select a TV show"
        : BuildMediaDisplayName(_selectedTvShowRow.Source.Name, _selectedTvShowRow.Source.Year);
    private string SelectedShowMetadata => _selectedTvShowRow?.MetadataText ?? "Season and episode coverage will appear here.";
    private string SelectedShowStatusHeading => _selectedTvShowRow?.InspectionStatus ?? "No show selected";
    private string SelectedShowSummary => _selectedTvShowRow?.StatusSummaryText ?? "Pick a row in the TV Shows table to inspect season gaps and episode coverage.";
    private string SelectedShowDiagnostics => _selectedTvShowRow?.DiagnosticsText ?? string.Empty;
    private string SelectedShowIndexedEpisodes => (_selectedTvShowRow?.Source.IndexedEpisodeCount ?? 0).ToString();
    private string SelectedShowMissingEpisodes => (_selectedTvShowRow?.Source.MissingEpisodeCount ?? 0).ToString();
    private string SelectedShowDuplicateEpisodes => (_selectedTvShowRow?.Source.DuplicateEpisodeCount ?? 0).ToString();
    private string SelectedShowUnparsedFiles => (_selectedTvShowRow?.Source.UnparsedEpisodeFileCount ?? 0).ToString();
    private IReadOnlyCollection<ResolutionBadgeRow> SelectedShowResolutionBadges => _selectedTvShowRow?.ResolutionBadges ?? Array.Empty<ResolutionBadgeRow>();
    private IReadOnlyCollection<SeasonInspectionRow> SelectedShowSeasonInspections => _selectedTvShowRow?.SeasonInspections ?? Array.Empty<SeasonInspectionRow>();

    protected override void OnInitialized()
    {
        ConfiguredSourceFolderPath = GetConfiguredSourceFolderPath();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await LoadUiStateAsync();
        RefreshFilters();
        StateHasChanged();
    }

    private async Task OnOutputFileNameChangedAsync(ChangeEventArgs args)
    {
        OutputFileName = args.Value?.ToString() ?? string.Empty;
        await PersistUiStateSafeAsync();
    }

    private async Task OnFormatChangedAsync(Format format, ChangeEventArgs args)
    {
        if (IsChecked(args))
        {
            _selectedFormats.Add(format);
        }
        else
        {
            _selectedFormats.Remove(format);
        }

        await PersistUiStateSafeAsync();
    }

    private void OnSearchTextChanged(ChangeEventArgs args)
    {
        SearchText = args.Value?.ToString() ?? string.Empty;
        RefreshFilters();
    }

    private void ToggleResolutionFilter(Resolution resolution)
    {
        if (!_selectedResolutions.Add(resolution))
        {
            _selectedResolutions.Remove(resolution);
        }

        RefreshFilters();
    }

    private void ClearFilters()
    {
        SearchText = string.Empty;
        _selectedResolutions.Clear();
        RefreshFilters();
    }

    private async Task ChangeTabAsync(ResultTab tab)
    {
        if (ActiveResultTab == tab)
        {
            return;
        }

        ActiveResultTab = tab;
        await PersistUiStateSafeAsync();
    }

    private void SelectTvShow(TvShowRow tvShowRow)
    {
        _selectedTvShowRow = tvShowRow;
    }

    private bool IsFormatSelected(Format format)
    {
        return _selectedFormats.Contains(format);
    }

    private bool IsResolutionSelected(Resolution resolution)
    {
        return _selectedResolutions.Contains(resolution);
    }

    private async Task ScanLibraryAsync()
    {
        await ExecuteBusyOperationAsync(
            "Scanning Movies and TV Shows...",
            async () =>
            {
                var sourceFolderPath = GetConfiguredSourceFolderPathOrThrow();
                var mediaLibrary = await ScanLibraryAsync(sourceFolderPath);

                ApplyScannedLibrary(sourceFolderPath, mediaLibrary);
                SetStatus($"Scan complete. Found {mediaLibrary.Movies.Count} movies and {mediaLibrary.TvShows.Count} TV shows.");
            });
    }

    private async Task ExportActiveTabAsync()
    {
        await ExecuteBusyOperationAsync(
            $"Exporting {GetActiveResultTabDisplayName()}...",
            async () =>
            {
                var sourceFolderPath = GetConfiguredSourceFolderPathOrThrow();
                var outputFolderPath = EnsureDownloadDirectory();
                var outputFileName = GetOutputFileName(OutputFileName);
                var selectedFormats = GetSelectedFormats();

                if (selectedFormats.Length == 0)
                {
                    throw new InvalidOperationException("Select at least one export format.");
                }

                await EnsureCurrentScanAsync(sourceFolderPath);
                var outputPathBase = Path.Combine(outputFolderPath, outputFileName + GetActiveExportSuffix());
                var filteredMovies = GetFilteredMovies();
                var filteredTvShows = GetFilteredTvShows();

                IReadOnlyCollection<ExportResult> results = ActiveResultTab switch
                {
                    ResultTab.Movies when filteredMovies.Length > 0
                        => await OutputGenerator.ExportManyAsync(filteredMovies, outputPathBase, selectedFormats),
                    ResultTab.Movies
                        => throw new InvalidOperationException("There are no movie results to export with the current filters."),
                    ResultTab.TvShows when filteredTvShows.Length > 0
                        => await TvShowOutputGenerator.ExportManyAsync(filteredTvShows, outputPathBase, selectedFormats),
                    _ => throw new InvalidOperationException("There are no TV show results to export with the current filters.")
                };

                _exportedFiles.Clear();
                _exportedFiles.AddRange(results.Select(result => result.OutputPath));

                SetStatus($"Export complete for {GetActiveResultTabDisplayName()}. Wrote {results.Count} file(s).");
            });
    }

    private async Task ExecuteBusyOperationAsync(string busyMessage, Func<Task> operation)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            SetBusyState(true, busyMessage);
            await operation();
            _lastOperationFailed = false;
        }
        catch (Exception exception)
        {
            _lastOperationFailed = true;
            SetStatus(exception.Message);

            try
            {
                await JSRuntime.InvokeVoidAsync("alert", exception.Message);
            }
            catch (JSDisconnectedException)
            {
            }
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task<MediaLibrary> EnsureCurrentScanAsync(string sourceFolderPath)
    {
        var normalizedSourceFolderPath = Path.GetFullPath(sourceFolderPath);

        if (!string.IsNullOrWhiteSpace(_lastScannedSourceFolder)
            && string.Equals(_lastScannedSourceFolder, normalizedSourceFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            return _scannedLibrary;
        }

        SetStatus("Refreshing scan results before export...");
        var mediaLibrary = await ScanLibraryAsync(normalizedSourceFolderPath);
        ApplyScannedLibrary(normalizedSourceFolderPath, mediaLibrary);

        return mediaLibrary;
    }

    private static Task<MediaLibrary> ScanLibraryAsync(string sourceFolderPath)
    {
        return Task.Run(() => MediaLibraryScanner.Scan(sourceFolderPath));
    }

    private void ApplyScannedLibrary(string sourceFolderPath, MediaLibrary mediaLibrary)
    {
        _scannedLibrary = mediaLibrary;
        _lastScannedSourceFolder = Path.GetFullPath(sourceFolderPath);

        _movieRows.Clear();
        _movieRows.AddRange(mediaLibrary.Movies.Select(movie => new MovieRow
        {
            Source = movie,
            Name = movie.Name,
            Year = movie.Year?.ToString() ?? string.Empty,
            ImdbId = movie.ImdbId ?? string.Empty,
            TmdbId = movie.TmdbId ?? string.Empty,
            SearchIndex = BuildSearchIndex(
                movie.Name,
                movie.Year?.ToString(),
                movie.ImdbId,
                movie.TmdbId),
            AvailableResolutions = movie.Resolutions.ToArray(),
            ResolutionBadges = CreateResolutionBadges(movie.Resolutions)
        }));

        _tvShowRows.Clear();
        _tvShowRows.AddRange(mediaLibrary.TvShows.Select(tvShow =>
        {
            var inspectionState = CreateInspectionState(tvShow);

            return new TvShowRow
            {
                Source = tvShow,
                Name = tvShow.Name,
                Year = tvShow.Year?.ToString() ?? string.Empty,
                SeasonCount = tvShow.SeasonCount.ToString(),
                EpisodeCount = tvShow.EpisodeCount.ToString(),
                SearchIndex = BuildSearchIndex(
                    tvShow.Name,
                    tvShow.Year?.ToString(),
                    tvShow.ImdbId,
                    tvShow.TmdbId,
                    tvShow.SeasonCount.ToString(),
                    tvShow.EpisodeCount.ToString()),
                AvailableResolutions = tvShow.Resolutions.ToArray(),
                ResolutionBadges = CreateResolutionBadges(tvShow.Resolutions),
                InspectionStatus = inspectionState.StatusText,
                InspectionSummary = inspectionState.GridSummaryText,
                InspectionPalette = inspectionState.Palette,
                MetadataText = BuildTvShowMetadataText(tvShow),
                StatusSummaryText = inspectionState.InspectorSummaryText,
                DiagnosticsText = inspectionState.InspectorDiagnosticsText,
                SeasonInspections = CreateSeasonInspectionRows(tvShow)
            };
        }));

        _exportedFiles.Clear();
        RefreshFilters();
    }

    private void RefreshFilters()
    {
        _filteredMovieRows.Clear();
        _filteredMovieRows.AddRange(_movieRows.Where(FilterMovieRow));

        _filteredTvShowRows.Clear();
        _filteredTvShowRows.AddRange(_tvShowRows.Where(FilterTvShowRow));

        SyncTvShowSelectionToFilteredResults();
    }

    private void SyncTvShowSelectionToFilteredResults()
    {
        if (_filteredTvShowRows.Count == 0)
        {
            _selectedTvShowRow = null;
            return;
        }

        if (_selectedTvShowRow is not null && _filteredTvShowRows.Contains(_selectedTvShowRow))
        {
            return;
        }

        _selectedTvShowRow = _filteredTvShowRows[0];
    }

    private bool FilterMovieRow(MovieRow movieRow)
    {
        return MatchesSearch(movieRow.SearchIndex)
            && MatchesResolutionFilter(movieRow.AvailableResolutions);
    }

    private bool FilterTvShowRow(TvShowRow tvShowRow)
    {
        return MatchesSearch(tvShowRow.SearchIndex)
            && MatchesResolutionFilter(tvShowRow.AvailableResolutions);
    }

    private bool MatchesSearch(string searchIndex)
    {
        var searchText = SearchText.Trim();
        return string.IsNullOrWhiteSpace(searchText)
            || searchIndex.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesResolutionFilter(IReadOnlyCollection<Resolution> availableResolutions)
    {
        return _selectedResolutions.Count == 0
            || availableResolutions.Any(_selectedResolutions.Contains);
    }

    private string BuildFilterSummaryText()
    {
        var totalMovieCount = _movieRows.Count;
        var totalTvShowCount = _tvShowRows.Count;

        var selectedResolutionLabels = _selectedResolutions
            .OrderBy(resolution => (int)resolution)
            .Select(GetResolutionLabel)
            .ToArray();

        var activeFilterParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            activeFilterParts.Add($"search \"{SearchText.Trim()}\"");
        }

        if (selectedResolutionLabels.Length > 0)
        {
            activeFilterParts.Add($"resolution {string.Join(", ", selectedResolutionLabels)}");
        }

        return activeFilterParts.Count == 0
            ? $"Showing all scanned results: {FormatCountPhrase(totalMovieCount, "movie", "movies")} and {FormatCountPhrase(totalTvShowCount, "TV show", "TV shows")}."
            : $"Showing {_filteredMovieRows.Count} of {totalMovieCount} movies and {_filteredTvShowRows.Count} of {totalTvShowCount} TV shows. Active filters: {string.Join(" • ", activeFilterParts)}.";
    }

    private Movie[] GetFilteredMovies()
    {
        return _filteredMovieRows
            .Select(movieRow => movieRow.Source)
            .ToArray();
    }

    private TvShow[] GetFilteredTvShows()
    {
        return _filteredTvShowRows
            .Select(tvShowRow => tvShowRow.Source)
            .ToArray();
    }

    private Format[] GetSelectedFormats()
    {
        return AvailableFormats
            .Where(_selectedFormats.Contains)
            .ToArray();
    }

    private static IReadOnlyCollection<ResolutionBadgeRow> CreateResolutionBadges(IEnumerable<Resolution> availableResolutions)
    {
        var availableResolutionSet = availableResolutions.ToHashSet();

        return OrderedResolutions
            .Select(resolution => new ResolutionBadgeRow
            {
                Label = GetResolutionLabel(resolution),
                Palette = availableResolutionSet.Contains(resolution)
                    ? GetResolutionPalette(resolution)
                    : NeutralPalette
            })
            .ToArray();
    }

    private static InspectionState CreateInspectionState(TvShow tvShow)
    {
        var indexedEpisodeCount = tvShow.IndexedEpisodeCount;
        var missingEpisodeCount = tvShow.MissingEpisodeCount;
        var duplicateEpisodeCount = tvShow.DuplicateEpisodeCount;
        var unparsedFileCount = tvShow.UnparsedEpisodeFileCount;
        var indexedSeasonCount = tvShow.Seasons.Count;

        if (indexedSeasonCount == 0)
        {
            return new InspectionState(
                "No tags",
                $"{tvShow.EpisodeCount} file(s) not indexed",
                tvShow.EpisodeCount == 0
                    ? "No video files were found for this show."
                    : $"No episode numbers were parsed from {FormatCountPhrase(tvShow.EpisodeCount, "video file", "video files")}.",
                tvShow.EpisodeCount == 0
                    ? "Missing-episode detection is unavailable because the show folder is empty."
                    : "Use filenames like S01E03 or 1x03 to detect missing episodes automatically.",
                NeutralPalette);
        }

        var gridSummaryParts = new List<string> { $"{indexedEpisodeCount} indexed" };
        if (missingEpisodeCount > 0)
        {
            gridSummaryParts.Add($"{missingEpisodeCount} missing");
        }

        if (duplicateEpisodeCount > 0)
        {
            gridSummaryParts.Add($"{duplicateEpisodeCount} dup");
        }

        if (unparsedFileCount > 0)
        {
            gridSummaryParts.Add($"{unparsedFileCount} unparsed");
        }

        if (gridSummaryParts.Count == 1)
        {
            gridSummaryParts.Add("clean");
        }

        var diagnosticsParts = new List<string>();
        if (missingEpisodeCount > 0)
        {
            diagnosticsParts.Add($"{FormatCountPhrase(missingEpisodeCount, "missing episode", "missing episodes")} detected");
        }

        if (duplicateEpisodeCount > 0)
        {
            diagnosticsParts.Add($"{FormatCountPhrase(duplicateEpisodeCount, "duplicate episode", "duplicate episodes")} detected");
        }

        if (unparsedFileCount > 0)
        {
            diagnosticsParts.Add($"{FormatCountPhrase(unparsedFileCount, "unparsed file", "unparsed files")}");
        }

        var palette = missingEpisodeCount > 0
            ? DangerPalette
            : duplicateEpisodeCount > 0
                ? WarningPalette
                : unparsedFileCount > 0
                    ? AttentionPalette
                    : SuccessPalette;

        var statusText = missingEpisodeCount > 0
            ? $"Missing {missingEpisodeCount}"
            : duplicateEpisodeCount > 0
                ? "Duplicates"
                : unparsedFileCount > 0
                    ? "Partial"
                    : "Complete";

        return new InspectionState(
            statusText,
            string.Join(" • ", gridSummaryParts),
            $"{FormatCountPhrase(indexedEpisodeCount, "indexed episode number", "indexed episode numbers")} across {FormatCountPhrase(indexedSeasonCount, "season", "seasons")}.",
            diagnosticsParts.Count == 0
                ? "No numbering gaps detected in indexed seasons."
                : string.Join(" • ", diagnosticsParts),
            palette);
    }

    private static IReadOnlyCollection<SeasonInspectionRow> CreateSeasonInspectionRows(TvShow tvShow)
    {
        return tvShow.Seasons
            .OrderBy(season => season.Number)
            .Select(season =>
            {
                var (statusText, palette) = CreateSeasonStatus(season);

                return new SeasonInspectionRow
                {
                    SeasonLabel = $"Season {season.Number:00}",
                    StatusText = statusText,
                    Palette = palette,
                    FoundSummary = season.Episodes.Count == 0
                        ? "Found: no indexed episode numbers."
                        : $"Found: {FormatEpisodeRangeSummary(season.Episodes.Select(episode => episode.EpisodeNumber))}",
                    MissingSummary = season.MissingEpisodeNumbers.Count == 0
                        ? "Missing: none detected."
                        : $"Missing: {FormatEpisodeRangeSummary(season.MissingEpisodeNumbers)}",
                    DiagnosticsSummary = $"Duplicates: {(season.DuplicateEpisodeNumbers.Count == 0 ? "none" : FormatEpisodeRangeSummary(season.DuplicateEpisodeNumbers))} • Unparsed files: {season.UnparsedFileCount}"
                };
            })
            .ToArray();
    }

    private static (string StatusText, StatusPalette Palette) CreateSeasonStatus(TvShowSeason season)
    {
        if (season.MissingEpisodeNumbers.Count > 0)
        {
            return ($"{season.MissingEpisodeNumbers.Count} missing", DangerPalette);
        }

        if (season.DuplicateEpisodeNumbers.Count > 0)
        {
            return ("Duplicates", WarningPalette);
        }

        if (season.UnparsedFileCount > 0 && season.Episodes.Count == 0)
        {
            return ("Unparsed", NeutralPalette);
        }

        if (season.UnparsedFileCount > 0)
        {
            return ("Partial", AttentionPalette);
        }

        return ("Complete", SuccessPalette);
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        _isBusy = isBusy;

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            SetStatus(statusText);
        }
    }

    private void SetStatus(string statusText)
    {
        StatusText = statusText;
    }

    private async Task LoadUiStateAsync()
    {
        try
        {
            var storedStateJson = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", UiStateStorageKey);
            if (string.IsNullOrWhiteSpace(storedStateJson))
            {
                return;
            }

            var state = JsonSerializer.Deserialize<WebAppState>(storedStateJson, JsonOptions);
            if (state is null)
            {
                return;
            }

            OutputFileName = string.IsNullOrWhiteSpace(state.OutputFileName)
                ? GetDefaultOutputFileName()
                : state.OutputFileName;

            _selectedFormats.Clear();
            foreach (var format in state.SelectedFormats is { Length: > 0 }
                ? state.SelectedFormats
                : [Format.JSON, Format.XLSX])
            {
                _selectedFormats.Add(format);
            }

            ActiveResultTab = string.Equals(state.SelectedResultTab, "TvShows", StringComparison.OrdinalIgnoreCase)
                ? ResultTab.TvShows
                : ResultTab.Movies;
        }
        catch (JSException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private async Task PersistUiStateSafeAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(new WebAppState
            {
                OutputFileName = OutputFileName,
                SelectedFormats = GetSelectedFormats(),
                SelectedResultTab = ActiveResultTab == ResultTab.TvShows ? "TvShows" : "Movies"
            }, JsonOptions);

            await JSRuntime.InvokeVoidAsync("localStorage.setItem", UiStateStorageKey, json);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
    }

    private static string BuildTvShowMetadataText(TvShow tvShow)
    {
        var parts = new List<string>();

        if (tvShow.Year.HasValue)
        {
            parts.Add(tvShow.Year.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(tvShow.ImdbId))
        {
            parts.Add($"IMDb {tvShow.ImdbId}");
        }

        if (!string.IsNullOrWhiteSpace(tvShow.TmdbId))
        {
            parts.Add($"TMDb {tvShow.TmdbId}");
        }

        parts.Add($"{tvShow.SeasonCount} season folder(s)");
        parts.Add($"{tvShow.EpisodeCount} video file(s)");

        return string.Join(" • ", parts);
    }

    private static string BuildMediaDisplayName(string name, int? year)
    {
        return year.HasValue ? $"{name} ({year.Value})" : name;
    }

    private static string FormatEpisodeRangeSummary(IEnumerable<int> episodeNumbers)
    {
        var orderedEpisodeNumbers = episodeNumbers
            .Distinct()
            .OrderBy(episodeNumber => episodeNumber)
            .ToArray();

        if (orderedEpisodeNumbers.Length == 0)
        {
            return "none";
        }

        var ranges = new List<string>();
        var rangeStart = orderedEpisodeNumbers[0];
        var rangeEnd = orderedEpisodeNumbers[0];

        foreach (var episodeNumber in orderedEpisodeNumbers.Skip(1))
        {
            if (episodeNumber == rangeEnd + 1)
            {
                rangeEnd = episodeNumber;
                continue;
            }

            ranges.Add(FormatEpisodeRange(rangeStart, rangeEnd));
            rangeStart = episodeNumber;
            rangeEnd = episodeNumber;
        }

        ranges.Add(FormatEpisodeRange(rangeStart, rangeEnd));
        return string.Join(", ", ranges);
    }

    private static string FormatEpisodeRange(int startEpisodeNumber, int endEpisodeNumber)
    {
        return startEpisodeNumber == endEpisodeNumber
            ? $"E{startEpisodeNumber:00}"
            : $"E{startEpisodeNumber:00}-E{endEpisodeNumber:00}";
    }

    private static string FormatCountPhrase(int count, string singular, string plural)
    {
        return count == 1 ? $"1 {singular}" : $"{count} {plural}";
    }

    private static string FormatSummaryCount(int visibleCount, int totalCount)
    {
        return visibleCount == totalCount
            ? totalCount.ToString()
            : $"{visibleCount} / {totalCount}";
    }

    private static string BuildSearchIndex(params string?[] values)
    {
        return string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string GetExistingDirectory(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"Select a {fieldName}.");
        }

        var normalizedPath = Path.GetFullPath(path);

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"The {fieldName} '{normalizedPath}' does not exist.");
        }

        return normalizedPath;
    }

    private static string EnsureDownloadDirectory()
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath))
        {
            throw new InvalidOperationException("The server user's Downloads folder could not be resolved.");
        }

        var normalizedPath = Path.Combine(userProfilePath, "Downloads");
        Directory.CreateDirectory(normalizedPath);

        return normalizedPath;
    }

    private static string GetOutputFileName(string fileName)
    {
        var normalizedFileName = fileName.Trim();

        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            throw new InvalidOperationException("Enter an export file name.");
        }

        if (normalizedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("The export file name contains invalid characters.");
        }

        return normalizedFileName;
    }

    private string GetActiveResultTabDisplayName()
    {
        return ActiveResultTab == ResultTab.TvShows ? "TV Shows" : "Movies";
    }

    private string GetActiveExportSuffix()
    {
        return ActiveResultTab == ResultTab.TvShows ? "-tv-shows" : "-movies";
    }

    private static string GetResolutionLabel(Resolution resolution)
    {
        return $"{(int)resolution}p";
    }

    private static StatusPalette GetResolutionPalette(Resolution resolution)
    {
        return resolution switch
        {
            Resolution.PAL => new StatusPalette("#EADDC0", "#C6A669", "#6E531D"),
            Resolution.HD => new StatusPalette("#D7ECE4", "#8DBAA8", "#285240"),
            Resolution.FullHD => new StatusPalette("#D9F0DA", "#89C08D", "#2C5931"),
            Resolution.QuadHD => new StatusPalette("#DDE9F5", "#93ADCB", "#28445D"),
            Resolution.UHDTV4K => new StatusPalette("#F6DFD4", "#D39B79", "#843E1F"),
            Resolution.UHDTV8K => new StatusPalette("#E1E6F1", "#9AA7BE", "#33405E"),
            _ => NeutralPalette
        };
    }

    private static bool IsChecked(ChangeEventArgs args)
    {
        return args.Value is bool boolValue && boolValue;
    }

    private string GetConfiguredSourceFolderPathOrThrow()
    {
        ConfiguredSourceFolderPath ??= GetConfiguredSourceFolderPath();

        if (string.IsNullOrWhiteSpace(ConfiguredSourceFolderPath))
        {
            throw new InvalidOperationException(
                $"Set the {SourceLibraryEnvironmentVariable} environment variable to the root media library path.");
        }

        return GetExistingDirectory(ConfiguredSourceFolderPath, "source folder");
    }

    private static string? GetConfiguredSourceFolderPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(SourceLibraryEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        return Path.GetFullPath(configuredPath);
    }

    private static string GetDefaultOutputFileName()
    {
        return $"media-report-{DateTime.Now:yyyyMMdd-HHmm}";
    }

    private enum ResultTab
    {
        Movies,
        TvShows
    }

    private sealed class MovieRow
    {
        public required Movie Source { get; init; }
        public required string Name { get; init; }
        public required string Year { get; init; }
        public required string ImdbId { get; init; }
        public required string TmdbId { get; init; }
        public required string SearchIndex { get; init; }
        public required IReadOnlyCollection<Resolution> AvailableResolutions { get; init; }
        public required IReadOnlyCollection<ResolutionBadgeRow> ResolutionBadges { get; init; }
    }

    private sealed class TvShowRow
    {
        public required TvShow Source { get; init; }
        public required string Name { get; init; }
        public required string Year { get; init; }
        public required string SeasonCount { get; init; }
        public required string EpisodeCount { get; init; }
        public required string SearchIndex { get; init; }
        public required IReadOnlyCollection<Resolution> AvailableResolutions { get; init; }
        public required IReadOnlyCollection<ResolutionBadgeRow> ResolutionBadges { get; init; }
        public required string InspectionStatus { get; init; }
        public required string InspectionSummary { get; init; }
        public required StatusPalette InspectionPalette { get; init; }
        public required string MetadataText { get; init; }
        public required string StatusSummaryText { get; init; }
        public required string DiagnosticsText { get; init; }
        public required IReadOnlyCollection<SeasonInspectionRow> SeasonInspections { get; init; }
    }

    private sealed class ResolutionBadgeRow
    {
        public required string Label { get; init; }
        public required StatusPalette Palette { get; init; }
    }

    private sealed class SeasonInspectionRow
    {
        public required string SeasonLabel { get; init; }
        public required string StatusText { get; init; }
        public required StatusPalette Palette { get; init; }
        public required string FoundSummary { get; init; }
        public required string MissingSummary { get; init; }
        public required string DiagnosticsSummary { get; init; }
    }

    private sealed class WebAppState
    {
        public string? OutputFileName { get; init; }
        public Format[] SelectedFormats { get; init; } = [];
        public string? SelectedResultTab { get; init; }
    }

    private readonly record struct StatusPalette(string BackgroundColor, string BorderColor, string ForegroundColor)
    {
        public string ToInlineStyle()
        {
            return $"background:{BackgroundColor};border-color:{BorderColor};color:{ForegroundColor};";
        }
    }

    private readonly record struct InspectionState(
        string StatusText,
        string GridSummaryText,
        string InspectorSummaryText,
        string InspectorDiagnosticsText,
        StatusPalette Palette);
}
