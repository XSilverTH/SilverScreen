using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Common;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;

namespace SilverScreen.Browsing.Channel;

public partial class ChannelView : ViewBase<Box>
{
    private const int AvatarSize = 80;
    private const double CollapseThreshold = 50.0;
    private const double TopRevealThreshold = 1.0;
    private const double TopHoverZoneThreshold = 40.0;
    private const long LayoutStabilizationMs = 350;
    private static readonly ILogger Logger = Log.ForContext<ChannelView>();
    private readonly EventControllerScroll _scrollController;
    private readonly IThumbnailService _thumbnails;
    private readonly Adjustment? _vadjustment;
    private readonly VideoListView _videoList;

    private readonly EventControllerMotion _videoMotionController;
    private readonly ChannelViewModel _viewModel;

    private int _avatarBindingGeneration;
    private CancellationTokenSource? _avatarCancellation;
    private Picture? _boundAvatarPicture;
    private Texture? _boundAvatarTexture;
    private string? _currentAvatarUrl;
    private bool _disposed;

    private bool _isHeaderCollapsed;
    private bool _isUserScrollingUp;
    private long _lastHeaderStateChangeTicks;
    private long _lastScrollDownTicks;
    private double _lastScrollY;
    private long _lastUserScrollTicks;
    private bool _updatingSortDropdown;

