using System.Diagnostics.CodeAnalysis;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Player.Controllers;

/// <summary>
///     Pure SponsorBlock evaluation and helper logic: segment lookup, auto-skip evaluation,
///     manual prompt evaluation, preference inspection, category labels, styling classes, and config keys.
/// </summary>
public static class PlayerSponsorBlockState
{
    public const uint DefaultSkipPromptDurationMilliseconds = 5_000;

    public static SponsorBlockSegment? FindSponsorBlockSegmentAt(
        IReadOnlyList<SponsorBlockSegment> segments,
        TimeSpan position)
    {
        return segments.FirstOrDefault(segment => position >= segment.Start && position < segment.End);
    }

    public static bool ShouldAutoSkip(
        TimeSpan currentPosition,
        IReadOnlyList<SponsorBlockSegment> segments,
        bool isPaused,
        bool autoSkipEnabled,
        ISet<string> autoSkippedSegmentIds,
        [NotNullWhen(true)] out SponsorBlockSegment? segmentToSkip)
    {
        segmentToSkip = null;
        if (isPaused || !autoSkipEnabled || segments.Count == 0) return false;
        var segment = FindSponsorBlockSegmentAt(segments, currentPosition);
        if (segment is null || !autoSkippedSegmentIds.Add(segment.Id)) return false;
        segmentToSkip = segment;
        return true;
    }

    public static bool ShouldShowManualPrompt(
        SponsorBlockSegment? activeSegment,
        SponsorBlockSegment? candidateSegment,
        bool isPaused,
        bool wasPaused,
        bool hadSeek)
    {
        if (candidateSegment is null) return false;
        return hadSeek ||
               !string.Equals(activeSegment?.Id, candidateSegment.Id, StringComparison.Ordinal) ||
               (isPaused && !wasPaused);
    }

    public static bool ManualSponsorBlockSkipEnabled(AppPreferences preferences)
    {
        return ManualSponsorBlockSkipEnabled(preferences.SponsorBlockSegmentDisplayEnabled,
            preferences.SponsorBlockAutoSkipEnabled);
    }

    public static bool ManualSponsorBlockSkipEnabled(bool segmentDisplayEnabled, bool autoSkipEnabled)
    {
        return segmentDisplayEnabled && !autoSkipEnabled;
    }

    public static string GetSponsorBlockCategoryLabel(string category)
    {
        return category switch
        {
            SponsorBlockCategories.Sponsor => "Sponsor",
            SponsorBlockCategories.SelfPromotion => "Self-promotion",
            SponsorBlockCategories.InteractionReminder => "Interaction reminder",
            SponsorBlockCategories.Intro => "Intro",
            SponsorBlockCategories.Outro => "Outro",
            SponsorBlockCategories.Preview => "Preview",
            SponsorBlockCategories.Hook => "Hook",
            SponsorBlockCategories.Filler => "Filler",
            _ => category
        };
    }

    public static string GetSponsorBlockButtonColorClass(string category)
    {
        var resolved = SponsorBlockCategories.All.Contains(category) ? category : SponsorBlockCategories.Sponsor;
        return $"player-sponsorblock-skip-button-{resolved}";
    }

    public static string GetSponsorBlockConfigurationKey(AppPreferences preferences)
    {
        return GetSponsorBlockConfigurationKey(
            preferences.SponsorBlockAutoSkipEnabled,
            preferences.SponsorBlockSegmentDisplayEnabled,
            preferences.SponsorBlockCategories);
    }

    public static string GetSponsorBlockConfigurationKey(
        bool autoSkipEnabled,
        bool segmentDisplayEnabled,
        IEnumerable<string> categories)
    {
        if (!autoSkipEnabled && !segmentDisplayEnabled) return "disabled";
        var filtered = categories.Where(SponsorBlockCategories.All.Contains).Distinct(StringComparer.Ordinal);
        return $"{autoSkipEnabled}:{segmentDisplayEnabled}:{string.Join(',', filtered)}";
    }
}