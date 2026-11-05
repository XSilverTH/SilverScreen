using System.Security.Cryptography;
using System.Text;
using DBus.Services.Secrets;
using SilverScreen.Core.Account.Session;
using SilverScreen.Infrastructure.Account.Session;

namespace SilverScreen.Tests.Account.Session;

public sealed class DBusCookieSecretStoreTests
{
    [Fact]
    public async Task SaveAsync_WithNullSecret_ThrowsArgumentNullException()
    {
        var store = new DBusCookieSecretStore();
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_BufferDiscipline_ZeroesBufferOnConnectionFailure()
    {
        var store = new DBusCookieSecretStore(() =>
            Task.FromException<SecretService>(new InvalidOperationException("D-Bus connection simulated failure")));

        var buffer = Encoding.UTF8.GetBytes("super-secret-cookie-content");
        Assert.Contains(buffer, b => b != 0);

        var ex = await Assert.ThrowsAsync<SessionPersistenceException>(() => store.SaveAsync(buffer));
        Assert.NotNull(ex);

        // Caller's buffer MUST be zeroed on failure
        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task LoadAsync_WhenConnectionFails_ThrowsSessionPersistenceException()
    {
        var store = new DBusCookieSecretStore(() =>
            Task.FromException<SecretService>(new InvalidOperationException("D-Bus daemon unavailable")));

        var ex = await Assert.ThrowsAsync<SessionPersistenceException>(() => store.LoadAsync());
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task DeleteAsync_WhenConnectionFails_ThrowsSessionPersistenceException()
    {
        var store = new DBusCookieSecretStore(() =>
            Task.FromException<SecretService>(new InvalidOperationException("D-Bus daemon unavailable")));

        var ex = await Assert.ThrowsAsync<SessionPersistenceException>(() => store.DeleteAsync());
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task LiveSecretService_LoadAsync_RecoversSessionIfPresent_AndRespectsBufferDiscipline()
    {
        // Only run live check if D-Bus session bus is available
        var sessionAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        var userRuntimeBus = $"/run/user/{GetUid()}/bus";
        if (string.IsNullOrEmpty(sessionAddress) && !File.Exists(userRuntimeBus))
        {
            return;
        }

        var store = new DBusCookieSecretStore();
        byte[]? loaded = null;
        try
        {
            loaded = await store.LoadAsync();
            if (loaded is not null)
            {
                Assert.True(loaded.Length > 0);
                var content = Encoding.UTF8.GetString(loaded);
                Assert.Contains("youtube.com", content, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (SessionPersistenceException)
        {
            // Allowed if keyring is locked and user cannot be prompted in headless test run
        }
        finally
        {
            if (loaded is not null)
            {
                CryptographicOperations.ZeroMemory(loaded);
            }
        }
    }

    [Fact]
    public async Task SecretServiceSessionService_DefaultConstructor_RestoresWithoutCrashing()
    {
        using var tempDir = new TemporaryDirectory();
        var service = new SecretServiceSessionService(profileServiceFactory: null, tempRoot: tempDir.Path);

        await service.WaitForRestoreAsync();

        // Either available and restored, or available and empty, or unavailable; but MUST NOT throw
        var session = service.GetCurrentSession();
        Assert.NotNull(session);
    }

    private static int GetUid()
    {
        try
        {
            return (int)PInvokeGetUid();
        }
        catch
        {
            return 1000;
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getuid")]
    private static extern uint PInvokeGetUid();

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"silverscreen-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
