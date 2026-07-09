using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface ISessionService
{
    AccountSession GetCurrentSession();

    void ClearSession();
}
