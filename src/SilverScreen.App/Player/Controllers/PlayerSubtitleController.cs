using Gtk;
using Serilog;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;

namespace SilverScreen.Player.Controllers;

internal sealed class PlayerSubtitleController(
    IPreferencesService preferences,
    DropDown dropdown,
    StringList model,
    Button button,
    Action<long> selectTrack)
    : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<PlayerSubtitleController>();
    private bool _disposed;
    private bool _suppressSelectionChanged;
    private IReadOnlyList<LibMpvSubtitleTrack> _tracks = [];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    public void UpdateTracks(IReadOnlyList<LibMpvSubtitleTrack> tracks, bool suppressSelectionChanged)
    {
        if (_disposed) return;
        if (_tracks.SequenceEqual(tracks))
        {
            UpdateButton();
            return;
        }

        _tracks = tracks;
        while (model.GetNItems() > 0) model.Remove(0);
        model.Append("Off");
        uint selected = 0;
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            model.Append(track.Label);
            if (track.IsSelected) selected = (uint)index + 1;
        }

        _suppressSelectionChanged = suppressSelectionChanged;
        try
        {
            dropdown.SetSelected(selected);
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        UpdateButton();
    }

    public void OnSelectionChanged()
    {
        if (_disposed || _suppressSelectionChanged) return;

        var selected = dropdown.GetSelected();
        if (selected is 0 or > int.MaxValue || selected > _tracks.Count)
        {
            selectTrack(0);
            return;
        }

        var track = _tracks[(int)selected - 1];
        selectTrack(track.Id);
        SavePreferredSubtitle(track.Language);
    }

    public void ShowPreferredSubtitle()
    {
        if (_disposed) return;

        var preferredLanguage = preferences.GetPreferences().PreferredSubtitleLanguage;
        var track = _tracks.FirstOrDefault(track =>
            SubtitleLanguageMatches(track.Language, preferredLanguage));
        if (track is not null) selectTrack(track.IsSelected ? 0 : track.Id);
    }

    private static bool SubtitleLanguageMatches(string language, string preferredLanguage)
    {
        if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(preferredLanguage)) return false;
        if (string.Equals(language, preferredLanguage, StringComparison.OrdinalIgnoreCase)) return true;

        var languageSeparator = language.IndexOf('-');
        var preferredLanguageSeparator = preferredLanguage.IndexOf('-');
        return language.AsSpan(0, languageSeparator < 0 ? language.Length : languageSeparator)
            .Equals(preferredLanguage.AsSpan(0,
                    preferredLanguageSeparator < 0 ? preferredLanguage.Length : preferredLanguageSeparator),
                StringComparison.OrdinalIgnoreCase);
    }

    private void SavePreferredSubtitle(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return;
        var preferences1 = preferences.GetPreferences();
        if (string.Equals(preferences1.PreferredSubtitleLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            UpdateButton();
            return;
        }

        var updated = preferences1 with { PreferredSubtitleLanguage = language };
        try
        {
            preferences.SavePreferences(updated);
            UpdateButton();
        }
        catch (PreferencesPersistenceException exception)
        {
            Logger.Warning(exception, "Could not save preferred subtitle language");
        }
    }

    private void UpdateButton()
    {
        var preferredLanguage = preferences.GetPreferences().PreferredSubtitleLanguage;
        var track = _tracks.FirstOrDefault(track =>
            SubtitleLanguageMatches(track.Language, preferredLanguage));
        button.SetSensitive(track is not null);
        button.SetTooltipText(track is null
            ? "Choose a subtitle in player settings to set your preference"
            : track.IsSelected
                ? "Turn off preferred subtitles (C)"
                : $"Use preferred subtitles: {preferredLanguage} (C)");
    }
}