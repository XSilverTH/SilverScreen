namespace SilverScreen.Core.Account.Profile;

public union AccountProfileResult(
    AccountProfileResult.Success,
    AccountProfileResult.AuthenticationRequired,
    AccountProfileResult.AuthenticationRejected,
    AccountProfileResult.TemporaryFailure)
{
    public sealed record Success(AccountProfile Profile);

    public sealed record AuthenticationRequired;

    public sealed record AuthenticationRejected;

    public sealed record TemporaryFailure;
}