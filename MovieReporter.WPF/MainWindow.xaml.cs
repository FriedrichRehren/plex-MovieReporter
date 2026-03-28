using MovieReporter.Core;
using MovieReporter.Core.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Forms = System.Windows.Forms;

namespace MovieReporter.WPF;

public partial class MainWindow : Window
{
    private static readonly Resolution[] OrderedResolutions =
    [
        Resolution.PAL,
        Resolution.HD,
        Resolution.FullHD,
        Resolution.QuadHD,
        Resolution.UHDTV4K,
        Resolution.UHDTV8K
    ];

    private static readonly StatusPalette NeutralPalette = new(
        CreateBrush("#EEE5D7"),
        CreateBrush("#D0C0AA"),
        CreateBrush("#5E625C"));

    private static readonly StatusPalette SuccessPalette = new(
        CreateBrush("#DAEFE1"),
        CreateBrush("#90C0A2"),
        CreateBrush("#29533C"));

    private static readonly StatusPalette AttentionPalette = new(
        CreateBrush("#F7E2D6"),
        CreateBrush("#D69A7A"),
        CreateBrush("#8B4727"));

    private static readonly StatusPalette WarningPalette = new(
        CreateBrush("#F5E8C8"),
        CreateBrush("#D8B66D"),
        CreateBrush("#72511B"));

    private static readonly StatusPalette DangerPalette = new(
        CreateBrush("#F4D9D5"),
        CreateBrush("#D99086"),
        CreateBrush("#8A3029"));

    private readonly ObservableCollection<MovieRow> _movieRows = [];
    private readonly ObservableCollection<TvShowRow> _tvShowRows = [];
    private readonly ObservableCollection<string> _exportedFiles = [];
    private readonly ICollectionView _movieRowsView;
    private readonly ICollectionView _tvShowRowsView;

    private MediaLibrary _scannedLibrary = new();
    private string? _lastScannedSourceFolder;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();

        _movieRowsView = CollectionViewSource.GetDefaultView(_movieRows);
        _movieRowsView.Filter = FilterMovieRow;

        _tvShowRowsView = CollectionViewSource.GetDefaultView(_tvShowRows);
        _tvShowRowsView.Filter = FilterTvShowRow;

        MoviesDataGrid.ItemsSource = _movieRowsView;
        TvShowsDataGrid.ItemsSource = _tvShowRowsView;
        ExportedFilesListBox.ItemsSource = _exportedFiles;

        LoadAppState();
        Closing += MainWindow_Closing;

