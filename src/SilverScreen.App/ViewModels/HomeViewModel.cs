using System.ComponentModel;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Feed;

namespace SilverScreen.ViewModels;

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