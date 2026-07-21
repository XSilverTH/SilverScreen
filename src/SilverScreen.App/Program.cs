using Adw;
using Serilog;
using SilverScreen;
using XSTH.Blueprint.Helpers;

var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
var applicationStateDirectory = string.IsNullOrWhiteSpace(stateHome)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state", "SilverScreen")
    : Path.Combine(stateHome, "SilverScreen");
var logDirectory = Path.Combine(applicationStateDirectory, "logs");
Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
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
    Log.Information("Starting SilverScreen");
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