        UpdateSummary();
        UpdateFilterSummary();
        SetTvShowInspection(null);
        SetStatus("Select a source folder and scan the library.");
    }

    private void BrowseSourceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedFolder = BrowseForFolder(
            "Select the root folder that contains Movies and TV Shows.",
            SourceFolderTextBox.Text);

        if (selectedFolder is not null)
        {
            SourceFolderTextBox.Text = selectedFolder;
        }
    }

    private void BrowseOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedFolder = BrowseForFolder(
            "Select the output folder for exported files.",
            OutputFolderTextBox.Text);

        if (selectedFolder is not null)
        {
            OutputFolderTextBox.Text = selectedFolder;
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteBusyOperationAsync(
            "Scanning Movies and TV Shows...",
            async () =>
            {
                var sourceFolderPath = GetExistingDirectory(SourceFolderTextBox.Text, "source folder");
                var mediaLibrary = await ScanLibraryAsync(sourceFolderPath);

                ApplyScannedLibrary(sourceFolderPath, mediaLibrary);
                SetStatus($"Scan complete. Found {mediaLibrary.Movies.Count} movies and {mediaLibrary.TvShows.Count} TV shows.");
            });
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteBusyOperationAsync(
            $"Exporting {GetActiveResultTabDisplayName()}...",
            async () =>
            {
                var sourceFolderPath = GetExistingDirectory(SourceFolderTextBox.Text, "source folder");
                var outputFolderPath = EnsureOutputDirectory(OutputFolderTextBox.Text);
                var outputFileName = GetOutputFileName(OutputFileNameTextBox.Text);
                var selectedFormats = GetSelectedFormats();

                if (selectedFormats.Length == 0)
                {
                    throw new InvalidOperationException("Select at least one export format.");
                }

                await EnsureCurrentScanAsync(sourceFolderPath);
                var outputPathBase = Path.Combine(outputFolderPath, outputFileName + GetActiveExportSuffix());
                var filteredMovies = GetFilteredMovies();
                var filteredTvShows = GetFilteredTvShows();

                IReadOnlyCollection<ExportResult> results = GetActiveResultTabKey() switch
                {
                    "Movies" when filteredMovies.Length > 0
                        => await OutputGenerator.ExportManyAsync(filteredMovies, outputPathBase, selectedFormats),
                    "Movies"
                        => throw new InvalidOperationException("There are no movie results to export with the current filters."),
                    "TvShows" when filteredTvShows.Length > 0
                        => await TvShowOutputGenerator.ExportManyAsync(filteredTvShows, outputPathBase, selectedFormats),
                    _ => throw new InvalidOperationException("There are no TV show results to export with the current filters.")
                };

                _exportedFiles.Clear();
                foreach (var result in results)
                {
                    _exportedFiles.Add(result.OutputPath);
                }

                SetStatus($"Export complete for {GetActiveResultTabDisplayName()}. Wrote {results.Count} file(s).");
            });
    }

    private void FormatSelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateSummary();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshFilters();
    }

    private void ResultFilterChanged(object sender, RoutedEventArgs e)
    {
        RefreshFilters();
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Text = string.Empty;
        PalFilterCheckBox.IsChecked = false;
        HdFilterCheckBox.IsChecked = false;
        FullHdFilterCheckBox.IsChecked = false;
        QuadHdFilterCheckBox.IsChecked = false;
        Uhd4kFilterCheckBox.IsChecked = false;
        Uhd8kFilterCheckBox.IsChecked = false;

        RefreshFilters();
    }

    private void TvShowsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SetTvShowInspection(TvShowsDataGrid.SelectedItem as TvShowRow);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveAppState();
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
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            System.Windows.MessageBox.Show(
                this,
                exception.Message,
                "Movie Reporter",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
        foreach (var movie in mediaLibrary.Movies)
        {
            _movieRows.Add(new MovieRow
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
            });
        }

        _tvShowRows.Clear();
        foreach (var tvShow in mediaLibrary.TvShows)
        {
            var inspectionState = CreateInspectionState(tvShow);

            _tvShowRows.Add(new TvShowRow
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
                InspectionBackgroundBrush = inspectionState.Palette.BackgroundBrush,
                InspectionBorderBrush = inspectionState.Palette.BorderBrush,
                InspectionForegroundBrush = inspectionState.Palette.ForegroundBrush,
                MetadataText = BuildTvShowMetadataText(tvShow),
                StatusSummaryText = inspectionState.InspectorSummaryText,
                DiagnosticsText = inspectionState.InspectorDiagnosticsText,
                SeasonInspections = CreateSeasonInspectionRows(tvShow)
            });
        }

        _exportedFiles.Clear();
        RefreshFilters();
    }

    private void UpdateSummary()
    {
        var visibleMovies = _movieRowsView.Cast<MovieRow>().ToArray();
        var visibleTvShows = _tvShowRowsView.Cast<TvShowRow>().ToArray();

        MovieCountTextBlock.Text = FormatSummaryCount(visibleMovies.Length, _scannedLibrary.Movies.Count);
        MovieResolutionCountTextBlock.Text = FormatSummaryCount(
            visibleMovies.Sum(movie => movie.AvailableResolutions.Count),
            _scannedLibrary.Movies.Sum(movie => movie.Resolutions.Count()));

        TvShowCountTextBlock.Text = FormatSummaryCount(visibleTvShows.Length, _scannedLibrary.TvShows.Count);
        TvSeasonCountTextBlock.Text = FormatSummaryCount(
            visibleTvShows.Sum(tvShow => tvShow.Source.SeasonCount),
            _scannedLibrary.TvShows.Sum(tvShow => tvShow.SeasonCount));
        TvEpisodeCountTextBlock.Text = FormatSummaryCount(
            visibleTvShows.Sum(tvShow => tvShow.Source.EpisodeCount),
            _scannedLibrary.TvShows.Sum(tvShow => tvShow.EpisodeCount));
        TvResolutionCountTextBlock.Text = FormatSummaryCount(
            visibleTvShows.Sum(tvShow => tvShow.AvailableResolutions.Count),
            _scannedLibrary.TvShows.Sum(tvShow => tvShow.Resolutions.Count()));

        UpdateFilterSummary();
    }

    private void SetBusyState(bool isBusy, string? statusText = null)
    {
        _isBusy = isBusy;
        InputPanel.IsEnabled = !isBusy;
        BusyProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            SetStatus(statusText);
        }
    }

    private void SetStatus(string statusText)
    {
        StatusTextBlock.Text = statusText;
        HeaderStatusTextBlock.Text = statusText;
    }

    private void SetTvShowInspection(TvShowRow? selectedRow)
    {
        if (selectedRow is null)
        {
            ApplyInspectionStatusPalette(NeutralPalette);
            SelectedShowTitleTextBlock.Text = "Select a TV show";
            SelectedShowMetadataTextBlock.Text = "Season and episode coverage will appear here.";
            SelectedShowStatusTextBlock.Text = "No show selected";
            SelectedShowSummaryTextBlock.Text = "Pick a row in the TV Shows table to inspect season gaps and episode coverage.";
            SelectedShowDiagnosticsTextBlock.Text = string.Empty;
            SelectedShowIndexedEpisodesTextBlock.Text = "0";
            SelectedShowMissingEpisodesTextBlock.Text = "0";
            SelectedShowDuplicateEpisodesTextBlock.Text = "0";
            SelectedShowUnparsedFilesTextBlock.Text = "0";
            SelectedShowResolutionItemsControl.ItemsSource = Array.Empty<ResolutionBadgeRow>();
            SelectedShowSeasonItemsControl.ItemsSource = Array.Empty<SeasonInspectionRow>();
            return;
        }

        ApplyInspectionStatusPalette(new StatusPalette(
            selectedRow.InspectionBackgroundBrush,
            selectedRow.InspectionBorderBrush,
            selectedRow.InspectionForegroundBrush));

        SelectedShowTitleTextBlock.Text = BuildMediaDisplayName(selectedRow.Source.Name, selectedRow.Source.Year);
        SelectedShowMetadataTextBlock.Text = selectedRow.MetadataText;
        SelectedShowStatusTextBlock.Text = selectedRow.InspectionStatus;
        SelectedShowSummaryTextBlock.Text = selectedRow.StatusSummaryText;
        SelectedShowDiagnosticsTextBlock.Text = selectedRow.DiagnosticsText;
        SelectedShowIndexedEpisodesTextBlock.Text = selectedRow.Source.IndexedEpisodeCount.ToString();
        SelectedShowMissingEpisodesTextBlock.Text = selectedRow.Source.MissingEpisodeCount.ToString();
        SelectedShowDuplicateEpisodesTextBlock.Text = selectedRow.Source.DuplicateEpisodeCount.ToString();
        SelectedShowUnparsedFilesTextBlock.Text = selectedRow.Source.UnparsedEpisodeFileCount.ToString();
        SelectedShowResolutionItemsControl.ItemsSource = selectedRow.ResolutionBadges;
        SelectedShowSeasonItemsControl.ItemsSource = selectedRow.SeasonInspections;
    }

    private void RefreshFilters()
    {
        _movieRowsView.Refresh();
        _tvShowRowsView.Refresh();
        SyncTvShowSelectionToFilteredResults();
        UpdateSummary();
    }

    private void SyncTvShowSelectionToFilteredResults()
    {
        var visibleTvShowRows = _tvShowRowsView.Cast<TvShowRow>().ToArray();

        if (visibleTvShowRows.Length == 0)
        {
            TvShowsDataGrid.SelectedItem = null;
            SetTvShowInspection(null);
            return;
        }

        if (TvShowsDataGrid.SelectedItem is TvShowRow selectedRow
            && visibleTvShowRows.Contains(selectedRow))
        {
            SetTvShowInspection(selectedRow);
            return;
        }

        TvShowsDataGrid.SelectedItem = visibleTvShowRows[0];
        SetTvShowInspection(visibleTvShowRows[0]);
    }

    private void UpdateFilterSummary()
    {
        var visibleMovieCount = _movieRowsView.Cast<MovieRow>().Count();
        var visibleTvShowCount = _tvShowRowsView.Cast<TvShowRow>().Count();
        var totalMovieCount = _scannedLibrary.Movies.Count;
        var totalTvShowCount = _scannedLibrary.TvShows.Count;

        var selectedResolutionLabels = GetSelectedResolutions()
            .OrderBy(resolution => (int)resolution)
            .Select(resolution => $"{(int)resolution}p")
            .ToArray();

        var activeFilterParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            activeFilterParts.Add($"search \"{SearchTextBox.Text.Trim()}\"");
        }

        if (selectedResolutionLabels.Length > 0)
        {
            activeFilterParts.Add($"resolution {string.Join(", ", selectedResolutionLabels)}");
        }

        FilterSummaryTextBlock.Text = activeFilterParts.Count == 0
            ? $"Showing all scanned results: {FormatCountPhrase(totalMovieCount, "movie", "movies")} and {FormatCountPhrase(totalTvShowCount, "TV show", "TV shows")}."
            : $"Showing {visibleMovieCount} of {totalMovieCount} movies and {visibleTvShowCount} of {totalTvShowCount} TV shows. Active filters: {string.Join(" • ", activeFilterParts)}.";
    }

    private bool FilterMovieRow(object item)
    {
        return item is MovieRow movieRow
            && MatchesSearch(movieRow.SearchIndex)
            && MatchesResolutionFilter(movieRow.AvailableResolutions);
    }

    private bool FilterTvShowRow(object item)
    {
        return item is TvShowRow tvShowRow
            && MatchesSearch(tvShowRow.SearchIndex)
            && MatchesResolutionFilter(tvShowRow.AvailableResolutions);
    }

    private bool MatchesSearch(string searchIndex)
    {
        var searchText = SearchTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(searchText)
            || searchIndex.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesResolutionFilter(IReadOnlyCollection<Resolution> availableResolutions)
    {
        var selectedResolutions = GetSelectedResolutions();
        return selectedResolutions.Length == 0
            || availableResolutions.Any(selectedResolutions.Contains);
    }

    private Resolution[] GetSelectedResolutions()
    {
        var selectedResolutions = new List<Resolution>();

        if (PalFilterCheckBox.IsChecked == true)
        {
            selectedResolutions.Add(Resolution.PAL);
        }

        if (HdFilterCheckBox.IsChecked == true)
        {
            selectedResolutions.Add(Resolution.HD);
        }

        if (FullHdFilterCheckBox.IsChecked == true)
        {
            selectedResolutions.Add(Resolution.FullHD);
        }

        if (QuadHdFilterCheckBox.IsChecked == true)
        {
            selectedResolutions.Add(Resolution.QuadHD);
        }

        if (Uhd4kFilterCheckBox.IsChecked == true)
        {
            selectedResolutions.Add(Resolution.UHDTV4K);
        }

        if (Uhd8kFilterCheckBox.IsChecked == true)
        {
            selectedResolutions.Add(Resolution.UHDTV8K);
        }

        return selectedResolutions.ToArray();
    }

    private void ApplyInspectionStatusPalette(StatusPalette palette)
    {
        SelectedShowStatusBorder.Background = palette.BackgroundBrush;
        SelectedShowStatusBorder.BorderBrush = palette.BorderBrush;
        SelectedShowStatusTextBlock.Foreground = palette.ForegroundBrush;
    }

    private static IReadOnlyCollection<ResolutionBadgeRow> CreateResolutionBadges(IEnumerable<Resolution> availableResolutions)
    {
        var availableResolutionSet = availableResolutions.ToHashSet();

        return OrderedResolutions
            .Select(resolution =>
            {
                var palette = availableResolutionSet.Contains(resolution)
                    ? GetResolutionPalette(resolution)
                    : NeutralPalette;

                return new ResolutionBadgeRow
                {
                    Label = $"{(int)resolution}p",
                    BackgroundBrush = palette.BackgroundBrush,
                    BorderBrush = palette.BorderBrush,
                    ForegroundBrush = palette.ForegroundBrush
                };
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
                    StatusBackground = palette.BackgroundBrush,
                    StatusBorderBrush = palette.BorderBrush,
                    StatusForeground = palette.ForegroundBrush,
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

    private Movie[] GetFilteredMovies()
    {
        return _movieRowsView
            .Cast<MovieRow>()
            .Select(movieRow => movieRow.Source)
            .ToArray();
    }

    private TvShow[] GetFilteredTvShows()
    {
        return _tvShowRowsView
            .Cast<TvShowRow>()
            .Select(tvShowRow => tvShowRow.Source)
            .ToArray();
    }

    private static StatusPalette GetResolutionPalette(Resolution resolution)
    {
        return resolution switch
        {
            Resolution.PAL => new StatusPalette(
                CreateBrush("#EADDC0"),
                CreateBrush("#C6A669"),
                CreateBrush("#6E531D")),
            Resolution.HD => new StatusPalette(
                CreateBrush("#D7ECE4"),
                CreateBrush("#8DBAA8"),
                CreateBrush("#285240")),
            Resolution.FullHD => new StatusPalette(
                CreateBrush("#D9F0DA"),
                CreateBrush("#89C08D"),
                CreateBrush("#2C5931")),
            Resolution.QuadHD => new StatusPalette(
                CreateBrush("#DDE9F5"),
                CreateBrush("#93ADCB"),
                CreateBrush("#28445D")),
            Resolution.UHDTV4K => new StatusPalette(
                CreateBrush("#F6DFD4"),
                CreateBrush("#D39B79"),
                CreateBrush("#843E1F")),
            Resolution.UHDTV8K => new StatusPalette(
                CreateBrush("#E1E6F1"),
                CreateBrush("#9AA7BE"),
                CreateBrush("#33405E")),
            _ => NeutralPalette
        };
    }

    private static Brush CreateBrush(string colorValue)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorValue)!);
        brush.Freeze();
        return brush;
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

    private static string EnsureOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Select an output folder.");
        }

        var normalizedPath = Path.GetFullPath(path);
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

    private Format[] GetSelectedFormats()
    {
        var formats = new List<Format>();

        if (TxtFormatCheckBox.IsChecked == true)
        {
            formats.Add(Format.TXT);
        }

        if (CsvFormatCheckBox.IsChecked == true)
        {
            formats.Add(Format.CSV);
        }

        if (JsonFormatCheckBox.IsChecked == true)
        {
            formats.Add(Format.JSON);
        }

        if (XlsxFormatCheckBox.IsChecked == true)
        {
            formats.Add(Format.XLSX);
        }

        return formats.ToArray();
    }

    private string? BrowseForFolder(string description, string currentPath)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(currentPath)
                ? Path.GetFullPath(currentPath)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private void LoadAppState()
    {
        var appState = AppStateStore.Load();

        SourceFolderTextBox.Text = appState.SourceFolderPath ?? string.Empty;
        OutputFolderTextBox.Text = string.IsNullOrWhiteSpace(appState.OutputFolderPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : appState.OutputFolderPath;
        OutputFileNameTextBox.Text = string.IsNullOrWhiteSpace(appState.OutputFileName)
            ? $"media-report-{DateTime.Now:yyyyMMdd-HHmm}"
            : appState.OutputFileName;

        var selectedFormats = appState.SelectedFormats.Length == 0
            ? [Format.JSON, Format.XLSX]
            : appState.SelectedFormats;

        TxtFormatCheckBox.IsChecked = selectedFormats.Contains(Format.TXT);
        CsvFormatCheckBox.IsChecked = selectedFormats.Contains(Format.CSV);
        JsonFormatCheckBox.IsChecked = selectedFormats.Contains(Format.JSON);
        XlsxFormatCheckBox.IsChecked = selectedFormats.Contains(Format.XLSX);

        ResultsTabControl.SelectedItem = string.Equals(appState.SelectedResultTab, "TvShows", StringComparison.OrdinalIgnoreCase)
            ? TvShowsTabItem
            : MoviesTabItem;
    }

    private void SaveAppState()
    {
        AppStateStore.Save(new AppState
        {
            SourceFolderPath = SourceFolderTextBox.Text,
            OutputFolderPath = OutputFolderTextBox.Text,
            OutputFileName = OutputFileNameTextBox.Text,
            SelectedFormats = GetSelectedFormats(),
            SelectedResultTab = GetActiveResultTabKey()
        });
    }

    private string GetActiveResultTabKey()
    {
        return ReferenceEquals(ResultsTabControl.SelectedItem, TvShowsTabItem) ? "TvShows" : "Movies";
    }

    private string GetActiveResultTabDisplayName()
    {
        return GetActiveResultTabKey() == "TvShows" ? "TV Shows" : "Movies";
    }

    private string GetActiveExportSuffix()
    {
        return GetActiveResultTabKey() == "TvShows" ? "-tv-shows" : "-movies";
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
        public required Brush InspectionBackgroundBrush { get; init; }
        public required Brush InspectionBorderBrush { get; init; }
        public required Brush InspectionForegroundBrush { get; init; }
        public required string MetadataText { get; init; }
        public required string StatusSummaryText { get; init; }
        public required string DiagnosticsText { get; init; }
        public required IReadOnlyCollection<SeasonInspectionRow> SeasonInspections { get; init; }
    }

    private sealed class ResolutionBadgeRow
    {
        public required string Label { get; init; }
        public required Brush BackgroundBrush { get; init; }
        public required Brush BorderBrush { get; init; }
        public required Brush ForegroundBrush { get; init; }
    }

    private sealed class SeasonInspectionRow
    {
        public required string SeasonLabel { get; init; }
        public required string StatusText { get; init; }
        public required Brush StatusBackground { get; init; }
        public required Brush StatusBorderBrush { get; init; }
        public required Brush StatusForeground { get; init; }
        public required string FoundSummary { get; init; }
        public required string MissingSummary { get; init; }
        public required string DiagnosticsSummary { get; init; }
    }

    private readonly record struct StatusPalette(Brush BackgroundBrush, Brush BorderBrush, Brush ForegroundBrush);

    private readonly record struct InspectionState(
        string StatusText,
        string GridSummaryText,
        string InspectorSummaryText,
        string InspectorDiagnosticsText,
        StatusPalette Palette);
}
