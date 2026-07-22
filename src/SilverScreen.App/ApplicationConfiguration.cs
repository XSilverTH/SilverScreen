namespace SilverScreen;

/// <summary>Configuration values supplied to the application's composition root.</summary>
public sealed class ApplicationConfiguration
{
    private const string DefaultDiscordApplicationId = "1528325550475579522";

    /// <summary>The Discord application identifier used for Rich Presence.</summary>
    public string? DiscordApplicationId { get; private init; } = DefaultDiscordApplicationId;

    public static ApplicationConfiguration FromEnvironment()
    {
        return new ApplicationConfiguration
        {
            DiscordApplicationId = Environment.GetEnvironmentVariable("SILVERSCREEN_DISCORD_APPLICATION_ID")
                                   ?? DefaultDiscordApplicationId
        };
    }
}