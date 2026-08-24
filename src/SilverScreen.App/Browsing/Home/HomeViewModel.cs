using System.ComponentModel;
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
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;

namespace SilverScreen.Browsing.Home;

public sealed class HomeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly HomeFeedCoordinator _coordinator;
    private bool _disposed;
    private HomeFeedState _state;

    public HomeViewModel(HomeFeedCoordinator coordinator)
    {
        _coordinator = coordinator;
        _state = coordinator.State;
        _coordinator.StateChanged += OnStateChanged;
    }

    public HomeFeedState State
    {
        get => _state;
        private set
        {
            _state = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            StateChanged?.Invoke(this, value);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _coordinator.StateChanged -= OnStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<HomeFeedState>? StateChanged;

    public Task RefreshAsync()
    {
        return _coordinator.RefreshAsync();
    }

    public Task LoadMoreAsync()
    {
        return _coordinator.LoadMoreAsync();
    }

    private void OnStateChanged(object? sender, HomeFeedState state)
    {
        if (!_disposed)
            State = state;
    }
}