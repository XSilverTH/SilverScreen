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
    public void SaveManualSession_WithEmptyOrWhitespace_ReturnsFalseAndSetsError()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);

        var result = vm.SaveManualSession("   ");

        Assert.False(result);
        Assert.Equal("Nothing to save — paste the contents of your cookies.txt file first.", vm.ManualSessionError);
    }

    [Fact]
    public void SaveManualSession_WithMalformedCookies_ReturnsFalseAndSetsError()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);

        var result = vm.SaveManualSession("malformed cookie data without tabs or valid structure");

        Assert.False(result);
        Assert.Equal(
            "That doesn't look like a cookies.txt file with valid YouTube login credentials — export Netscape-format cookies while logged into YouTube and paste the whole file contents.",
            vm.ManualSessionError);
    }

    [Fact]
    public void SaveManualSession_WithoutAuthenticationCookies_ReturnsFalseAndSetsError()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var nonAuthCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSID\tsome_sid_value\n";

        var result = vm.SaveManualSession(nonAuthCookies);

        Assert.False(result);
        Assert.Equal(
            "That doesn't look like a cookies.txt file with valid YouTube login credentials — export Netscape-format cookies while logged into YouTube and paste the whole file contents.",
            vm.ManualSessionError);
    }

    [Fact]
    public void SaveManualSession_WithSapisid_ReturnsTrueAndClearsError()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var validCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSAPISID\tauth_sapisid_value\n";

        var result = vm.SaveManualSession(validCookies);

        Assert.True(result);
        Assert.Null(vm.ManualSessionError);
        Assert.True(vm.HasManualSession);
    }

    [Fact]
    public void SaveManualSession_WithSecure3Papisid_ReturnsTrueAndClearsError()
    {
        using var vm = new AccountViewModel(_profileService, _sessionService);
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var validCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\t__Secure-3PAPISID\tauth_3papisid_value\n";

        var result = vm.SaveManualSession(validCookies);

        Assert.True(result);
        Assert.Null(vm.ManualSessionError);
        Assert.True(vm.HasManualSession);
    }

    private sealed class FakeProfileService : IAccountProfileService
    {
        public AccountProfile? GetCachedProfile() => null;
        public Task<AccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountProfile?>(null);
    }
}
