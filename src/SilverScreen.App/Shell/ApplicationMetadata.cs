using System.Reflection;

namespace SilverScreen.Shell;

internal static class ApplicationMetadata
{
    internal const string ApplicationId = "io.github.silverscreen.SilverScreen";
    internal const string IconName = "silverscreen";
    internal const string ApplicationName = "SilverScreen";
    internal const string Copyright = "Copyright © XSilverTH";
    internal const string DeveloperName = "XSilverTH";
    internal const string IssueUrl = "https://github.com/XSilverTH/SilverScreen/issues";
    internal const string SourceUrl = "https://github.com/XSilverTH/SilverScreen";

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

    internal static void PresentAboutDialog(Gtk.Window parent)
    {
        var d = Adw.AboutDialog.New();
        d.ApplicationIcon = IconName;
        d.ApplicationName = ApplicationName;
        d.Version = Version;
        d.DeveloperName = DeveloperName;
        d.Developers = [DeveloperName];
        d.Comments = "A GTK 4 and Libadwaita desktop app for YouTube.";
        d.Copyright = Copyright;
        d.LicenseType = Gtk.License.Gpl30;
        d.Website = SourceUrl;
        d.IssueUrl = IssueUrl;
        d.Present(parent);
    }
}
