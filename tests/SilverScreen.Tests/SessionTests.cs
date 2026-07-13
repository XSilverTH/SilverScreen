using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.Tests;

public sealed class SessionTests
{
    private const string FakeCookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tfake-session-value\n";

    [Fact]
    public void SessionServiceStartsSignedOut()
    {
        var service = new InMemorySessionService();

        var session = service.GetCurrentSession();

        Assert.False(session.IsSignedIn);
        Assert.False(session.HasManualSession);
        Assert.Null(service.GetManualSessionCookies());
    }

    [Fact]
    public void SettingManualCookiesMarksSessionActiveWithoutExposingContent()
    {
        var service = new InMemorySessionService();

        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var session = service.GetCurrentSession();

        Assert.True(session.IsSignedIn);
        Assert.True(session.HasManualSession);
        Assert.Equal("Manual YouTube session", session.DisplayName);
        Assert.Equal(SessionCookieFormat.NetscapeCookiesText, session.CookieFormat);
        Assert.DoesNotContain("fake-session-value", session.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingSessionRemovesManualCookies()
    {
        var service = new InMemorySessionService();
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        service.ClearSession();

        var session = service.GetCurrentSession();
        Assert.False(session.IsSignedIn);
        Assert.False(session.HasManualSession);
        Assert.Null(service.GetManualSessionCookies());
    }

    [Fact]
    public void CookieProviderCreatesTempFileWithExpectedContent()
    {
        using var tempRoot = new TemporaryDirectory();
        var service = new InMemorySessionService();
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var provider = new TemporaryCookieFileProvider(service, tempRoot.Path);

        using var lease = provider.CreateCookieFile();

        Assert.NotNull(lease);
        Assert.StartsWith(tempRoot.Path, lease.Path, StringComparison.Ordinal);
        Assert.Equal(FakeCookieContent, File.ReadAllText(lease.Path));
    }

    [Fact]
    public void CookieProviderDoesNotCreateFileWithoutSession()
    {
        using var tempRoot = new TemporaryDirectory();
        var service = new InMemorySessionService();
        var provider = new TemporaryCookieFileProvider(service, tempRoot.Path);

        var lease = provider.CreateCookieFile();

        Assert.Null(lease);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tempRoot.Path));
    }

    [Fact]
    public void CookieProviderCleanupRemovesTempFileAndDirectory()
    {
        using var tempRoot = new TemporaryDirectory();
        var service = new InMemorySessionService();
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var provider = new TemporaryCookieFileProvider(service, tempRoot.Path);
        var lease = provider.CreateCookieFile();
        Assert.NotNull(lease);
        var cookiePath = lease.Path;
        var directoryPath = Directory.GetParent(cookiePath)?.FullName;

        lease.Dispose();

        Assert.False(File.Exists(cookiePath));
        Assert.NotNull(directoryPath);
        Assert.False(Directory.Exists(directoryPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"silverscreen-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}