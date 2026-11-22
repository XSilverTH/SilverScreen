using System.Reflection;
using Gtk;
using AboutDialog = Adw.AboutDialog;

namespace SilverScreen.Shell;

internal static class ApplicationMetadata
{
    internal const string ApplicationId = "io.github.silverscreen.SilverScreen";
    internal const string IconName = "silverscreen";
    internal const string ApplicationName = "SilverScreen";
    private const string Copyright = "Copyright © XSilverTH";
    private const string DeveloperName = "XSilverTH";
    private const string IssueUrl = "https://github.com/XSilverTH/SilverScreen/issues";
    private const string SourceUrl = "https://github.com/XSilverTH/SilverScreen";

    internal static readonly string Version = GetVersion();

    private static string GetVersion()
    {
        var assembly = typeof(ApplicationMetadata).Assembly;
        var informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "Unknown"
            : informationalVersion;
    }

    internal static void PresentAboutDialog(Window parent)
    {
        var d = AboutDialog.New();
        d.ApplicationIcon = IconName;
        d.ApplicationName = ApplicationName;
        d.Version = Version;
        d.DeveloperName = DeveloperName;
        d.Developers = [DeveloperName];
        d.Comments = "A GTK 4 and Libadwaita desktop app for YouTube.";
        d.Copyright = Copyright;
        d.LicenseType = License.Gpl30;
        d.Website = SourceUrl;
        d.IssueUrl = IssueUrl;
        d.Present(parent);
    }
}