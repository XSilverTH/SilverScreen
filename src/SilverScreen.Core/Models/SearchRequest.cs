namespace SilverScreen.Core.Models;

public sealed record SearchRequest(string Query, int StartIndex = 1);