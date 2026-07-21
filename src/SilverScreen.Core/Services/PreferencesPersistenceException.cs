namespace SilverScreen.Core.Services;

public sealed class PreferencesPersistenceException : IOException
{
    public PreferencesPersistenceException(string filePath, Exception innerException)
        : base($"Failed to save preferences to '{filePath}'.", innerException)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}
