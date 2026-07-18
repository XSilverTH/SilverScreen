namespace SilverScreen.Core.Services;

public sealed class SessionPersistenceException : Exception
{
    public SessionPersistenceException()
        : base("The system keyring is unavailable.")
    {
    }
}