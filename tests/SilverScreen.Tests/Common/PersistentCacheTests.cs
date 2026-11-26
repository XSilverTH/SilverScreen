using SilverScreen.Account.Profile;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Account.Session;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.Common;

public sealed class PersistentCacheTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"SilverScreen_PersistentCacheTests_{Guid.NewGuid():N}");

    public PersistentCacheTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task AccountViewModel_SeedsCachedProfileImmediately_WhileRefreshingInBackground()
    {
        var session = new InMemorySessionService();
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        session.SetManualSession($".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSAPISID\tauth_val\n",
            SessionCookieFormat.NetscapeCookiesText);

        var networkTcs = new TaskCompletionSource<AccountProfileResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profileService = new CachedThenAsyncProfileService(
            new AccountProfile("Cached User", "https://example.com/avatar-cached.jpg"),
            networkTcs.Task);

        using var vm = new AccountViewModel(profileService, session);

        // Immediately available from cache without waiting for the network task.
        Assert.Equal(AccountProfilePresentationStatus.Ready, vm.ProfileStatus);
        Assert.Equal("Cached User", vm.DisplayName);
        Assert.Equal("https://example.com/avatar-cached.jpg", vm.AvatarUrl);

        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.StateChanged += (_, _) => updated.TrySetResult();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountViewModel.DisplayName) && vm.DisplayName == "Fresh User")
                updated.TrySetResult();
        };

        networkTcs.SetResult(new AccountProfileResult.Success(
            new AccountProfile("Fresh User", "https://example.com/avatar-fresh.jpg")));
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(AccountProfilePresentationStatus.Ready, vm.ProfileStatus);
        Assert.Equal("Fresh User", vm.DisplayName);
        Assert.Equal("https://example.com/avatar-fresh.jpg", vm.AvatarUrl);
    }

    [Fact]
    public void AccountProfileCache_PersistsAcrossInstances_AndClearsOnSignOut()
    {
        var cachePath = Path.Combine(_tempDir, "account-profile.json");
        var session = new InMemorySessionService();
        session.SetManualSession("cookies-a", SessionCookieFormat.NetscapeCookiesText);
        var clientProvider = new YouTubeClientProvider(session, Path.Combine(_tempDir, "bootstrap.json"));

        var sessionHash = PersistentCacheFile.TryGetActiveSessionHash(session)!;
        PersistentCacheFile.WriteAtomically(
            cachePath,
            new PersistedAccountProfile(sessionHash, "Alice", "https://example.com/a.jpg", DateTimeOffset.UtcNow),
            PersistentCacheJsonContext.Default.PersistedAccountProfile);

        using var service = new YoutubeApiAccountProfileService(clientProvider, session, cachePath);
        var cached = service.TryGetCachedProfile();
        Assert.NotNull(cached);
        Assert.Equal("Alice", cached.DisplayName);
        Assert.Equal("https://example.com/a.jpg", cached.AvatarUrl);

        session.ClearSession();
        Assert.Null(service.TryGetCachedProfile());
        Assert.False(File.Exists(cachePath));
    }

    private sealed class CachedThenAsyncProfileService(
        AccountProfile? cachedProfile,
        Task<AccountProfileResult> networkTask) : IAccountProfileService
    {
        public AccountProfile? TryGetCachedProfile() => cachedProfile;

        public Task<AccountProfileResult> GetCurrentProfileAsync(CancellationToken cancellationToken = default) =>
            networkTask;
    }
}
