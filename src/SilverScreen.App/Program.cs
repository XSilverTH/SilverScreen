using System.Diagnostics;
using System.Runtime.InteropServices;
using Adw;
using Serilog;
using Serilog.Events;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using XSTH.Blueprint.Helpers;

if (args.Length > 0 && args.Contains("--auth")) return RunAuthHelper();

var applicationConfiguration = ApplicationConfiguration.FromEnvironment();
var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
var applicationStateDirectory = string.IsNullOrWhiteSpace(stateHome)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state", "SilverScreen")
    : Path.Combine(stateHome, "SilverScreen");
var logDirectory = Path.Combine(applicationStateDirectory, "logs");
try
{
    Directory.CreateDirectory(logDirectory);
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(ResolveLogLevel(applicationConfiguration.LogLevelOverride))
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(logDirectory, "silverscreen-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14)
        .CreateLogger();
}
catch (Exception setupException)
{
    // Headless/read-only homes must never fail startup: fall back to console-only logging.
    Console.Error.WriteLine(
        $"SilverScreen: file log setup failed ({setupException.Message}); continuing with console logging.");
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(ResolveLogLevel(applicationConfiguration.LogLevelOverride))
        .CreateLogger();
}

IServiceProvider? serviceProvider = null;
var servicesHandedToApplication = false;
try
{
    serviceProvider = ApplicationComposition.CreateServiceProvider(applicationConfiguration);
    Log.Information(
        "Starting SilverScreen {Version} on {RuntimeIdentifier} ({OSArchitecture}, {Framework})",
        ApplicationMetadata.Version,
        RuntimeInformation.RuntimeIdentifier,
        RuntimeInformation.OSArchitecture,
        RuntimeInformation.FrameworkDescription);

    Module.Initialize();
    GResourceHelper.RegisterAssemblyResources(typeof(Program).Assembly);

    var app = App.NewWithProperties([]);
    app.UseServices(serviceProvider);
    var initialQuery = args.FirstOrDefault(static a => !a.StartsWith('-'));
    if (!string.IsNullOrWhiteSpace(initialQuery))
        app.SetInitialQuery(initialQuery);
    servicesHandedToApplication = true;
    return app.RunWithSynchronizationContext(args);
}
catch (Exception exception)
{
    Log.Fatal(exception, "SilverScreen terminated unexpectedly");
    return 1;
}
finally
{
    if (!servicesHandedToApplication)
        (serviceProvider as IDisposable)?.Dispose();
    Log.Information("Stopping SilverScreen");
    Log.CloseAndFlush();
}

static LogEventLevel ResolveLogLevel(string? logLevelOverride)
{
    return logLevelOverride?.Trim().ToLowerInvariant() switch
    {
        "debug" or "verbose" => LogEventLevel.Debug,
        "warning" or "warn" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "fatal" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };
}

internal partial class Program
{
    [LibraryImport("auth_helper", EntryPoint = "run_auth_helper")]
    private static partial int RunAuthHelper();

    internal static void LogExternalDependencyVersions(AppPreferences preferences)
    {
        LogToolVersion("yt-dlp", preferences.YtDlpExecutablePath, "yt-dlp", "--version",
            static output => output.Trim());
        if (PlaybackBackends.IsEmbedded(preferences.PlaybackBackendKind))
            return;

        LogToolVersion("mpv", preferences.MpvExecutablePath, "mpv", "--version",
            static output =>
            {
                var firstLine = output.Split('\n', 2)[0].Trim();
                var tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 1 && tokens[0].Equals("mpv", StringComparison.OrdinalIgnoreCase))
                    return tokens[1].TrimStart('v', 'V');

                return firstLine.TrimStart('v', 'V');
            });
    }

    private static void LogToolVersion(
        string displayName,
        string configuredPath,
        string fallbackFileName,
        string arguments,
        Func<string, string> extractVersion)
    {
        var executablePath = string.IsNullOrWhiteSpace(configuredPath) ? fallbackFileName : configuredPath.Trim();
        var version = ProbeToolVersion(executablePath, arguments, TimeSpan.FromSeconds(5), extractVersion);
        if (version is null)
        {
            Log.Warning("{Tool} not found or failed to report a version", displayName);
            return;
        }

        Log.Information("{Tool} version {Version}", displayName, version);
    }

    private static string? ProbeToolVersion(
        string fileName,
        string arguments,
        TimeSpan timeout,
        Func<string, string> extractVersion)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var output = Process.RunAndCaptureText(startInfo, timeout);
            if (output.ExitStatus is not { ExitCode: 0, Canceled: false, Signal: null } ||
                string.IsNullOrWhiteSpace(output.StandardOutput))
                return null;

            var version = extractVersion(output.StandardOutput);
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            // Missing binary, permission denied, etc. The caller logs a warning.
            return null;
        }
    }
}