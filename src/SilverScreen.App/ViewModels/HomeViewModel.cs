using System.ComponentModel;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Feed;

namespace SilverScreen.ViewModels;

public sealed class HomeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly HomeFeedCoordinator _coordinator;
    private HomeFeedState _state;
    private bool _disposed;

    public HomeViewModel(HomeFeedCoordinator coordinator)
    {
        _coordinator = coordinator;
        _state = coordinator.State;
        _coordinator.StateChanged += OnStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<HomeFeedState>? StateChanged;

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

    public Task RefreshAsync() => _coordinator.RefreshAsync();

    public Task LoadMoreAsync() => _coordinator.LoadMoreAsync();

    private void OnStateChanged(object? sender, HomeFeedState state)
    {
        if (!_disposed)
            State = state;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _coordinator.StateChanged -= OnStateChanged;
    }
}