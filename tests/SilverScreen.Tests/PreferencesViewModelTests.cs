using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;

namespace SilverScreen.Tests;

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
        var shortcuts = viewModel.EditorState.Shortcuts.Clone();
        shortcuts.TogglePause = ["Pause"];

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