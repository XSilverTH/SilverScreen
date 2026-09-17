using System.Text;
using SilverScreen.Core.Account.Profile;

using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Infrastructure.Account.Session;

namespace SilverScreen.Tests.Account.Session;

public sealed class SessionTests
{
    private const string FakeCookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tfake-session-value\n" +
        ".youtube.com\tTRUE\t/\tTRUE\t2147483647\tSAPISID\tfake-sapisid-value\n";


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
    public async Task SecretServiceSessionPersistsAcrossRestartAndClearsStoredCookies()
    {
        var store = new FakeCookieSecretStore();
        var firstService = new SecretServiceSessionService(store);
        Assert.True(firstService.IsAvailable);
        var setEvents = 0;
        firstService.SessionChanged += (_, _) =>
        {
            if (firstService.GetManualSessionCookies() is not null)
                setEvents++;
        };

        firstService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        Assert.Equal(1, setEvents);
        Assert.Equal(FakeCookieContent, store.StoredContent);
        var restartedService = new SecretServiceSessionService(store);
        await WaitForSessionAsync(restartedService, signedIn: true);
        Assert.True(restartedService.GetCurrentSession().IsSignedIn);
        Assert.Equal(SessionCookieFormat.NetscapeCookiesText, restartedService.GetCurrentSession().CookieFormat);
        Assert.Equal(FakeCookieContent, restartedService.GetManualSessionCookies()?.Content);
        var clearEvents = 0;
        restartedService.SessionChanged += (_, _) =>
        {
            if (restartedService.GetManualSessionCookies() is null)
                clearEvents++;
        };

        restartedService.ClearSession();

        Assert.Equal(1, clearEvents);
        Assert.Null(store.StoredContent);
        var clearedService = new SecretServiceSessionService(store);
        await WaitForSessionAsync(clearedService, signedIn: false);
        Assert.False(clearedService.GetCurrentSession().IsSignedIn);
    }

    [Fact]
    public void SecretServiceSessionIgnoresStoredCookiesWithoutAuthentication()
    {
        var store = new FakeCookieSecretStore();
        store.Save(Encoding.UTF8.GetBytes(
            "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsession-only\n"));

        var service = new SecretServiceSessionService(store);

        Assert.True(service.IsAvailable);
        Assert.False(service.GetCurrentSession().IsSignedIn);
        Assert.Null(service.GetManualSessionCookies());
    }

    [Fact]
    public void SecretServiceSessionPreservesActiveCookiesWhenSaveFails()
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
    }

    [Fact]
    public async Task SecretServiceSessionSignOutIntentSurvivesKeyringDeletionFailureAndRestart()
    {
        using var tempRoot = new TemporaryDirectory();
        var store = new FakeCookieSecretStore();
        var service = new SecretServiceSessionService(store, tempRoot.Path);
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var changes = 0;
        service.SessionChanged += (_, _) => changes++;

        store.FailDelete = true;
        service.ClearSession();

        Assert.False(service.IsAvailable);
        Assert.Null(service.GetManualSessionCookies());
        Assert.False(service.GetCurrentSession().IsSignedIn);
        Assert.Equal(FakeCookieContent, store.StoredContent);
        Assert.Equal(1, changes);

        var restartedService = new SecretServiceSessionService(store, tempRoot.Path);
        await restartedService.WaitForRestoreAsync();
        Assert.False(restartedService.GetCurrentSession().IsSignedIn);
        Assert.Null(restartedService.GetManualSessionCookies());

        store.FailDelete = false;
        restartedService.ClearSession();
        Assert.Null(store.StoredContent);

        // A successful retry clears the intent, so a later startup remains signed out.
        var clearedService = new SecretServiceSessionService(store, tempRoot.Path);
        await clearedService.WaitForRestoreAsync();
        Assert.False(clearedService.GetCurrentSession().IsSignedIn);
    }

    [Fact]
    public async Task SecretServiceSessionValidation_UsesLightweightProfileCheck()
    {
        var store = new FakeCookieSecretStore();
        var profileCalls = 0;
        var profile = new FakeProfileService(() =>
        {
            profileCalls++;
            return Task.FromResult<AccountProfile?>(new AccountProfile("Test account"));
        });
        var service = new SecretServiceSessionService(() => profile, tempRoot: null);
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var result = await service.ValidateSessionAsync();

        Assert.Contains("Validation succeeded.", result);
        Assert.Contains("Usable videos: 0", result);
        Assert.Equal(1, profileCalls);
    }
    [Fact]
    public async Task SecretServiceSessionRecoversAfterStartupKeyringFailure()
    {
        var store = new FakeCookieSecretStore { FailLoad = true };
        var service = new SecretServiceSessionService(store);

        await WaitForAvailabilityAsync(service, available: false);
        Assert.False(service.GetCurrentSession().IsSignedIn);
        Assert.False(service.IsAvailable);

        store.FailLoad = false;
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        Assert.True(service.IsAvailable);

        Assert.True(service.GetCurrentSession().IsSignedIn);
        Assert.Equal(FakeCookieContent, store.StoredContent);
    }
    [Fact]
    public async Task SecretServiceSession_RestoresWithoutBlockingConstruction()
    {
        var store = new ControllableCookieSecretStore();
        var service = new SecretServiceSessionService(store);
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SessionChanged += (_, _) =>
        {
            if (service.GetCurrentSession().IsSignedIn)
                restored.TrySetResult();
        };

        Assert.False(service.GetCurrentSession().IsSignedIn);
        Assert.True(store.LoadStarted.Wait(TimeSpan.FromSeconds(5)));

        store.ReleaseLoad(Encoding.UTF8.GetBytes(FakeCookieContent));
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(service.GetCurrentSession().IsSignedIn);
        Assert.Equal(FakeCookieContent, service.GetManualSessionCookies()?.Content);
    }


