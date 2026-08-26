using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Browsing.Components;
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
        AuthenticatedSubscriptionsStatus.Success,
        string.Empty,
        true);
}

public sealed class SubscriptionsViewModel : INotifyPropertyChanged, IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<SubscriptionsViewModel>();
    private readonly IChannelService _channelService;
    private readonly List<VideoSummary> _channelVideos = [];
    private readonly List<SubscribedChannel> _channels = [];
    private readonly PagedFeedEngine _engine;
    private readonly List<VideoSummary> _feedVideos = [];
    private readonly Lock _lock = new();
    private readonly ISessionService _sessionService;

    private readonly IAuthenticatedSubscriptionsService _subscriptionsService;
    private bool _disposed;
    private string? _feedContinuationToken;
    private AuthenticatedSubscriptionsStatus _feedStatus = AuthenticatedSubscriptionsStatus.Success;
    private bool _feedSuccess = true;
    private string _feedSummary = string.Empty;
    private bool _hasMoreFeed;
    private bool _isLoadingChannels;
    private int _lastRequestedCount = VideoFeedConstants.DefaultPageSize;
    private bool _loadedAtLeastOnce;
    private Action? _openWebLogin;

    private SubscribedChannel? _selectedChannel;

    public SubscriptionsViewModel(
        IAuthenticatedSubscriptionsService subscriptionsService,
        IChannelService channelService,
        ISessionService sessionService,
        Action? openWebLogin = null)
    {
        _subscriptionsService = subscriptionsService ?? throw new ArgumentNullException(nameof(subscriptionsService));
        _channelService = channelService ?? throw new ArgumentNullException(nameof(channelService));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _openWebLogin = openWebLogin;

        _engine = new PagedFeedEngine(
            FetchCurrentFeedPageAsync,
            (_, _, _) => SubscriptionsVideoListSource.MapState(State, _openWebLogin).Status,
            "Loading subscriptions…",
            defaultTitle: "Subscriptions",
            clearOnRefresh: false);

        _engine.EngineStateChanged += OnEngineStateChanged;
    }

    public SubscriptionsViewModel(
        IAuthenticatedSubscriptionsService subscriptionsService,
        IChannelService channelService,
        ISessionService sessionService,
        bool subscribeSessionEvents,
        Action? openWebLogin = null) : this(subscriptionsService, channelService, sessionService, openWebLogin)
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionService.SessionChanged -= OnSessionChanged;
        _engine.Dispose();
    }

    VideoListPresentationState IVideoListSource.State => _engine.State;

    event EventHandler<VideoListPresentationState>? IVideoListSource.StateChanged
    {
        add => _engine.StateChanged += value;
        remove => _engine.StateChanged -= value;
    }

    public async Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        _lastRequestedCount = Math.Max(count, 1);

        if (!IsSessionActive())
        {
            lock (_lock)
            {
                _feedVideos.Clear();
                _channels.Clear();
                _channelVideos.Clear();
                _selectedChannel = null;
                _loadedAtLeastOnce = false;
                _feedContinuationToken = null;
                _hasMoreFeed = false;
            }

            _engine.Reset();
            State = new SubscriptionsViewState(
                [],
                null,
                [],
                false,
                false,
                false,
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                "Sign in to YouTube to load your subscriptions.",
                false);
            return;
        }

        lock (_lock)
        {
            _isLoadingChannels = true;
        }

        State = State with
        {
            IsLoading = true,
            Summary = "Loading subscriptions…"
        };

        try
        {
            var channelsTask = _subscriptionsService.LoadSubscribedChannelsAsync(CancellationToken.None);
            var feedTask = _subscriptionsService.LoadFirstFeedPageAsync(_lastRequestedCount, CancellationToken.None);

            await Task.WhenAll(channelsTask, feedTask).ConfigureAwait(false);

            if (_disposed)
                return;

            var channelsResult = await channelsTask.ConfigureAwait(false);
            var feedResult = await feedTask.ConfigureAwait(false);

            lock (_lock)
            {
                _channels.Clear();
                _channels.AddRange(channelsResult.Channels);

                _feedVideos.Clear();
                _feedVideos.AddRange(feedResult.FeedPage.Videos.Where(v => !v.IsShort).DistinctBy(v => v.Id));
                _feedContinuationToken = feedResult.FeedPage.ContinuationToken;
                _hasMoreFeed = !string.IsNullOrEmpty(_feedContinuationToken);
                _feedStatus = feedResult.Status;
                _feedSummary = feedResult.StatusMessage;
                _feedSuccess = feedResult.Status is AuthenticatedSubscriptionsStatus.Success
                    or AuthenticatedSubscriptionsStatus.Empty;
                _loadedAtLeastOnce = true;
                _isLoadingChannels = false;
            }

            if (_selectedChannel is { } activeChannel)
            {
                await RefreshSelectedChannelAsync(activeChannel).ConfigureAwait(false);
                return;
            }

            _engine.SetVideos(
                _feedVideos,
                _feedContinuationToken,
                _hasMoreFeed,
                statusMessage: feedResult.StatusMessage,
                isSuccess: _feedSuccess);

            UpdateViewState();
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to refresh subscriptions");
            if (_disposed)
                return;

            lock (_lock)
            {
                _isLoadingChannels = false;
            }

            State = State with
            {
                IsLoading = false,
                HasMore = false,
                Status = AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                Summary = "Failed to load subscriptions. Check your network connection and try again.",
                IsSuccess = false
            };
        }
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        if (State.IsLoading || State.IsLoadingMore || !State.HasMore)
            return Task.CompletedTask;

        _lastRequestedCount = Math.Max(count, 1);
        return _engine.LoadMoreAsync(count);
    }

    public event EventHandler<SubscriptionsViewState>? StateChanged;

    public IVideoListSource GetVideoListSource(Action? openWebLogin = null)
    {
        if (openWebLogin != null)
            _openWebLogin = openWebLogin;
        return this;
    }

    public Task LoadAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();

        if (State.IsLoading || State.IsLoadingMore || (_loadedAtLeastOnce &&
                                                       State.Status is not (AuthenticatedSubscriptionsStatus
                                                               .AuthenticationRequired
                                                           or AuthenticatedSubscriptionsStatus
                                                               .AuthenticationRejected) &&
                                                       (State.Videos.Count > 0 || State.Channels.Count > 0 ||
                                                        State.IsSuccess)))
            return Task.CompletedTask;

        return RefreshAsync(count);
    }

    public async Task SelectChannelAsync(SubscribedChannel? channel, int batchSize = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();

        if (channel is null)
        {
            if (_selectedChannel is null)
                return;

            lock (_lock)
            {
                _selectedChannel = null;
                _channelVideos.Clear();
            }

            _engine.SetVideos(
                _feedVideos,
                _feedContinuationToken,
                _hasMoreFeed,
                statusMessage: _feedSummary,
                isSuccess: _feedSuccess);

            UpdateViewState();
            return;
        }

        if (_selectedChannel is { } current &&
            (ReferenceEquals(current, channel) ||
             (!string.IsNullOrWhiteSpace(channel.Id) && current.Id == channel.Id) ||
             (!string.IsNullOrWhiteSpace(channel.Url) && current.Url == channel.Url)))
            return;

        _selectedChannel = channel;
        var pageSize = Math.Max(batchSize, 1);

        List<VideoSummary> inMemoryMatches;
        lock (_lock)
        {
            inMemoryMatches = [.. _feedVideos.Where(v => IsMatchingChannel(v, channel))];
            _channelVideos.Clear();
            _channelVideos.AddRange(inMemoryMatches);
        }

        // Configure engine for channel uploads
        _engine.SetVideos(
            inMemoryMatches,
            null,
            true,
            statusMessage: inMemoryMatches.Count == 0 ? $"Loading {channel.Title} uploads…" : string.Empty,
            isSuccess: true);

        State = State with
        {
            SelectedChannel = channel,
            Videos = [.. inMemoryMatches],
            IsLoading = inMemoryMatches.Count == 0,
            IsLoadingMore = inMemoryMatches.Count > 0,
            HasMore = true,
            Status = AuthenticatedSubscriptionsStatus.Success,
            Summary = inMemoryMatches.Count == 0 ? $"Loading {channel.Title} uploads…" : string.Empty,
            IsSuccess = true
        };

        try
        {
            var page = await _channelService.GetChannelAsync(
                channel.Url,
                channel.Title,
                ChannelVideoSort.Newest,
                1,
                pageSize,
                CancellationToken.None).ConfigureAwait(false);

            if (_disposed || _selectedChannel != channel)
                return;

            if (page.IsSuccess)
            {
                lock (_lock)
                {
                    foreach (var video in page.Videos)
                        if (_channelVideos.All(existing => existing.Id != video.Id))
                            _channelVideos.Add(video);
                }

                _engine.SetVideos(
                    _channelVideos,
                    page.NextStartIndex?.ToString(),
                    page.NextStartIndex.HasValue,
                    statusMessage: string.Empty,
                    isSuccess: true);
            }
            else
            {
                _engine.SetVideos(
                    _channelVideos,
                    null,
                    false,
                    statusMessage: page.StatusMessage ?? "Could not load channel uploads.",
                    isSuccess: _channelVideos.Count > 0);
            }

            UpdateViewState();
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load background channel uploads for {ChannelTitle}", channel.Title);
            if (_disposed || _selectedChannel != channel)
                return;

            _engine.SetVideos(
                _channelVideos,
                null,
                false,
                statusMessage: "Failed to load channel uploads.",
                isSuccess: _channelVideos.Count > 0);

            UpdateViewState();
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
            if (!string.IsNullOrWhiteSpace(video.ChannelUrl) &&
                video.ChannelUrl.Contains(channel.Id, StringComparison.OrdinalIgnoreCase))
                return true;

        if (string.IsNullOrWhiteSpace(video.ChannelName) || string.IsNullOrWhiteSpace(channel.Title)) return false;
        return string.Equals(video.ChannelName, channel.Title, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<FeedPageResult> FetchCurrentFeedPageAsync(string? token, int count, CancellationToken ct)
    {
        SubscribedChannel? selected;
        lock (_lock)
        {
            selected = _selectedChannel;
        }

        if (selected is not null)
        {
            var startIndex = token != null && int.TryParse(token, out var idx) ? idx : 1;
            var page = await _channelService.GetChannelAsync(
                selected.Url,
                selected.Title,
                ChannelVideoSort.Newest,
                startIndex,
                count,
                ct).ConfigureAwait(false);

            return new FeedPageResult(
                page.Videos,
                page.IsSuccess ? page.NextStartIndex?.ToString() : null,
                page.IsSuccess,
                page.StatusMessage);
        }

        var res = token is null
            ? await _subscriptionsService.LoadFirstFeedPageAsync(count, ct).ConfigureAwait(false)
            : await _subscriptionsService.LoadNextFeedPageAsync(count, ct).ConfigureAwait(false);

        lock (_lock)
        {
            _feedStatus = res.Status;
            _feedSummary = res.StatusMessage;
            _feedSuccess =
                res.Status is AuthenticatedSubscriptionsStatus.Success or AuthenticatedSubscriptionsStatus.Empty;
            _feedContinuationToken = res.FeedPage.ContinuationToken;
            _hasMoreFeed = !string.IsNullOrEmpty(_feedContinuationToken);
        }

        return new FeedPageResult(
            res.FeedPage.Videos,
            res.Status == AuthenticatedSubscriptionsStatus.Success ? res.FeedPage.ContinuationToken : null,
            _feedSuccess,
            res.StatusMessage);
    }

    private async Task RefreshSelectedChannelAsync(SubscribedChannel activeChannel)
    {
        var inMemoryMatches = _feedVideos.Where(v => IsMatchingChannel(v, activeChannel)).ToList();
        lock (_lock)
        {
            _channelVideos.Clear();
            _channelVideos.AddRange(inMemoryMatches);
        }

        var page = await _channelService.GetChannelAsync(
            activeChannel.Url,
            activeChannel.Title,
            ChannelVideoSort.Newest,
            1,
            _lastRequestedCount,
            CancellationToken.None).ConfigureAwait(false);

        if (_disposed || _selectedChannel != activeChannel)
            return;

        if (page.IsSuccess)
        {
            lock (_lock)
            {
                foreach (var video in page.Videos)
                    if (_channelVideos.All(existing => existing.Id != video.Id))
                        _channelVideos.Add(video);
            }

            _engine.SetVideos(
                _channelVideos,
                page.NextStartIndex?.ToString(),
                page.NextStartIndex.HasValue,
                statusMessage: string.Empty,
                isSuccess: true);
        }
        else
        {
            _engine.SetVideos(
                _channelVideos,
                null,
                false,
                statusMessage: page.StatusMessage ?? "Could not load channel uploads.",
                isSuccess: _channelVideos.Count > 0);
        }

        UpdateViewState();
    }

    private void OnEngineStateChanged(object? sender, FeedEngineState engineState)
    {
        UpdateViewState();
    }

    private void UpdateViewState()
    {
        var engineState = _engine.EngineState;
        var status = _selectedChannel != null
            ? engineState.IsSuccess
                ? AuthenticatedSubscriptionsStatus.Success
                : AuthenticatedSubscriptionsStatus.TemporaryBackendFailure
            : _feedStatus;

        State = new SubscriptionsViewState(
            [.. _channels],
            _selectedChannel,
            engineState.Videos,
            engineState.IsLoading,
            engineState.IsLoadingMore,
            engineState.HasMore,
            status,
            engineState.StatusMessage ?? string.Empty,
            engineState.IsSuccess);
    }

    private bool IsSessionActive()
    {
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
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
            lock (_lock)
            {
                _feedVideos.Clear();
                _channels.Clear();
                _channelVideos.Clear();
                _selectedChannel = null;
                _loadedAtLeastOnce = false;
            }

            _engine.Reset();
            State = new SubscriptionsViewState(
                [],
                null,
                [],
                false,
                false,
                false,
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                "Sign in to YouTube to load your subscriptions.",
                false);
        }
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