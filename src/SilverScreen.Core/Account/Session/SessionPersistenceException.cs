using SilverScreen.Core.Common;

namespace SilverScreen.Core.Account.Session;

public sealed class SessionPersistenceException : Exception
{
    public SessionPersistenceException()
        : base(RuntimeDependencyGuidance.SecretServiceUnavailable)
    {
    }

    public SessionPersistenceException(string? message)
        : base(message ?? RuntimeDependencyGuidance.SecretServiceUnavailable)
    {
    }

    public SessionPersistenceException(Exception? innerException)
        : base(RuntimeDependencyGuidance.SecretServiceUnavailable, innerException)
    {
    }

    public SessionPersistenceException(string? message, Exception? innerException)
        : base(message ?? RuntimeDependencyGuidance.SecretServiceUnavailable, innerException)
    {
    }
}