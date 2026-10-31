using SilverScreen.Account.Profile;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Account.Session;
using SilverScreen.Infrastructure.Account.Session;

namespace SilverScreen.Tests.Account.Profile;

public sealed class AccountViewModelTests
{
    private readonly FakeProfileService _profileService = new();
    private readonly InMemorySessionService _sessionService = new();

    [Fact]
    public async Task TemporaryProfileFailureKeepsCredentialsAndRetryCanRestoreHealthyProfile()
    {
        _profileService.Results.Enqueue(Task.FromResult(
            new AccountProfileResult(AccountProfileLoadStatus.TemporaryFailure)));
        _profileService.Results.Enqueue(Task.FromResult(
            new AccountProfileResult(AccountProfileLoadStatus.Success, new AccountProfile("Verified account"))));
        using var vm = new AccountViewModel(_profileService, _sessionService);
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var validCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSAPISID\tauth_sapisid_value\n";

        Assert.True(await vm.SaveManualSessionAsync(validCookies));
        Assert.True(vm.HasManualSession);
        Assert.Equal(AccountProfilePresentationStatus.Unavailable, vm.ProfileStatus);

        vm.RefreshProfile();

        Assert.True(vm.HasManualSession);
        Assert.Equal(AccountProfilePresentationStatus.Ready, vm.ProfileStatus);
        Assert.Equal("Verified account", vm.DisplayName);
    }

    [Fact]
    public async Task AuthenticationRejectionShowsRejectedStateWithoutClearingCredentials()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var validCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSAPISID\tauth_sapisid_value\n";

        Assert.True(await vm.SaveManualSessionAsync(validCookies));

        Assert.Equal(AccountProfilePresentationStatus.Rejected, vm.ProfileStatus);
        Assert.True(vm.HasManualSession);
    }

    [Fact]
    public async Task UncanceledProfileRequestTimeoutMovesCheckingToUnavailable()
    {
        var profileRequest = new TaskCompletionSource<AccountProfileResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _profileService.Results.Enqueue(profileRequest.Task);
        using var vm = new AccountViewModel(_profileService, _sessionService);
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var validCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSAPISID\tauth_sapisid_value\n";

        Assert.True(await vm.SaveManualSessionAsync(validCookies));
        Assert.Equal(AccountProfilePresentationStatus.Checking, vm.ProfileStatus);

        var unavailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.StateChanged += (_, _) =>
        {
            if (vm.ProfileStatus == AccountProfilePresentationStatus.Unavailable)
                unavailable.TrySetResult();
        };
        profileRequest.SetException(new TaskCanceledException("Profile request timed out."));
        await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vm.HasManualSession);
        Assert.Equal(AccountProfilePresentationStatus.Unavailable, vm.ProfileStatus);
    }

    [Fact]
    public async Task SaveManualSession_WithEmptyOrWhitespace_ReturnsFalseAndSetsError()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);

        var result = await vm.SaveManualSessionAsync("   ");

        Assert.False(result);
        Assert.Equal("Nothing to save — paste the contents of your cookies.txt file first.", vm.ManualSessionError);
    }

    [Fact]
    public async Task SaveManualSession_WithMalformedCookies_ReturnsFalseAndSetsError()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);

        var result = await vm.SaveManualSessionAsync("malformed cookie data without tabs or valid structure");

        Assert.False(result);
        Assert.Equal(
            "That doesn't look like a cookies.txt file with valid YouTube login credentials — export Netscape-format cookies while logged into YouTube and paste the whole file contents.",
            vm.ManualSessionError);
    }

    [Fact]
    public async Task SaveManualSession_WithoutAuthenticationCookies_ReturnsFalseAndSetsError()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var nonAuthCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSID\tsome_sid_value\n";

        var result = await vm.SaveManualSessionAsync(nonAuthCookies);

        Assert.False(result);
        Assert.Equal(
            "That doesn't look like a cookies.txt file with valid YouTube login credentials — export Netscape-format cookies while logged into YouTube and paste the whole file contents.",
            vm.ManualSessionError);
    }

    [Fact]
    public async Task SaveManualSession_WithSapisid_ReturnsTrueAndClearsError()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var validCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSAPISID\tauth_sapisid_value\n";

        var result = await vm.SaveManualSessionAsync(validCookies);

        Assert.True(result);
        Assert.Null(vm.ManualSessionError);
        Assert.True(vm.HasManualSession);
    }

    [Fact]
    public async Task ClearSessionAsync_ClearsActiveSession()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var validCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSAPISID\tauth_sapisid_value\n";

        Assert.True(await vm.SaveManualSessionAsync(validCookies));
        Assert.True(vm.HasManualSession);

        await vm.ClearSessionAsync();

        Assert.False(vm.HasManualSession);
    }

    private sealed class FakeProfileService : IAccountProfileService
    {
        public Queue<Task<AccountProfileResult>> Results { get; } = new();

        public AccountProfile? GetCachedProfile() => null;

        public Task<AccountProfileResult> GetCurrentProfileAsync(CancellationToken cancellationToken = default) =>
            Results.Count > 0
                ? Results.Dequeue()
                : Task.FromResult(new AccountProfileResult(AccountProfileLoadStatus.AuthenticationRequired));
    }
}
