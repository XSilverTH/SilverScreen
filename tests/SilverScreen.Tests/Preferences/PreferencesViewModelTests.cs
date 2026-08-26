using SilverScreen.Core.Preferences;
using SilverScreen.Preferences;

namespace SilverScreen.Tests.Preferences;

public sealed class PreferencesViewModelTests
{
    [Fact]
    public void Save_PreservesCurrentSubtitleLanguage()
    {
        var service = new FakePreferencesService(new AppPreferences { PreferredSubtitleLanguage = "ja" });
        var viewModel = new PreferencesViewModel(service);

        var result = viewModel.Save(viewModel.EditorState with { Theme = "Dark" });

        Assert.True(result.Succeeded);
        Assert.NotNull(service.Saved);
        Assert.Equal("ja", service.Saved!.PreferredSubtitleLanguage);
    }

    [Fact]
    public void Save_PersistsShortcutBindings()
    {
        var service = new FakePreferencesService(new AppPreferences());
        var viewModel = new PreferencesViewModel(service);
        var shortcuts = viewModel.EditorState.Shortcuts with { TogglePause = ["Pause"] };

        var result = viewModel.Save(viewModel.EditorState with { Shortcuts = shortcuts });

        Assert.True(result.Succeeded);
        Assert.Equal(["Pause"], service.Saved!.Shortcuts.TogglePause);
        Assert.Equal(["Pause"], result.State.Shortcuts.TogglePause);
    }

    [Fact]
    public void Save_PersistsShortcutOsdEnabled()
    {
        var service = new FakePreferencesService(new AppPreferences());
        var viewModel = new PreferencesViewModel(service);
        Assert.True(viewModel.EditorState.ShortcutOsdEnabled);

        var result = viewModel.Save(viewModel.EditorState with { ShortcutOsdEnabled = false });

        Assert.True(result.Succeeded);
        Assert.False(service.Saved!.ShortcutOsdEnabled);
        Assert.False(result.State.ShortcutOsdEnabled);
    }


    [Fact]
    public void Save_WhenMarkWatchedIsEnabled_DisablesTelemetry()
    {
        var service = new FakePreferencesService(new AppPreferences
        {
            YouTubePlaybackTelemetryEnabled = true
        });
        var viewModel = new PreferencesViewModel(service);

        var result = viewModel.Save(viewModel.EditorState with { MarkWatchedVideos = true },
            PreferencesMutuallyExclusiveOption.MarkWatchedVideos);

        Assert.True(result.Succeeded);
        Assert.True(service.Saved!.MarkWatchedVideos);
        Assert.False(service.Saved.YouTubePlaybackTelemetryEnabled);
    }

    [Fact]
    public void Save_WhenTelemetryIsEnabled_DisablesMarkWatched()
    {
        var service = new FakePreferencesService(new AppPreferences
        {
            MarkWatchedVideos = true
        });
        var viewModel = new PreferencesViewModel(service);

        var result = viewModel.Save(viewModel.EditorState with { YouTubePlaybackTelemetryEnabled = true },
            PreferencesMutuallyExclusiveOption.YouTubePlaybackTelemetry);

        Assert.True(result.Succeeded);
        Assert.True(service.Saved!.YouTubePlaybackTelemetryEnabled);
        Assert.False(service.Saved.MarkWatchedVideos);
    }

    [Fact]
    public void Save_WhenResumeAutomaticallyIsEnabled_DisablesResumeOnDemand()
    {
        var service = new FakePreferencesService(new AppPreferences
        {
            ResumePlaybackOnDemand = true
        });
        var viewModel = new PreferencesViewModel(service);

        var result = viewModel.Save(viewModel.EditorState with { ResumePlaybackAutomatically = true },
            PreferencesMutuallyExclusiveOption.ResumePlaybackAutomatically);

        Assert.True(result.Succeeded);
        Assert.True(service.Saved!.ResumePlaybackAutomatically);
        Assert.False(service.Saved.ResumePlaybackOnDemand);
    }

    [Fact]
    public void Save_WhenResumeOnDemandIsEnabled_DisablesResumeAutomatically()
    {
        var service = new FakePreferencesService(new AppPreferences
        {
            ResumePlaybackAutomatically = true
        });
        var viewModel = new PreferencesViewModel(service);

        var result = viewModel.Save(viewModel.EditorState with { ResumePlaybackOnDemand = true },
            PreferencesMutuallyExclusiveOption.ResumePlaybackOnDemand);

        Assert.True(result.Succeeded);
        Assert.True(service.Saved!.ResumePlaybackOnDemand);
        Assert.False(service.Saved.ResumePlaybackAutomatically);
    }

    [Fact]
    public void Save_WithNoChangedOptionAndConflictingFlags_NormalizesUsingPrecedenceRules()
    {
        var service = new FakePreferencesService(new AppPreferences());
        var viewModel = new PreferencesViewModel(service);

        var result = viewModel.Save(viewModel.EditorState with
        {
            MarkWatchedVideos = true,
            YouTubePlaybackTelemetryEnabled = true,
            ResumePlaybackAutomatically = true,
            ResumePlaybackOnDemand = true
        });

        Assert.True(result.Succeeded);
        // Fallback rule: MarkWatchedVideos is false if telemetry is enabled; ResumePlaybackOnDemand is false if auto-resume is true
        Assert.False(service.Saved!.MarkWatchedVideos);
        Assert.True(service.Saved.YouTubePlaybackTelemetryEnabled);
        Assert.True(service.Saved.ResumePlaybackAutomatically);
        Assert.False(service.Saved.ResumePlaybackOnDemand);
    }


    [Fact]
    public void Save_WhenPersistenceFails_ReturnsRevertedStateAndExactStatusMessage()
    {
        var original = new AppPreferences { Theme = "Light", YtDlpExecutablePath = "/usr/bin/yt-dlp" };
        var service = new FakePreferencesService(original) { ThrowOnSave = true };
        var viewModel = new PreferencesViewModel(service);

        var result = viewModel.Save(viewModel.EditorState with { Theme = "Dark", YtDlpExecutablePath = "/custom/yt-dlp" });

        Assert.False(result.Succeeded);
        Assert.Equal("Light", result.State.Theme);
        Assert.Equal("/usr/bin/yt-dlp", result.State.YtDlpExecutablePath);
        Assert.Equal(PreferencesViewModel.PersistenceErrorMessage, result.ErrorMessage);
        Assert.Null(service.Saved);
    }

    private sealed class FakePreferencesService(AppPreferences initial) : IPreferencesService
    {
        private AppPreferences _current = initial;

        public bool ThrowOnSave { get; init; }
        public AppPreferences? Saved { get; private set; }
        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences()
        {
            return _current;
        }

        public void SavePreferences(AppPreferences preferences)
        {
            if (ThrowOnSave) throw new PreferencesPersistenceException("test", new IOException());

            Saved = preferences;
            _current = preferences;
            PreferencesChanged?.Invoke(this, preferences);
        }
    }
}