[Fact]
    public void SessionService_CreatesTempFileWithExpectedContent()
    {
        using var tempRoot = new TemporaryDirectory();
        var service = new InMemorySessionService(tempRoot.Path);
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        using var lease = service.CreateCookieFile();

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
    public void SessionService_CleanupRemovesTempFileAndDirectory()
    {
        using var tempRoot = new TemporaryDirectory();
        var service = new InMemorySessionService(tempRoot.Path);
        service.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var lease = service.CreateCookieFile();
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

    [Fact]
    public void TemporaryCookieFile_GetDefaultTempRoot_PrefersXdgRuntimeDir()
    {
        var root = TemporaryCookieFile.GetDefaultTempRoot();
        Assert.False(string.IsNullOrWhiteSpace(root));
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrWhiteSpace(xdg) && Directory.Exists(xdg))
            {
                Assert.Equal(xdg, root);
            }
        }
    }

    [Fact]
    public void TemporaryCookieFile_CreateLease_AndDispose_CleansUpFileAndDirectory()
    {
        using var tempRoot = new TemporaryDirectory();
        var lease = TemporaryCookieFile.CreateLease(FakeCookieContent, tempRoot.Path);
        Assert.NotNull(lease);
        Assert.True(File.Exists(lease.Path));
        var directory = Path.GetDirectoryName(lease.Path);
        Assert.NotNull(directory);
        Assert.True(Directory.Exists(directory));

        lease.Dispose();
        Assert.False(File.Exists(lease.Path));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void TemporaryCookieFile_SweepStale_RemovesEntriesOlderThanOneHour()
    {
        using var tempRoot = new TemporaryDirectory();
        var oldDir = Path.Combine(tempRoot.Path, $"{TemporaryCookieFile.DirectoryPrefix}old");
        var newDir = Path.Combine(tempRoot.Path, $"{TemporaryCookieFile.DirectoryPrefix}new");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);
        File.WriteAllText(Path.Combine(oldDir, "cookies.txt"), "old-cookies");
        File.WriteAllText(Path.Combine(newDir, "cookies.txt"), "new-cookies");

        // Set oldDir write time to 2 hours ago
        Directory.SetLastWriteTimeUtc(oldDir, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(Path.Combine(oldDir, "cookies.txt"), DateTime.UtcNow.AddHours(-2));

        // Sweep with default stale age (1 hour)
        TemporaryCookieFile.SweepStale(tempRoot: tempRoot.Path);

        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public void TemporaryCookieFile_SweepStale_PurgesOrphanedDirectoryWithDeadPidImmediately()
    {
        using var tempRoot = new TemporaryDirectory();
        var deadPid = FindDeadPid();
        var deadDir = Path.Combine(tempRoot.Path, $"{TemporaryCookieFile.DirectoryPrefix}{deadPid}-orphaned");
        Directory.CreateDirectory(deadDir);
        File.WriteAllText(Path.Combine(deadDir, "cookies.txt"), "dead-session-cookies");

        using var activeLease = TemporaryCookieFile.CreateLease(FakeCookieContent, tempRoot.Path);
        Assert.NotNull(activeLease);
        var activeDir = Path.GetDirectoryName(activeLease.Path);
        Assert.NotNull(activeDir);
        Assert.True(Directory.Exists(activeDir));

        // Sweep without waiting for 1 hour
        TemporaryCookieFile.SweepStale(tempRoot: tempRoot.Path);

        Assert.False(Directory.Exists(deadDir));
        Assert.True(Directory.Exists(activeDir));
    }

    [Fact]
    public void TemporaryCookieFile_SweepStale_PurgesUntrackedDirectoryMatchingCurrentPid()
    {
        using var tempRoot = new TemporaryDirectory();
        var untrackedDir = Path.Combine(tempRoot.Path, $"{TemporaryCookieFile.DirectoryPrefix}{Environment.ProcessId}-untracked");
        Directory.CreateDirectory(untrackedDir);
        File.WriteAllText(Path.Combine(untrackedDir, "cookies.txt"), "untracked-session-cookies");

        // Untracked lease matching current PID is purged on sweep (e.g. startup cleanup)
        TemporaryCookieFile.SweepStale(tempRoot: tempRoot.Path);

        Assert.False(Directory.Exists(untrackedDir));
    }

    private static async Task WaitForSessionAsync(SecretServiceSessionService service, bool signedIn)
    {
        if (service.GetCurrentSession().IsSignedIn == signedIn)
            return;

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? _, EventArgs __)
        {
            if (service.GetCurrentSession().IsSignedIn == signedIn)
                changed.TrySetResult();
        }

        service.SessionChanged += OnChanged;
        try
        {
            if (service.GetCurrentSession().IsSignedIn != signedIn)
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            service.SessionChanged -= OnChanged;
        }
    }

    private static async Task WaitForAvailabilityAsync(SecretServiceSessionService service, bool available)
    {
        if (service.IsAvailable == available)
            return;

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? _, EventArgs __)
        {
            if (service.IsAvailable == available)
                changed.TrySetResult();
        }

        service.SessionChanged += OnChanged;
        try
        {
            if (service.IsAvailable != available)
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            service.SessionChanged -= OnChanged;
        }
    }

    private static int FindDeadPid()
    {
        for (var pid = 999999; pid > 100000; pid--)
        {
            try
            {
                System.Diagnostics.Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return pid;
            }
        }

        return 999999;
    }

    [Fact]
    public async Task CookieSecretStore_AsyncOperations_SucceedAndPersist()
    {
        ICookieSecretStore store = new FakeCookieSecretStore();
        var secret = Encoding.UTF8.GetBytes("secret-token");

        await store.SaveAsync(secret);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("secret-token", Encoding.UTF8.GetString(loaded));

        await store.DeleteAsync();
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public void LibSecretCookieStore_InstantiatesLazilyWithoutThrowing()
    {
        ICookieSecretStore store = new LibSecretCookieStore();
        Assert.NotNull(store);
    }


    private sealed class FakeProfileService(
        Func<Task<AccountProfile?>> getProfile) : IAccountProfileService
    {
        public AccountProfile? GetCachedProfile() => null;

        public Task<AccountProfile?> GetCurrentProfileAsync(
            CancellationToken cancellationToken = default) => getProfile();
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
    private sealed class ControllableCookieSecretStore : ICookieSecretStore
    {
        private readonly TaskCompletionSource<byte[]?> _load =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim LoadStarted { get; } = new();

        public byte[]? Load() => throw new InvalidOperationException("Synchronous load was invoked.");

        public Task<byte[]?> LoadAsync()
        {
            LoadStarted.Set();
            return _load.Task;
        }

        public void Save(byte[] secret)
        {
        }

        public void Delete()
        {
        }

        public void ReleaseLoad(byte[]? secret) => _load.TrySetResult(secret);
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