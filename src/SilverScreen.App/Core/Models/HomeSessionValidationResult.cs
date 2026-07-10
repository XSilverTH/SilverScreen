namespace SilverScreen.Core.Models;

public sealed record HomeSessionValidationResult(
    bool IsSuccess,
    int VideoCount,
    bool HasContinuation,
    bool RequiresAuthentication,
    AuthenticatedHomeFeedStatus HighLevelStatus,
    string StatusMessage)
{
    public override string ToString()
    {
        return
            $"IsSuccess: {IsSuccess}, VideoCount: {VideoCount}, HasContinuation: {HasContinuation}, RequiresAuthentication: {RequiresAuthentication}, HighLevelStatus: {HighLevelStatus}, StatusMessage: {StatusMessage}";
    }
}