    public ChannelView(
        ChannelViewModel viewModel,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));

        _videoList = new VideoListView(
            viewModel,
            thumbnails,
            watchProgress ?? throw new ArgumentNullException(nameof(watchProgress)),
            videoActions ?? throw new ArgumentNullException(nameof(videoActions)));
        _videoList.RefreshLoadingChanged += OnVideoListRefreshLoadingChanged;
        channel_video_list_host.Append(_videoList.Widget);

        _vadjustment = _videoList.Vadjustment;
        if (_vadjustment is not null)
            _vadjustment.OnValueChanged += OnScrollValueChanged;

        _scrollController = EventControllerScroll.New(EventControllerScrollFlags.Vertical);
        _scrollController.SetPropagationPhase(PropagationPhase.Capture);
        _scrollController.OnScroll += OnScrollEvent;
        _videoList.ScrolledWindow.AddController(_scrollController);

        _videoMotionController = EventControllerMotion.New();
        _videoMotionController.SetPropagationPhase(PropagationPhase.Capture);
        _videoMotionController.OnEnter += OnVideoPointerEnter;
        _videoMotionController.OnMotion += OnVideoPointerMotion;
        _videoList.ScrolledWindow.AddController(_videoMotionController);

        _viewModel.StateChanged += OnStateChanged;
        Render(_viewModel.State);
    }

    public bool IsLoading => _videoList.IsLoading;

    public event EventHandler<bool>? RefreshLoadingChanged;

    public int GetBatchSize()
    {
        return _videoList.GetBatchSize();
    }

    public void ScrollToTop()
    {
        if (_disposed) return;
        _videoList.ScrollToTop();
        SetHeaderCollapsed(false);
    }

    public async Task RefreshAsync()
    {
        try
        {
            await _videoList.RefreshAsync().ConfigureAwait(false);
        }
        finally
        {
            Functions.IdleAdd(0, () =>
            {
                if (!_disposed)
                    SetHeaderCollapsed(false);

                return false;
            });
        }
    }

    private void OnVideoListRefreshLoadingChanged(object? sender, bool isLoading)
    {
        RefreshLoadingChanged?.Invoke(this, isLoading);
    }

    private bool OnScrollEvent(EventControllerScroll sender, EventControllerScroll.ScrollSignalArgs args)
    {
        if (_disposed) return false;

        switch (args.Dy)
        {
            case < -0.01:
                _isUserScrollingUp = true;
                _lastUserScrollTicks = Environment.TickCount64;
                break;
            case > 0.01:
                _isUserScrollingUp = false;
                _lastUserScrollTicks = Environment.TickCount64;
                _lastScrollDownTicks = Environment.TickCount64;
                break;
        }

        return false;
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (_disposed || _vadjustment is null) return;

        var currentY = _vadjustment.Value;
        var now = Environment.TickCount64;

        if (now - _lastHeaderStateChangeTicks < LayoutStabilizationMs)
        {
            _lastScrollY = currentY;
            return;
        }

        if (!_isHeaderCollapsed)
        {
            if (currentY > CollapseThreshold && currentY > _lastScrollY) SetHeaderCollapsed(true);
        }
        else
        {
            var reachesTop = currentY <= TopRevealThreshold;
            var userMovingUp = currentY < _lastScrollY || (_isUserScrollingUp && now - _lastUserScrollTicks < 500);

            if (reachesTop && userMovingUp) SetHeaderCollapsed(false);
        }

        _lastScrollY = currentY;
    }

    private void OnVideoPointerEnter(EventControllerMotion sender, EventControllerMotion.EnterSignalArgs args)
    {
        CheckHoverReveal(args.Y);
    }

    private void OnVideoPointerMotion(EventControllerMotion sender, EventControllerMotion.MotionSignalArgs args)
    {
        CheckHoverReveal(args.Y);
    }

    private void CheckHoverReveal(double y)
    {
        if (_disposed || !_isHeaderCollapsed) return;

        var now = Environment.TickCount64;
        if (now - _lastHeaderStateChangeTicks < LayoutStabilizationMs) return;
        if (now - _lastScrollDownTicks < 300) return;

        if (y is >= 0 and <= TopHoverZoneThreshold) SetHeaderCollapsed(false);
    }

    private void SetHeaderCollapsed(bool collapsed)
    {
        _isHeaderCollapsed = collapsed;
        channel_header_revealer.RevealChild = !collapsed;
        _lastHeaderStateChangeTicks = Environment.TickCount64;
        if (_vadjustment is not null) _lastScrollY = _vadjustment.Value;
    }

    private void OnStateChanged(object? sender, ChannelViewState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (_disposed) return false;
            Render(state);

            return false;
        });
    }

    private void Render(ChannelViewState state)
    {
        // 1. Metadata & Avatar
        channel_name.SetText(state.Name);

        if (!string.IsNullOrWhiteSpace(state.Url))
        {
            channel_handle.SetText(state.Url);
            channel_handle.Visible = true;
        }
        else
        {
            channel_handle.Visible = false;
        }

        if (state.SubscriberCount is { } subsCount)
        {
            channel_subscribers.SetText(FormatSubscriberCount(subsCount));
            channel_subscribers.Visible = true;
        }
        else
        {
            channel_subscribers.Visible = false;
        }

        channel_meta_dot1.Visible = channel_handle.Visible && channel_subscribers.Visible;

        if (!string.IsNullOrWhiteSpace(state.Description))
        {
            channel_description.SetText(state.Description);
            channel_description.Visible = true;
        }
        else
        {
            channel_description.Visible = false;
        }

        if (!string.Equals(_currentAvatarUrl, state.AvatarUrl, StringComparison.Ordinal))
        {
            _currentAvatarUrl = state.AvatarUrl;
            if (!string.IsNullOrWhiteSpace(state.AvatarUrl))
            {
                var gen = ++_avatarBindingGeneration;
                _avatarCancellation?.Cancel();
                _avatarCancellation?.Dispose();
                _avatarCancellation = new CancellationTokenSource();
                LoadAvatarAsync(state.AvatarUrl, gen, _avatarCancellation.Token).FireAndForget(Logger);
            }
            else
            {
                ClearAvatar();
            }
        }

        // 2. Sort Dropdown synchronization
        _updatingSortDropdown = true;
        try
        {
            var sortIndex = state.Sort switch
            {
                ChannelVideoSort.Oldest => 1u,
                ChannelVideoSort.Popular => 2u,
                _ => 0u
            };
            if (channel_sort_dropdown.Selected != sortIndex) channel_sort_dropdown.Selected = sortIndex;
        }
        finally
        {
            _updatingSortDropdown = false;
        }
    }

    private static string FormatSubscriberCount(long count)
    {
        return count switch
        {
            >= 1_000_000 => $"{count / 1_000_000.0:0.#}M subscribers",
            >= 1_000 => $"{count / 1_000.0:0.#}K subscribers",
            _ => count == 1 ? "1 subscriber" : $"{count:N0} subscribers"
        };
    }

    private async void OnSortDropdownNotify(object? sender = null, EventArgs? args = null)
    {
        try
        {
            if (_updatingSortDropdown || _disposed) return;

            var selected = channel_sort_dropdown.Selected;
            await _viewModel.SetSortSelection(selected, GetBatchSize());
        }
        catch (OperationCanceledException)
        {
            // Ignore cancelled sort requests
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to update channel sort selection");
        }
    }

    private async Task LoadAvatarAsync(string avatarUrl, int generation, CancellationToken cancellationToken)
    {
        Pixbuf? pixbuf;
        try
        {
            var result = await _thumbnails.GetThumbnailAsync(avatarUrl, cancellationToken).ConfigureAwait(false);
            if (result is null) return;

            pixbuf = await Task.Run(
                () => Pixbuf.NewFromFileAtScale(result.LocalPath, AvatarSize, AvatarSize, true),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load avatar from {AvatarUrl}", avatarUrl);
            return;
        }

        var decodedPixbuf = pixbuf ?? throw new InvalidOperationException("Avatar decode returned no pixbuf.");

        Functions.IdleAdd(0, () =>
        {
            try
            {
                if (_disposed || cancellationToken.IsCancellationRequested || _avatarBindingGeneration != generation ||
                    channel_avatar_overlay.GetRoot() is null)
                    return false;

                Texture? texture = null;
                Picture? picture = null;
                try
                {
                    var pixbufForTexture = decodedPixbuf ??
                                           throw new InvalidOperationException(
                                               "Avatar decode was released before texture creation.");
                    texture = Texture.NewForPixbuf(pixbufForTexture);
                    pixbufForTexture.Dispose();
                    decodedPixbuf = null;

                    picture = Picture.NewForPaintable(texture);
                    picture.AlternativeText = $"{channel_name.GetText()} avatar";
                    picture.ContentFit = ContentFit.Cover;
                    picture.WidthRequest = AvatarSize;
                    picture.HeightRequest = AvatarSize;

                    ClearAvatar();
                    channel_avatar_overlay.Child = picture;
                    _boundAvatarTexture = texture;
                    _boundAvatarPicture = picture;
                    texture = null;
                    picture = null;
                }
                finally
                {
                    picture?.Dispose();
                    texture?.Dispose();
                }
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Failed to render channel avatar texture from {AvatarUrl}", avatarUrl);
                // Leave placeholder intact on decode error
            }
            finally
            {
                decodedPixbuf?.Dispose();
            }

            return false;
        });
    }

    private void ClearAvatar()
    {
        var picture = _boundAvatarPicture;
        var texture = _boundAvatarTexture;
        _boundAvatarPicture = null;
        _boundAvatarTexture = null;
        channel_avatar_overlay.Child = channel_avatar_placeholder;
        if (picture is not null)
        {
            picture.Paintable = null!;
            picture.Dispose();
        }

        texture?.Dispose();
    }

    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_vadjustment is not null) _vadjustment.OnValueChanged -= OnScrollValueChanged;
        _scrollController.OnScroll -= OnScrollEvent;
        _videoList.ScrolledWindow.RemoveController(_scrollController);
        _scrollController.Dispose();

        _videoMotionController.OnEnter -= OnVideoPointerEnter;
        _videoMotionController.OnMotion -= OnVideoPointerMotion;
        _videoList.ScrolledWindow.RemoveController(_videoMotionController);
        _videoMotionController.Dispose();
        _viewModel.StateChanged -= OnStateChanged;

        _avatarCancellation?.Cancel();
        _avatarCancellation?.Dispose();
        _avatarCancellation = null;
        ClearAvatar();

        _videoList.RefreshLoadingChanged -= OnVideoListRefreshLoadingChanged;
        _videoList.Dispose();

        base.Dispose();
    }
}