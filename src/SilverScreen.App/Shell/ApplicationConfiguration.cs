namespace SilverScreen.Shell;

/// <summary>Configuration values supplied to the application's composition root.</summary>
public sealed class ApplicationConfiguration
{
    private const string DefaultDiscordApplicationId = "1528325550475579522";

    /// <summary>The Discord application identifier used for Rich Presence.</summary>
    public string? DiscordApplicationId { get; init; } = DefaultDiscordApplicationId;

    /// <summary>
    ///     Optional Serilog level name override (e.g. <c>Debug</c>, <c>Information</c>).
    ///     Read from <c>SILVERSCREEN_LOG_LEVEL</c>; <c>null</c> means "no override".
    /// </summary>
    public string? LogLevelOverride { get; init; }

    public static ApplicationConfiguration FromEnvironment()
    {
        return new ApplicationConfiguration
        {
            DiscordApplicationId = Environment.GetEnvironmentVariable("SILVERSCREEN_DISCORD_APPLICATION_ID")
                                   ?? DefaultDiscordApplicationId,
            LogLevelOverride = Environment.GetEnvironmentVariable("SILVERSCREEN_LOG_LEVEL")
        };
    }
}