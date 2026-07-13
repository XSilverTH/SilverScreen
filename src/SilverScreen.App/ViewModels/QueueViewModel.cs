using System.ComponentModel;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.ViewModels;

public sealed record QueuePresentationState(IReadOnlyList<QueueItem> Items, TimeSpan TotalDuration)
{
    public bool IsVisible => Items.Count > 0;
}

public sealed class QueueViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IQueueService _queue;
    private QueuePresentationState _state;
    private bool _disposed;

    public QueueViewModel(IQueueService queue)
    {
        _queue = queue;
        _state = Snapshot();
        _queue.Changed += OnQueueChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<QueuePresentationState>? StateChanged;

    public QueuePresentationState State
    {
        get => _state;
        private set
        {
            _state = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            StateChanged?.Invoke(this, value);
        }
    }

    public bool IsVisible => State.IsVisible;
    public void Remove(QueueItem item) => _queue.Remove(item);
    public void Clear() => _queue.Clear();

    private QueuePresentationState Snapshot() => new(_queue.Items.ToArray(), _queue.TotalDuration);

    private void OnQueueChanged(object? sender, EventArgs eventArgs)
    {
        if (!_disposed)
            State = Snapshot();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.Changed -= OnQueueChanged;
    }
}