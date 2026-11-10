using System.Diagnostics;
using System.Runtime.InteropServices;
using Adw;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using SilverScreen.Core.Preferences;
using SilverScreen.Shell;
using XSTH.Blueprint.Helpers;

if (args.Length > 0 && args.Contains("--auth")) return RunAuthHelper();

var applicationConfiguration = ApplicationConfiguration.FromEnvironment();
var applicationStateDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".local",
    "state",
    "SilverScreen");
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
            retainedFileCountLimit: 14,
            shared: true)
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
    LogExternalDependencyVersions(serviceProvider.GetRequiredService<IPreferencesService>().GetPreferences());


    Module.Initialize();
    GResourceHelper.RegisterAssemblyResources(typeof(Program).Assembly);

    var app = App.NewWithProperties([]);
    app.UseServices(serviceProvider);
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

static void LogExternalDependencyVersions(AppPreferences preferences)
{
    LogToolVersion("yt-dlp", preferences.YtDlpExecutablePath, "yt-dlp", "--version",
        static output => output.Trim());
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

static void LogToolVersion(
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

static string? ProbeToolVersion(
    string fileName,
    string arguments,
    TimeSpan timeout,
    Func<string, string> extractVersion)
{
    try
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
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

internal partial class Program
{
    [LibraryImport("auth_helper", EntryPoint = "run_auth_helper")]
    private static partial int RunAuthHelper();
}