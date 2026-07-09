using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Mock;

public sealed class MockSessionService : ISessionService
{
    public AccountSession GetCurrentSession() => AccountSession.SignedOut;

    public void ClearSession()
    {
        // No-op stub
    }
}
