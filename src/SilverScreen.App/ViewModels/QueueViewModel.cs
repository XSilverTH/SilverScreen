using System.Collections.Immutable;
using System.ComponentModel;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.ViewModels;

public sealed record QueuePresentationState(
    IReadOnlyList<QueueItem> Items,
    TimeSpan TotalDuration,
    bool IsLaunching,
    int CurrentPlayingIndex = -1)
{
    public bool IsVisible => Items.Count > 0;

    public bool CanPlay => IsVisible && !IsLaunching;
}

public sealed class QueueViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<QueueViewModel>();
    private readonly IPlaybackService _playback;
    private readonly IQueueService _queue;
    private readonly IStatusReporter? _shell;
    private int _currentPlayingIndex = -1;
    private bool _disposed;
    private bool _isLaunching;
    private QueuePresentationState _state;

    public QueueViewModel(IQueueService queue, IPlaybackService playback, IStatusReporter? shell = null)
    {
        _queue = queue;
        _playback = playback;
        _shell = shell;
        _state = Snapshot();
        _queue.Changed += OnQueueChanged;
    }

    public QueuePresentationState State
    {
        get => _state;
        private set
        {
            _state = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPlay)));
            StateChanged?.Invoke(this, value);
        }
    }

    public bool IsVisible => State.IsVisible;

    public bool CanPlay => State.CanPlay;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.Changed -= OnQueueChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<QueuePresentationState>? StateChanged;

    public void Move(Guid itemId, int destinationIndex)
    {
        _queue.Move(itemId, destinationIndex);
    }

    public void Remove(Guid itemId)
    {
        _queue.Remove(itemId);
    }


    public void SetCurrentPlayingIndex(int index)
    {
        if (_disposed || _currentPlayingIndex == index)
            return;

        _currentPlayingIndex = index;
        State = Snapshot();
    }

    public async Task PlayAllAsync()
    {
        if (_disposed || _isLaunching || _queue.Items.Count == 0)
            return;

        _isLaunching = true;
        State = Snapshot();
        var videos = _queue.Items.Select(item => item.Video).ToImmutableArray();

        try
        {
            var status = await _playback.PlayAsync(new PlaybackRequest(videos));
            _shell?.ReportStatus(status);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to start playback for queue items");
            _shell?.ReportStatus("Playback could not be started.");
        }
        finally
        {
            if (!_disposed)
            {
                _isLaunching = false;
                State = Snapshot();
            }
        }
    }

    public void Clear()
    {
        _queue.Clear();
    }

    private QueuePresentationState Snapshot()
    {
        return new QueuePresentationState([.. _queue.Items], _queue.TotalDuration, _isLaunching, _currentPlayingIndex);
    }

    private void OnQueueChanged(object? sender, EventArgs eventArgs)
    {
        if (!_disposed)
            State = Snapshot();
    }
}