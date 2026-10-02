namespace SilverScreen.Core.Account.Profile;

public enum AccountProfileLoadStatus
{
    Success,
    AuthenticationRequired,
    AuthenticationRejected,
    TemporaryFailure
}

public sealed record AccountProfileResult(AccountProfileLoadStatus Status, AccountProfile? Profile = null);
