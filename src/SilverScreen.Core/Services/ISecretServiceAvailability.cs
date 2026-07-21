namespace SilverScreen.Core.Services;

/// <summary>Reports whether the desktop Secret Service could be reached.</summary>
public interface ISecretServiceAvailability
{
    bool IsAvailable { get; }
}
