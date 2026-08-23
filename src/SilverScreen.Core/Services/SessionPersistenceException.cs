namespace SilverScreen.Core.Services;

public sealed class SessionPersistenceException : Exception
{
    public SessionPersistenceException()
        : base(RuntimeDependencyGuidance.SecretServiceUnavailable)
    {
    }

    public SessionPersistenceException(string? message, Exception? innerException)
        : base(message ?? RuntimeDependencyGuidance.SecretServiceUnavailable, innerException)
    {
    }

    public SessionPersistenceException(Exception? innerException)
        : base(RuntimeDependencyGuidance.SecretServiceUnavailable, innerException)
    {
    }
}