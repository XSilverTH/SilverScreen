using System.Reflection;
using System.Runtime.InteropServices;

namespace SilverScreen;

internal static class ApplicationMetadata
{
    internal const string ApplicationId = "io.github.silverscreen.SilverScreen";
    internal const string ApplicationName = "SilverScreen";
    internal const string Copyright = "Copyright © XSilverTH";
    internal const string DeveloperName = "XSilverTH";
    internal const string IssueUrl = "https://github.com/XSilverTH/SilverScreen/issues";
    internal const string SourceUrl = "https://github.com/XSilverTH/SilverScreen";

    internal static readonly string Version = GetVersion();

    private static string GetVersion()
    {
        var assembly = typeof(ApplicationMetadata).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "Unknown"
            : informationalVersion;
    }
}
