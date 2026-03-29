using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MovieReporter.Web.Components;

var builder = WebApplication.CreateBuilder(args);
var enableHttpsRedirection = builder.Configuration.GetValue("MovieReporter:EnableHttpsRedirection", builder.Environment.IsDevelopment());
var dataProtectionKeysPath = ResolveDataProtectionKeysPath();

Directory.CreateDirectory(dataProtectionKeysPath);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
});

// Add services to the container.
builder.Services.AddDataProtection()
    .SetApplicationName("MovieReporter.Web")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var dataProtectionOptions = app.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
var configuredSourceLibrary = Environment.GetEnvironmentVariable("MOVIE_REPORTER_SOURCE_LIBRARY");
var configuredAutoScanInterval = Environment.GetEnvironmentVariable("MOVIE_REPORTER_AUTO_SCAN_INTERVAL_SECONDS");

if (string.IsNullOrWhiteSpace(configuredSourceLibrary))
{
    logger.LogWarning("MOVIE_REPORTER_SOURCE_LIBRARY is not set. Library scanning and exports will fail until it is configured.");
}
else
{
    logger.LogInformation("Configured source library path: {SourceLibraryPath}", configuredSourceLibrary);
}

logger.LogInformation("Configured data protection key path: {DataProtectionKeysPath}", dataProtectionKeysPath);
logger.LogInformation("Data Protection application discriminator: {ApplicationDiscriminator}", dataProtectionOptions.ApplicationDiscriminator);
logger.LogInformation(
    "Starting MovieReporter.Web. Environment={EnvironmentName}; HttpsRedirection={HttpsRedirectionEnabled}; AutoScanIntervalSeconds={AutoScanIntervalSeconds}.",
    app.Environment.EnvironmentName,
    enableHttpsRedirection,
    string.IsNullOrWhiteSpace(configuredAutoScanInterval) ? "default" : configuredAutoScanInterval);

app.Lifetime.ApplicationStarted.Register(() =>
    logger.LogInformation("MovieReporter.Web started and is ready to accept requests."));
app.Lifetime.ApplicationStopping.Register(() =>
    logger.LogInformation("MovieReporter.Web is stopping."));

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (enableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string ResolveDataProtectionKeysPath()
{
    const string dataProtectionKeysPathEnvironmentVariable = "MOVIE_REPORTER_DATA_PROTECTION_KEYS_PATH";

    var configuredPath = Environment.GetEnvironmentVariable(dataProtectionKeysPathEnvironmentVariable);
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        return Path.GetFullPath(configuredPath);
    }

    var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (!string.IsNullOrWhiteSpace(userProfilePath))
    {
        return Path.Combine(userProfilePath, ".aspnet", "DataProtection-Keys");
    }

    return Path.Combine(AppContext.BaseDirectory, ".aspnet", "DataProtection-Keys");
}
