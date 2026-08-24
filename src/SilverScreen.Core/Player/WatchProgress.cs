namespace SilverScreen.Core.Player;

/// <summary>Represents the furthest point reached in a video's known duration.</summary>
public sealed record WatchProgress(string VideoId, double Fraction);