using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;


namespace SilverScreen.Tests.Preferences;

public sealed class PreferencesViewModelTests
{
    [Fact]
    public void Save_InvalidMaxResults_UsesTwenty_AndPreservesCurrentSubtitleLanguage()
    {
        var service = new FakePreferencesService(new AppPreferences { PreferredSubtitleLanguage = "ja" });
        var viewModel = new PreferencesViewModel(service);

        var result = viewModel.Save(viewModel.EditorState with { MaxResultsText = "not a number" });

        Assert.True(result.Succeeded);
        Assert.NotNull(service.Saved);
        Assert.Equal(20, service.Saved!.MaxResults);
        Assert.Equal("ja", service.Saved.PreferredSubtitleLanguage);
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
        var original = new AppPreferences { Theme = "Light", MaxResults = 12 };
        var service = new FakePreferencesService(original) { ThrowOnSave = true };
        var viewModel = new PreferencesViewModel(service);

        var result = viewModel.Save(viewModel.EditorState with { Theme = "Dark", MaxResultsText = "99" });

        Assert.False(result.Succeeded);
        Assert.Equal("Light", result.State.Theme);
        Assert.Equal("12", result.State.MaxResultsText);
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
