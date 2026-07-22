namespace SilverScreen.Core.Services;

public sealed class PreferencesPersistenceException(string filePath, Exception innerException)
    : IOException($"Failed to save preferences to '{filePath}'.", innerException)
{
    public string FilePath { get; } = filePath;
}