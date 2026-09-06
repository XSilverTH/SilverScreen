using Adw;
using Serilog;
using Serilog.Events;
using SilverScreen.Shell;
using System.Diagnostics;
using System.Runtime.InteropServices;
using XSTH.Blueprint.Helpers;

var applicationStateDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".local",
    "state",
    "SilverScreen");
var logDirectory = Path.Combine(applicationStateDirectory, "logs");
Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(ResolveLogLevel())
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDirectory, "silverscreen-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true)
    .CreateLogger();

using var serviceProvider = ApplicationComposition.CreateServiceProvider(ApplicationConfiguration.FromEnvironment());

try
{
    Log.Information(
        "Starting SilverScreen {Version} on {RuntimeIdentifier} ({OSArchitecture}, {Framework})",
        ApplicationMetadata.Version,
        RuntimeInformation.RuntimeIdentifier,
        RuntimeInformation.OSArchitecture,
        RuntimeInformation.FrameworkDescription);
    LogExternalDependencyVersions();

    Module.Initialize();
    WebKit.Module.Initialize();
    GResourceHelper.RegisterAssemblyResources(typeof(Program).Assembly);

    var app = App.NewWithProperties([]);
    app.UseServices(serviceProvider);
    return app.RunWithSynchronizationContext(args);
}
catch (Exception exception)
{
    Log.Fatal(exception, "SilverScreen terminated unexpectedly");
    throw;
}
finally
{
    Log.Information("Stopping SilverScreen");
    Log.CloseAndFlush();
}

static LogEventLevel ResolveLogLevel()
{
    // Read SILVERSCREEN_LOG_LEVEL directly so startup logging never hard-depends on
    // ApplicationConfiguration.LogLevelOverride (W0-CentralPkgs owns that property and may
    // land before or after this file). Both sides read the same variable, so behavior
    // converges regardless of merge order.
    var raw = Environment.GetEnvironmentVariable("SILVERSCREEN_LOG_LEVEL");
    return raw?.Trim().ToLowerInvariant() switch
    {
        "debug" or "verbose" => LogEventLevel.Debug,
        "warning" or "warn" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "fatal" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}

static void LogExternalDependencyVersions()
{
    // Floors mirror what Arch Linux x86-64 ships at release time (yt-dlp 2026.08.19-1,
    // mpv 0.41.0 as of 2026-09-06). Bump the yt-dlp floor — and the calendar User-Agent
    // override used for yt-dlp requests — with each release.
    const string ytDlpFloorVersion = "2026.08.19";
    const string mpvFloorVersion = "0.41.0";

    LogToolVersion("yt-dlp", "yt-dlp", "--version", ytDlpFloorVersion, static output => output.Trim());
    LogToolVersion("mpv", "mpv", "--version", mpvFloorVersion, static output =>
    {
        // First line looks like "mpv v0.41.0 Copyright ...".
        var firstLine = output.Split('\n', 2)[0].Trim();
        const string prefix = "mpv v";
        return firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? firstLine[prefix.Length..].Split(' ', 2)[0]
            : firstLine;
    });
}

static void LogToolVersion(
    string displayName,
    string fileName,
    string arguments,
    string floorVersion,
    Func<string, string> extractVersion)
{
    var version = ProbeToolVersion(fileName, arguments, TimeSpan.FromSeconds(5), extractVersion);
    if (version is null)
    {
        Log.Warning(
            "{Tool} not found or failed to report a version (need >= {FloorVersion})",
            displayName,
            floorVersion);
        return;
    }

    Log.Information("{Tool} version {Version}", displayName, version);

    if (Version.TryParse(version, out var actual)
        && Version.TryParse(floorVersion, out var floor)
        && actual < floor)
    {
        Log.Warning(
            "{Tool} {ActualVersion} is older than the supported floor {FloorVersion}; please upgrade",
            displayName,
            version,
            floorVersion);
    }
}

static string? ProbeToolVersion(
    string fileName,
    string arguments,
    TimeSpan timeout,
    Func<string, string> extractVersion)
{
    try
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        if (!process.Start())
            return null;

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Best effort: the process already exited or cannot be killed.
            }

            return null;
        }

        if (process.ExitCode != 0)
            return null;

        var output = process.StandardOutput.ReadToEnd();
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var version = extractVersion(output);
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }
    catch
    {
        // Missing binary, permission denied, etc. The caller logs a warning.
        return null;
    }
}
