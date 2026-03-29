using MovieReporter.Core;
using MovieReporter.Core.Models;

namespace MovieReporter.Web.Services;

public sealed class LibraryScanCoordinator
{
    public const string SourceLibraryEnvironmentVariable = "MOVIE_REPORTER_SOURCE_LIBRARY";
    public const string AutoScanIntervalSecondsEnvironmentVariable = "MOVIE_REPORTER_AUTO_SCAN_INTERVAL_SECONDS";

    private static readonly TimeSpan DefaultAutoScanInterval = TimeSpan.FromSeconds(60);

    private readonly ILogger<LibraryScanCoordinator> _logger;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateLock = new();

    private LibraryScanSnapshot _snapshot;

    public LibraryScanCoordinator(ILogger<LibraryScanCoordinator> logger)
    {
        _logger = logger;

        var configuredSourceFolderPath = ResolveConfiguredSourceFolderPath();
        var autoScanInterval = ResolveAutoScanInterval();

        _snapshot = new LibraryScanSnapshot
        {
            Library = new MediaLibrary(),
            AutoScanInterval = autoScanInterval,
            ConfiguredSourceFolderPath = configuredSourceFolderPath,
            StatusText = BuildWaitingStatusText(configuredSourceFolderPath, autoScanInterval)
        };
    }

    public event Action<LibraryScanSnapshot>? StateChanged;

    public LibraryScanSnapshot GetSnapshot()
    {
        lock (_stateLock)
        {
            return _snapshot;
        }
    }

    public async Task<LibraryScanSnapshot> RefreshAsync(LibraryScanTrigger trigger, CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken);

        try
        {
            var currentSnapshot = GetSnapshot();
            var sourceFolderPath = GetConfiguredSourceFolderPathOrThrow(currentSnapshot.ConfiguredSourceFolderPath);
            var busyStatusText = trigger switch
            {
                LibraryScanTrigger.InitialAutomatic => "Running initial library scan...",
                LibraryScanTrigger.Automatic => "Refreshing library automatically...",
                LibraryScanTrigger.ExportRefresh => "Refreshing scan results before export...",
                _ => "Scanning Movies and TV Shows..."
            };

            UpdateSnapshot(currentSnapshot with
            {
                ConfiguredSourceFolderPath = sourceFolderPath,
                IsScanInProgress = true,
                StatusText = busyStatusText
            });

            _logger.LogInformation("Starting {Trigger} for {SourceFolderPath}.", trigger, sourceFolderPath);

            var mediaLibrary = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return MediaLibraryScanner.Scan(sourceFolderPath);
            }, cancellationToken);

            var completedAt = DateTimeOffset.Now;
            var completionStatusText = trigger is LibraryScanTrigger.InitialAutomatic or LibraryScanTrigger.Automatic
                ? $"Library updated automatically. Found {mediaLibrary.Movies.Count} movies and {mediaLibrary.TvShows.Count} TV shows."
                : $"Scan complete. Found {mediaLibrary.Movies.Count} movies and {mediaLibrary.TvShows.Count} TV shows.";

            var completedSnapshot = GetSnapshot() with
            {
                Library = mediaLibrary,
                ConfiguredSourceFolderPath = sourceFolderPath,
                LastCompletedScanAt = completedAt,
                LastScanFailed = false,
                IsScanInProgress = false,
                StatusText = completionStatusText
            };

            UpdateSnapshot(completedSnapshot);

            _logger.LogInformation(
                "Completed {Trigger} for {SourceFolderPath}. Found {MovieCount} movies and {TvShowCount} TV shows.",
                trigger,
                sourceFolderPath,
                mediaLibrary.Movies.Count,
                mediaLibrary.TvShows.Count);

            return completedSnapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Canceled {Trigger}.", trigger);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed {Trigger}.", trigger);

            var failedSnapshot = GetSnapshot() with
            {
                LastScanFailed = true,
                IsScanInProgress = false,
                StatusText = exception.Message
            };

            UpdateSnapshot(failedSnapshot);
            return failedSnapshot;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private void UpdateSnapshot(LibraryScanSnapshot snapshot)
    {
        lock (_stateLock)
        {
            _snapshot = snapshot;
        }

        StateChanged?.Invoke(snapshot);
    }

    private static string BuildWaitingStatusText(string? configuredSourceFolderPath, TimeSpan autoScanInterval)
    {
        if (string.IsNullOrWhiteSpace(configuredSourceFolderPath))
        {
            return $"Scan the library using the {SourceLibraryEnvironmentVariable} environment variable.";
        }

        return autoScanInterval > TimeSpan.Zero
            ? "Waiting for the initial automatic library scan..."
            : "Waiting for the initial library scan...";
    }

    private static string GetConfiguredSourceFolderPathOrThrow(string? configuredSourceFolderPath)
    {
        if (string.IsNullOrWhiteSpace(configuredSourceFolderPath))
        {
            throw new InvalidOperationException(
                $"Set the {SourceLibraryEnvironmentVariable} environment variable to the root media library path.");
        }

        return GetExistingDirectory(configuredSourceFolderPath, "source folder");
    }

    private static string ResolveConfiguredSourceFolderPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(SourceLibraryEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return string.Empty;
        }

        return Path.GetFullPath(configuredPath);
    }

    private TimeSpan ResolveAutoScanInterval()
    {
        var configuredInterval = Environment.GetEnvironmentVariable(AutoScanIntervalSecondsEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(configuredInterval))
        {
            return DefaultAutoScanInterval;
        }

        if (!int.TryParse(configuredInterval, out var intervalSeconds) || intervalSeconds < 0)
        {
            _logger.LogWarning(
                "Invalid {EnvironmentVariable} value '{ConfiguredValue}'. Falling back to {DefaultIntervalSeconds} seconds.",
                AutoScanIntervalSecondsEnvironmentVariable,
                configuredInterval,
                DefaultAutoScanInterval.TotalSeconds);

            return DefaultAutoScanInterval;
        }

        return intervalSeconds == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(intervalSeconds);
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
}

public sealed record LibraryScanSnapshot
{
    public MediaLibrary Library { get; init; } = new();
    public DateTimeOffset? LastCompletedScanAt { get; init; }
    public bool IsScanInProgress { get; init; }
    public bool LastScanFailed { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public TimeSpan AutoScanInterval { get; init; }
    public string? ConfiguredSourceFolderPath { get; init; }
}

public enum LibraryScanTrigger
{
    InitialAutomatic,
    Automatic,
    Manual,
    ExportRefresh
}
