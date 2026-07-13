using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Feed;

public sealed class HomeFeedCoordinator : IDisposable
{
    private readonly ISessionService _sessionService;
    private readonly IAuthenticatedHomeFeedService _feedService;
    private readonly Lock _lock = new();
    private readonly List<VideoSummary> _videos = [];
    private string? _continuationToken;
    private CancellationTokenSource? _cts;
    private bool _isLoading;

    private long _currentRequestId;
    private long _stateVersion;
    private long _publishedStateVersion;

    public event EventHandler<HomeFeedState>? StateChanged;

    public HomeFeedCoordinator(ISessionService sessionService, IAuthenticatedHomeFeedService feedService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));

        _sessionService.SessionChanged += OnSessionChanged;

        if (IsSessionActive())
        {
            State = new HomeFeedState(HomeFeedStateKind.InitialLoading, [], IsLoading: true);
            _ = RefreshAsync();
        }
        else
            State = HomeFeedState.SignedOut;
    }

    public HomeFeedState State { get; private set; }

    public async Task RefreshAsync()
    {
        CancellationToken token;
        long requestId;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            _isLoading = true;
            _currentRequestId++;
            requestId = _currentRequestId;
        }

        if (!IsSessionActive())
        {
            CancelAndClear();
            PublishState(HomeFeedState.SignedOut);
            return;
        }

        // During refresh with existing videos: Ready + IsLoading true; initial: InitialLoading + IsLoading true
        HomeFeedState pendingState;
        long version;
        lock (_lock)
        {
            if (_currentRequestId != requestId)
            {
                return;
            }

            if (_videos.Count > 0)
            {
                pendingState = new HomeFeedState(
                    HomeFeedStateKind.Ready,
                    _videos.ToArray(),
                    IsLoading: true,
                    HasContinuation: !string.IsNullOrEmpty(_continuationToken));
            }
            else
            {
                pendingState = new HomeFeedState(
                    HomeFeedStateKind.InitialLoading,
                    [],
                    IsLoading: true);
            }

            _stateVersion++;
            State = pendingState;
            version = _stateVersion;
        }

        PublishStateWithVersion(pendingState, version);

        try
        {
            var result = await _feedService.LoadFirstPageAsync(token);

            token.ThrowIfCancellationRequested();

            ProcessResult(result, isFirstPage: true, requestId);
        }
        catch (OperationCanceledException)
        {
            // Cancellation never publishes errors
        }
        catch (Exception)
        {
            HomeFeedState errorState;
            long errVersion;
            lock (_lock)
            {
                if (_currentRequestId == requestId)
                {
                    errorState = new HomeFeedState(
                        HomeFeedStateKind.SafeError,
                        _videos.ToArray(),
                        Message: "Could not load YouTube recommendations.",
                        IsLoading: false,
                        IsLoadingMore: false,
                        HasContinuation: !string.IsNullOrEmpty(_continuationToken));

                    _stateVersion++;
                    State = errorState;
                    errVersion = _stateVersion;
                }
                else
                {
                    return;
                }
            }

            PublishStateWithVersion(errorState, errVersion);
        }
        finally
        {
            lock (_lock)
                if (_currentRequestId == requestId)
                    _isLoading = false;
        }
    }

    public async Task LoadMoreAsync()
    {
        CancellationToken token;
        long requestId;
        lock (_lock)
        {
            if (_isLoading)
                return;

            if (!IsSessionActive())
                return;

            if (string.IsNullOrEmpty(_continuationToken))
                return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            _isLoading = true;
            _currentRequestId++;
            requestId = _currentRequestId;
        }

        // LoadMore: Ready + IsLoadingMore true
        HomeFeedState pendingState;
        long version;
        lock (_lock)
        {
            if (_currentRequestId != requestId)
                return;

            pendingState = new HomeFeedState(
                HomeFeedStateKind.Ready,
                _videos.ToArray(),
                IsLoadingMore: true,
                HasContinuation: !string.IsNullOrEmpty(_continuationToken));

            _stateVersion++;
            State = pendingState;
            version = _stateVersion;
        }

        PublishStateWithVersion(pendingState, version);

        try
        {
            var result = await _feedService.LoadNextPageAsync(token);

            token.ThrowIfCancellationRequested();

            ProcessResult(result, isFirstPage: false, requestId);
        }
        catch (OperationCanceledException)
        {
            // Cancellation never publishes errors
        }
        catch (Exception)
        {
            HomeFeedState errorState;
            long errVersion;
            lock (_lock)
            {
                if (_currentRequestId == requestId)
                {
                    errorState = new HomeFeedState(
                        HomeFeedStateKind.SafeError,
                        _videos.ToArray(),
                        Message: "Could not load YouTube recommendations.",
                        IsLoading: false,
                        IsLoadingMore: false,
                        HasContinuation: !string.IsNullOrEmpty(_continuationToken));

                    _stateVersion++;
                    State = errorState;
                    errVersion = _stateVersion;
                }
                else
                    return;
            }

            PublishStateWithVersion(errorState, errVersion);
        }
        finally
        {
            lock (_lock)
                if (_currentRequestId == requestId)
                    _isLoading = false;
        }
    }

    private void ProcessResult(AuthenticatedHomeFeedResult result, bool isFirstPage, long requestId)
    {
        HomeFeedState nextState;
        long version;

        lock (_lock)
        {
            if (_currentRequestId != requestId)
                return;

            switch (result.Status)
            {
                case AuthenticatedHomeFeedStatus.AuthenticationRequired:
                case AuthenticatedHomeFeedStatus.AuthenticationRejected:
                    _videos.Clear();
                    _continuationToken = null;
                    nextState = new HomeFeedState(
                        HomeFeedStateKind.AuthenticationRequired,
                        [],
                        Message: "Your YouTube session is no longer valid.",
                        IsLoading: false,
                        IsLoadingMore: false,
                        HasContinuation: false);
                    break;
                case AuthenticatedHomeFeedStatus.TemporaryBackendFailure:
                    nextState = new HomeFeedState(
                        HomeFeedStateKind.SafeError,
                        _videos.ToArray(),
                        Message: "Could not load YouTube recommendations.",
                        IsLoading: false,
                        IsLoadingMore: false,
                        HasContinuation: !string.IsNullOrEmpty(_continuationToken));
                    break;
                case AuthenticatedHomeFeedStatus.Empty when isFirstPage:
                    _videos.Clear();
                    _continuationToken = null;
                    nextState = new HomeFeedState(
                        HomeFeedStateKind.Empty,
                        [],
                        Message: "No recommendations are available right now.",
                        IsLoading: false,
                        IsLoadingMore: false,
                        HasContinuation: false);
                    break;
                case AuthenticatedHomeFeedStatus.Empty:
                    _continuationToken = null;
                    nextState = new HomeFeedState(
                        HomeFeedStateKind.Ready,
                        _videos.ToArray(),
                        IsLoading: false,
                        IsLoadingMore: false,
                        HasContinuation: false);
                    break;
                // Success
                case AuthenticatedHomeFeedStatus.Success:
                default:
                {
                    var newVideos = result.FeedPage.Videos
                        .Where(v => !v.IsShort)
                        .ToList();

                    if (isFirstPage)
                    {
                        _videos.Clear();
                    }

                    foreach (var video in newVideos.Where(video => _videos.All(existing => existing.Id != video.Id)))
                    {
                        _videos.Add(video);
                    }

                    _continuationToken = result.FeedPage.ContinuationToken;

                    if (_videos.Count == 0)
                    {
                        nextState = new HomeFeedState(
                            HomeFeedStateKind.Empty,
                            [],
                            Message: "No recommendations are available right now.",
                            IsLoading: false,
                            IsLoadingMore: false,
                            HasContinuation: false);
                    }
                    else
                    {
                        nextState = new HomeFeedState(
                            HomeFeedStateKind.Ready,
                            _videos.ToArray(),
                            IsLoading: false,
                            IsLoadingMore: false,
                            HasContinuation: !string.IsNullOrEmpty(_continuationToken));
                    }

                    break;
                }
            }

            _stateVersion++;
            State = nextState;
            version = _stateVersion;
        }

        PublishStateWithVersion(nextState, version);
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
            _ = RefreshAsync();
        else
        {
            CancelAndClear();
            PublishState(HomeFeedState.SignedOut);
        }
    }

    private void CancelAndClear()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _isLoading = false;
            _videos.Clear();
            _continuationToken = null;
            _currentRequestId++;
        }
    }

    private void PublishState(HomeFeedState newState)
    {
        long version;
        lock (_lock)
        {
            _stateVersion++;
            State = newState;
            version = _stateVersion;
        }

        PublishStateWithVersion(newState, version);
    }

    private void PublishStateWithVersion(HomeFeedState stateToPublish, long version)
    {
        var shouldPublish = false;
        lock (_lock)
        {
            if (version == _stateVersion && version > _publishedStateVersion)
            {
                _publishedStateVersion = version;
                shouldPublish = true;
            }
        }

        if (shouldPublish)
            StateChanged?.Invoke(this, stateToPublish);
    }

    public void Dispose()
    {
        _sessionService.SessionChanged -= OnSessionChanged;
        CancelAndClear();
    }
}