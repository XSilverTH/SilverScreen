using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Browsing.Subscriptions;

public sealed record SubscriptionsViewState(
    IReadOnlyList<SubscribedChannel> Channels,
    SubscribedChannel? SelectedChannel,
    IReadOnlyList<VideoSummary> Videos,
    bool IsLoading,
    bool IsLoadingMore,
    bool HasMore,
    bool IsLoadingChannels,
    AuthenticatedSubscriptionsStatus Status,
    string Summary,
    bool IsSuccess)
{
    public static SubscriptionsViewState Empty { get; } = new(
        [],
        null,
        [],
        false,
        false,
        false,
        false,
        AuthenticatedSubscriptionsStatus.Success,
        string.Empty,
        true);
}

public sealed class SubscriptionsViewModel(
    IAuthenticatedSubscriptionsService subscriptionsService,
    IChannelService channelService,
    ISessionService sessionService) : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<SubscriptionsViewModel>();

    private readonly List<SubscribedChannel> _channels = [];
    private readonly List<VideoSummary> _channelVideos = [];
    private readonly List<VideoSummary> _feedVideos = [];
    private readonly Lock _lock = new();

    private CancellationTokenSource? _channelCancellation;
    private int? _channelNextStartIndex;
    private long _channelRequestGeneration;
    private bool _disposed;
    private AuthenticatedSubscriptionsStatus _feedStatus = AuthenticatedSubscriptionsStatus.Success;
    private bool _feedSuccess = true;
    private string _feedSummary = string.Empty;
    private bool _hasMoreFeed;
    private int _lastRequestedCount = VideoFeedConstants.DefaultPageSize;
    private bool _loadedAtLeastOnce;
    private CancellationTokenSource? _requestCancellation;
    private long _requestGeneration;
    private SubscribedChannel? _selectedChannel;

    public SubscriptionsViewModel(
        IAuthenticatedSubscriptionsService subscriptionsService,
        IChannelService channelService,
        ISessionService sessionService,
        bool subscribeSessionEvents) : this(subscriptionsService, channelService, sessionService)
    {
        if (subscribeSessionEvents)
            sessionService.SessionChanged += OnSessionChanged;
    }

    public SubscriptionsViewState State
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            StateChanged?.Invoke(this, value);
        }
    } = SubscriptionsViewState.Empty;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        sessionService.SessionChanged -= OnSessionChanged;
        CancelAll();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<SubscriptionsViewState>? StateChanged;

    public Task LoadAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();

        if (State.IsLoading || State.IsLoadingMore)
            return Task.CompletedTask;

        if (_loadedAtLeastOnce &&
            State.Status is not (AuthenticatedSubscriptionsStatus.AuthenticationRequired
                or AuthenticatedSubscriptionsStatus.AuthenticationRejected) &&
            (State.Videos.Count > 0 || State.Channels.Count > 0 || State.IsSuccess))
            return Task.CompletedTask;

        return RefreshAsync(count);
    }

    public async Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        _lastRequestedCount = Math.Max(count, 1);

        if (!IsSessionActive())
        {
            CancelAll();
            lock (_lock)
            {
                _feedVideos.Clear();
                _channels.Clear();
                _channelVideos.Clear();
                _selectedChannel = null;
                _loadedAtLeastOnce = false;
            }

            State = new SubscriptionsViewState(
                [],
                null,
                [],
                false,
                false,
                false,
                false,
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                "Sign in to YouTube to load your subscriptions.",
                false);
            return;
        }

        CancelAll();
        var generation = ++_requestGeneration;
        _requestCancellation = new CancellationTokenSource();
        var cancellationToken = _requestCancellation.Token;

        State = State with
        {
            IsLoading = true,
            IsLoadingChannels = true,
            Summary = "Loading subscriptions…"
        };

        try
        {
            var channelsTask = subscriptionsService.LoadSubscribedChannelsAsync(cancellationToken);
            var feedTask = subscriptionsService.LoadFirstFeedPageAsync(_lastRequestedCount, cancellationToken);

            await Task.WhenAll(channelsTask, feedTask).ConfigureAwait(false);

            if (_disposed || cancellationToken.IsCancellationRequested || _requestGeneration != generation)
                return;

            var channelsResult = await channelsTask.ConfigureAwait(false);
            var feedResult = await feedTask.ConfigureAwait(false);

            lock (_lock)
            {
                _channels.Clear();
                _channels.AddRange(channelsResult.Channels);

                _feedVideos.Clear();
                _feedVideos.AddRange(feedResult.FeedPage.Videos);
                _hasMoreFeed = !string.IsNullOrEmpty(feedResult.FeedPage.ContinuationToken);
                _feedStatus = feedResult.Status;
                _feedSummary = feedResult.StatusMessage;
                _feedSuccess = feedResult.Status is AuthenticatedSubscriptionsStatus.Success
                    or AuthenticatedSubscriptionsStatus.Empty;
                _loadedAtLeastOnce = true;
            }

            if (_selectedChannel is { } activeChannel)
            {
                await RefreshSelectedChannelAsync(activeChannel, generation, cancellationToken).ConfigureAwait(false);
                return;
            }

            State = new SubscriptionsViewState(
                [.. _channels],
                null,
                [.. _feedVideos],
                false,
                false,
                _hasMoreFeed,
                false,
                feedResult.Status,
                feedResult.StatusMessage,
                _feedSuccess);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ignored
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to refresh subscriptions");
            if (_disposed || cancellationToken.IsCancellationRequested || _requestGeneration != generation)
                return;

            State = State with
            {
                IsLoading = false,
                IsLoadingChannels = false,
                HasMore = false,
                Status = AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                Summary = "Failed to load subscriptions. Check your network connection and try again.",
                IsSuccess = false
            };
        }
    }

    public async Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();

        if (State.IsLoading || State.IsLoadingMore || !State.HasMore)
            return;

        var generation = _requestGeneration;
        _lastRequestedCount = Math.Max(count, 1);

        State = State with { IsLoadingMore = true };

        try
        {
            if (_selectedChannel is { } activeChannel)
            {
                if (!_channelNextStartIndex.HasValue)
                {
                    State = State with { IsLoadingMore = false, HasMore = false };
                    return;
                }

                var page = await channelService.GetChannelAsync(
                    activeChannel.Url,
                    activeChannel.Title,
                    ChannelVideoSort.Newest,
                    _channelNextStartIndex.Value,
                    _lastRequestedCount,
                    CancellationToken.None).ConfigureAwait(false);

                if (_disposed || _requestGeneration != generation || _selectedChannel != activeChannel)
                    return;

                if (page.IsSuccess && page.Videos.Count > 0)
                {
                    lock (_lock)
                    {
                        foreach (var video in page.Videos)
                            if (_channelVideos.All(existing => existing.Id != video.Id))
                                _channelVideos.Add(video);

                        _channelNextStartIndex = page.NextStartIndex;
                    }

                    State = State with
                    {
                        Videos = [.. _channelVideos],
                        IsLoadingMore = false,
                        HasMore = _channelNextStartIndex.HasValue
                    };
                }
                else
                {
                    _channelNextStartIndex = null;
                    State = State with { IsLoadingMore = false, HasMore = false };
                }

                return;
            }

            var result = await subscriptionsService.LoadNextFeedPageAsync(_lastRequestedCount, CancellationToken.None)
                .ConfigureAwait(false);

            if (_disposed || _requestGeneration != generation || _selectedChannel is not null)
                return;

            if (result.Status == AuthenticatedSubscriptionsStatus.Success && result.FeedPage.Videos.Count > 0)
            {
                lock (_lock)
                {
                    foreach (var video in result.FeedPage.Videos)
                        if (_feedVideos.All(existing => existing.Id != video.Id))
                            _feedVideos.Add(video);

                    _hasMoreFeed = !string.IsNullOrEmpty(result.FeedPage.ContinuationToken);
                }

                State = State with
                {
                    Videos = [.. _feedVideos],
                    IsLoadingMore = false,
                    HasMore = _hasMoreFeed
                };
            }
            else
            {
                _hasMoreFeed = false;
                State = State with { IsLoadingMore = false, HasMore = false };
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load more subscriptions");
            if (!_disposed && _requestGeneration == generation)
                State = State with { IsLoadingMore = false, HasMore = false };
        }
    }

    public async Task SelectChannelAsync(SubscribedChannel? channel, int batchSize = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();

        if (channel is null)
        {
            if (_selectedChannel is null)
                return;

            _channelCancellation?.Cancel();
            _channelCancellation?.Dispose();
            _channelCancellation = null;
            _selectedChannel = null;

            lock (_lock)
            {
                _channelVideos.Clear();
                _channelNextStartIndex = null;
            }

            State = State with
            {
                SelectedChannel = null,
                Videos = [.. _feedVideos],
                IsLoading = false,
                IsLoadingMore = false,
                HasMore = _hasMoreFeed,
                Status = _feedStatus,
                Summary = _feedSummary,
                IsSuccess = _feedSuccess
            };
            return;
        }

        if (_selectedChannel is { } current &&
            (ReferenceEquals(current, channel) ||
             (!string.IsNullOrWhiteSpace(channel.Id) && current.Id == channel.Id) ||
             (!string.IsNullOrWhiteSpace(channel.Url) && current.Url == channel.Url)))
        {
            return;
        }

        _selectedChannel = channel;
        var pageSize = Math.Max(batchSize, 1);

        // 1. Immediate in-memory filter
        List<VideoSummary> inMemoryMatches;
        lock (_lock)
        {
            inMemoryMatches = _feedVideos.Where(v => IsMatchingChannel(v, channel)).ToList();
            _channelVideos.Clear();
            _channelVideos.AddRange(inMemoryMatches);
            _channelNextStartIndex = null;
        }

        var channelGen = ++_channelRequestGeneration;
        _channelCancellation?.Cancel();
        _channelCancellation?.Dispose();
        _channelCancellation = new CancellationTokenSource();
        var token = _channelCancellation.Token;

        State = State with
        {
            SelectedChannel = channel,
            Videos = [.. _channelVideos],
            IsLoading = inMemoryMatches.Count == 0,
            IsLoadingMore = inMemoryMatches.Count > 0,
            HasMore = true,
            Status = AuthenticatedSubscriptionsStatus.Success,
            Summary = inMemoryMatches.Count == 0 ? $"Loading {channel.Title} uploads…" : string.Empty,
            IsSuccess = true
        };

        // 2. Background channel fetch
        try
        {
            var page = await channelService.GetChannelAsync(
                channel.Url,
                channel.Title,
                ChannelVideoSort.Newest,
                1,
                pageSize,
                token).ConfigureAwait(false);

            if (_disposed || token.IsCancellationRequested || _channelRequestGeneration != channelGen ||
                _selectedChannel != channel)
                return;

            if (page.IsSuccess)
            {
                lock (_lock)
                {
                    foreach (var video in page.Videos)
                        if (_channelVideos.All(existing => existing.Id != video.Id))
                            _channelVideos.Add(video);

                    _channelNextStartIndex = page.NextStartIndex;
                }

                State = State with
                {
                    Videos = [.. _channelVideos],
                    IsLoading = false,
                    IsLoadingMore = false,
                    HasMore = _channelNextStartIndex.HasValue,
                    Status = AuthenticatedSubscriptionsStatus.Success,
                    Summary = string.Empty,
                    IsSuccess = true
                };
            }
            else
            {
                State = State with
                {
                    IsLoading = false,
                    IsLoadingMore = false,
                    HasMore = false,
                    Status = _channelVideos.Count > 0
                        ? AuthenticatedSubscriptionsStatus.Success
                        : AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                    Summary = page.StatusMessage ?? "Could not load channel uploads.",
                    IsSuccess = _channelVideos.Count > 0
                };
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Ignored
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load background channel uploads for {ChannelTitle}", channel.Title);
            if (_disposed || token.IsCancellationRequested || _channelRequestGeneration != channelGen ||
                _selectedChannel != channel)
                return;

            State = State with
            {
                IsLoading = false,
                IsLoadingMore = false,
                HasMore = false,
                Status = _channelVideos.Count > 0
                    ? AuthenticatedSubscriptionsStatus.Success
                    : AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                Summary = "Failed to load channel uploads.",
                IsSuccess = _channelVideos.Count > 0
            };
        }
    }

    public static bool IsMatchingChannel(VideoSummary video, SubscribedChannel channel)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(channel);

        if (!string.IsNullOrWhiteSpace(video.ChannelUrl) && !string.IsNullOrWhiteSpace(channel.Url))
        {
            var vUrl = video.ChannelUrl.TrimEnd('/');
            var cUrl = channel.Url.TrimEnd('/');
            if (vUrl.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
                vUrl = vUrl[..^7];
            if (cUrl.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
                cUrl = cUrl[..^7];

            if (string.Equals(vUrl, cUrl, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(channel.Id))
        {
            if (!string.IsNullOrWhiteSpace(video.ChannelUrl) &&
                video.ChannelUrl.Contains(channel.Id, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(video.ChannelName) && !string.IsNullOrWhiteSpace(channel.Title))
        {
            if (string.Equals(video.ChannelName, channel.Title, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task RefreshSelectedChannelAsync(SubscribedChannel activeChannel, long generation,
        CancellationToken cancellationToken)
    {
        var inMemoryMatches = _feedVideos.Where(v => IsMatchingChannel(v, activeChannel)).ToList();
        lock (_lock)
        {
            _channelVideos.Clear();
            _channelVideos.AddRange(inMemoryMatches);
            _channelNextStartIndex = null;
        }

        var page = await channelService.GetChannelAsync(
            activeChannel.Url,
            activeChannel.Title,
            ChannelVideoSort.Newest,
            1,
            _lastRequestedCount,
            cancellationToken).ConfigureAwait(false);

        if (_disposed || cancellationToken.IsCancellationRequested || _requestGeneration != generation ||
            _selectedChannel != activeChannel)
            return;

        if (page.IsSuccess)
        {
            lock (_lock)
            {
                foreach (var video in page.Videos)
                    if (_channelVideos.All(existing => existing.Id != video.Id))
                        _channelVideos.Add(video);

                _channelNextStartIndex = page.NextStartIndex;
            }

            State = new SubscriptionsViewState(
                [.. _channels],
                activeChannel,
                [.. _channelVideos],
                false,
                false,
                _channelNextStartIndex.HasValue,
                false,
                AuthenticatedSubscriptionsStatus.Success,
                string.Empty,
                true);
        }
        else
        {
            State = new SubscriptionsViewState(
                [.. _channels],
                activeChannel,
                [.. _channelVideos],
                false,
                false,
                false,
                false,
                _channelVideos.Count > 0
                    ? AuthenticatedSubscriptionsStatus.Success
                    : AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                page.StatusMessage ?? "Could not load channel uploads.",
                _channelVideos.Count > 0);
        }
    }

    private bool IsSessionActive()
    {
        var session = sessionService.GetCurrentSession();
        var cookies = sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } && cookies != null &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (IsSessionActive())
        {
            RefreshAsync(_lastRequestedCount).FireAndForget(Logger);
        }
        else
        {
            CancelAll();
            lock (_lock)
            {
                _feedVideos.Clear();
                _channels.Clear();
                _channelVideos.Clear();
                _selectedChannel = null;
                _loadedAtLeastOnce = false;
            }

            State = new SubscriptionsViewState(
                [],
                null,
                [],
                false,
                false,
                false,
                false,
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                "Sign in to YouTube to load your subscriptions.",
                false);
        }
    }

    private void CancelAll()
    {
        ++_requestGeneration;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;

        ++_channelRequestGeneration;
        _channelCancellation?.Cancel();
        _channelCancellation?.Dispose();
        _channelCancellation = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
