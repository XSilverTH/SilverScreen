using System.Text;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Infrastructure.Account.Session;

namespace SilverScreen.Tests.Account.Session;

public sealed class SessionTests
{
    private const string FakeCookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tfake-session-value\n";


    [Fact]
    public void SettingManualCookiesMarksSessionActiveWithoutExposingContent()
    {
        var service = new InMemorySessionService();

        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var session = service.GetCurrentSession();

        Assert.True(session.IsSignedIn);
        Assert.True(session.HasManualSession);
        Assert.Equal("YouTube session", session.DisplayName);
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
    public void SecretServiceSessionPersistsAcrossRestartAndClearsStoredCookies()
    {
        var store = new FakeCookieSecretStore();
        var firstService = new SecretServiceSessionService(store);
        Assert.True(firstService.IsAvailable);
        var setEvents = 0;
        firstService.SessionChanged += (_, _) =>
        {
            Assert.Equal(FakeCookieContent, firstService.GetManualSessionCookies()?.Content);
            setEvents++;
        };

        firstService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        Assert.Equal(1, setEvents);
        Assert.Equal(FakeCookieContent, store.StoredContent);
        var restartedService = new SecretServiceSessionService(store);
        Assert.True(restartedService.GetCurrentSession().IsSignedIn);
        Assert.Equal(SessionCookieFormat.NetscapeCookiesText, restartedService.GetCurrentSession().CookieFormat);
        Assert.Equal(FakeCookieContent, restartedService.GetManualSessionCookies()?.Content);
        var clearEvents = 0;
        restartedService.SessionChanged += (_, _) =>
        {
            Assert.Null(restartedService.GetManualSessionCookies());
            clearEvents++;
        };

        restartedService.ClearSession();

        Assert.Equal(1, clearEvents);
        Assert.Null(store.StoredContent);
        var clearedService = new SecretServiceSessionService(store);
        Assert.False(clearedService.GetCurrentSession().IsSignedIn);
    }

    [Fact]
    public void SecretServiceSessionPreservesActiveCookiesWhenPersistenceFails()
    {
        var store = new FakeCookieSecretStore();
        var service = new SecretServiceSessionService(store);
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var changes = 0;
        service.SessionChanged += (_, _) => changes++;

        store.FailSave = true;
        Assert.Throws<SessionPersistenceException>(() =>
            service.SetManualSession("replacement", SessionCookieFormat.NetscapeCookiesText));
        Assert.False(service.IsAvailable);
        Assert.Equal(FakeCookieContent, service.GetManualSessionCookies()?.Content);
        Assert.Equal(FakeCookieContent, store.StoredContent);
        Assert.Equal(0, changes);

        store.FailSave = false;
        store.FailDelete = true;
        Assert.Throws<SessionPersistenceException>(service.ClearSession);
        Assert.False(service.IsAvailable);
        Assert.Equal(FakeCookieContent, service.GetManualSessionCookies()?.Content);
        Assert.Equal(FakeCookieContent, store.StoredContent);
        Assert.Equal(0, changes);

        store.FailDelete = false;
        service.ClearSession();
        Assert.True(service.IsAvailable);
        Assert.False(service.GetCurrentSession().IsSignedIn);
        Assert.Null(store.StoredContent);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SecretServiceSessionRecoversAfterStartupKeyringFailure()
    {
        var store = new FakeCookieSecretStore { FailLoad = true };
        var service = new SecretServiceSessionService(store);

        Assert.False(service.GetCurrentSession().IsSignedIn);
        Assert.False(service.IsAvailable);

        store.FailLoad = false;
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        Assert.True(service.IsAvailable);

        Assert.True(service.GetCurrentSession().IsSignedIn);
        Assert.Equal(FakeCookieContent, store.StoredContent);
    }

    [Fact]
    public void SessionPersistenceException_PreservesInnerExceptionAndMessage()
    {
        var inner = new InvalidOperationException("keyring locked");
        var exceptionWithInner = new SessionPersistenceException(inner);
        Assert.Same(inner, exceptionWithInner.InnerException);
        Assert.Equal(RuntimeDependencyGuidance.SecretServiceUnavailable, exceptionWithInner.Message);

        var customMessageException = new SessionPersistenceException("custom message", inner);
        Assert.Same(inner, customMessageException.InnerException);
        Assert.Equal("custom message", customMessageException.Message);
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

        if (!OperatingSystem.IsLinux()) return;
        var directoryPath = Directory.GetParent(lease.Path)?.FullName;
        Assert.NotNull(directoryPath);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(directoryPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(lease.Path));
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

    [Fact]
    public void SessionService_DirectlyAcquiresCookieFileLease()
    {
        using var tempRoot = new TemporaryDirectory();
        var service = new InMemorySessionService(tempRoot.Path);
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        using var lease = service.AcquireCookieFileLease();

        Assert.NotNull(lease);
        Assert.StartsWith(tempRoot.Path, lease.Path, StringComparison.Ordinal);
        Assert.Equal(FakeCookieContent, File.ReadAllText(lease.Path));
    }

    [Fact]
    public void SessionService_CreatesCookieContainerFromManualSession()
    {
        var service = new InMemorySessionService();
        Assert.Null(service.CreateCookieContainer());

        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var container = service.CreateCookieContainer();

        Assert.NotNull(container);
        var uri = new Uri("https://www.youtube.com");
        var cookies = container.GetCookies(uri);
        Assert.NotEmpty(cookies);
        Assert.Equal("fake-session-value", cookies["SID"]?.Value);
    }


    private sealed class FakeCookieSecretStore : ICookieSecretStore
    {
        private byte[]? _stored;

        public bool FailLoad { get; set; }
        public bool FailSave { get; set; }
        public bool FailDelete { get; set; }

        public string? StoredContent => _stored is null ? null : Encoding.UTF8.GetString(_stored);

        public byte[]? Load()
        {
            return FailLoad
                ? throw new SessionPersistenceException()
                : _stored?.ToArray();
        }

        public void Save(byte[] secret)
        {
            if (FailSave) throw new SessionPersistenceException();

            _stored = [.. secret];
        }

        public void Delete()
        {
            if (FailDelete) throw new SessionPersistenceException();

            _stored = null;
        }
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
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}