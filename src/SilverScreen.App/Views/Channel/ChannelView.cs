using System.Runtime.CompilerServices;
using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;

namespace SilverScreen.Views.Channel;

public partial class ChannelView : ViewBase<Box>
{
    private const int AvatarSize = 80;
    private const double CollapseThreshold = 50.0;
    private const double TopRevealThreshold = 1.0;
    private const long LayoutStabilizationMs = 350;
    private static readonly ILogger Logger = Log.ForContext<ChannelView>();

    private readonly Action? _backCallback;
    private readonly ConditionalWeakTable<ListItem, VideoCardView> _cardsByListItem = new();

    private readonly GestureClick _clueClickGesture;
    private readonly EventControllerMotion _clueMotionController;
    
    private readonly EventControllerScroll _scrollController;
    
    private readonly IThumbnailService _thumbnails;
    private readonly Adjustment? _vadjustment;
    private readonly VideoCardActions _videoActions;
    private readonly SignalListItemFactory _videoFactory;
    private readonly StringList _videoIds;
    private readonly NoSelection _videoSelection;
    private readonly Dictionary<string, VideoSummary> _videosById = [];
    private readonly ChannelViewModel _viewModel;
    private readonly IWatchProgressService _watchProgress;
    private int _avatarBindingGeneration;
    private CancellationTokenSource? _avatarCancellation;
    private Picture? _boundAvatarPicture;
    private Texture? _boundAvatarTexture;
    private string? _currentAvatarUrl;

    private VideoSummary[] _displayedVideos = [];
    private bool _disposed;

    private bool _isHeaderCollapsed;
    private bool _isUserScrollingUp;
    private long _lastHeaderStateChangeTicks;
    private double _lastScrollY;
    private long _lastUserScrollTicks;
    private bool _updatingSortDropdown;

    public ChannelView(
        ChannelViewModel viewModel,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions,
        Action? backCallback = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _watchProgress = watchProgress ?? throw new ArgumentNullException(nameof(watchProgress));
        _videoActions = videoActions ?? throw new ArgumentNullException(nameof(videoActions));
        _backCallback = backCallback;


        _vadjustment = channel_scrolled_window.Vadjustment;
        if (_vadjustment is not null) _vadjustment.OnValueChanged += OnScrollValueChanged;
        _scrollController = EventControllerScroll.New(EventControllerScrollFlags.Vertical);
        _scrollController.SetPropagationPhase(PropagationPhase.Capture);
        _scrollController.OnScroll += OnScrollEvent;
        channel_scrolled_window.AddController(_scrollController);


        _clueMotionController = EventControllerMotion.New();
        _clueMotionController.OnEnter += OnCluePointerEnter;
        _clueMotionController.OnMotion += OnCluePointerMotion;
        channel_reveal_clue.AddController(_clueMotionController);

        _clueClickGesture = GestureClick.New();
        _clueClickGesture.Button = 1;
        _clueClickGesture.OnPressed += OnClueClicked;
        channel_reveal_clue.AddController(_clueClickGesture);

        _videoIds = StringList.New([]);
        _videoSelection = NoSelection.New(_videoIds);
        _videoFactory = SignalListItemFactory.New();
        _videoFactory.OnSetup += OnVideoCardSetup;
        _videoFactory.OnBind += OnVideoCardBind;
        _videoFactory.OnUnbind += OnVideoCardUnbind;
        _videoFactory.OnTeardown += OnVideoCardTeardown;
        channel_video_grid.Model = _videoSelection;
        channel_video_grid.Factory = _videoFactory;

        _viewModel.StateChanged += OnStateChanged;
        Render(_viewModel.State);
    }

    public bool IsLoading => _viewModel.State is { IsLoading: true } or { IsLoadingMore: true };

    public event EventHandler? BackRequested;

    public event EventHandler<bool>? RefreshLoadingChanged;

    public Task RefreshAsync()
    {
        return _viewModel.RefreshAsync();
    }

    private void OnBackButtonClicked(object? sender = null, EventArgs? args = null)
    {
        _backCallback?.Invoke();
        BackRequested?.Invoke(this, EventArgs.Empty);
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
                break;
        }

        return false;
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (_disposed || _vadjustment is null) return;

        var currentY = _vadjustment.Value;
        if (currentY + _vadjustment.PageSize >= _vadjustment.Upper - 240)
            _viewModel.LoadMoreAsync().FireAndForget(Logger);
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

    private void OnCluePointerEnter(object? sender, EventControllerMotion.EnterSignalArgs args)
    {
        RevealHeaderFromClue();
    }

    private void OnCluePointerMotion(object? sender, EventControllerMotion.MotionSignalArgs args)
    {
        RevealHeaderFromClue();
    }

    private void OnClueClicked(object? sender, GestureClick.PressedSignalArgs args)
    {
        RevealHeaderFromClue();
    }

    private void RevealHeaderFromClue()
    {
        if (_disposed || !_isHeaderCollapsed) return;

        SetHeaderCollapsed(false);
    }

    private void SetHeaderCollapsed(bool collapsed)
    {
        _isHeaderCollapsed = collapsed;
        channel_header_revealer.RevealChild = !collapsed;
        channel_clue_revealer.RevealChild = collapsed;
        _lastHeaderStateChangeTicks = Environment.TickCount64;
        if (_vadjustment is not null) _lastScrollY = _vadjustment.Value;
    }

    private void OnStateChanged(object? sender, ChannelViewState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (_disposed) return false;
            RefreshLoadingChanged?.Invoke(this, state.IsLoading || state.IsLoadingMore);
            Render(state);

            return false;
        });
    }

    private void Render(ChannelViewState state)
    {
        // 1. Metadata & Avatar
        channel_name.SetText(state.Name);
        channel_bar_title.SetText(string.IsNullOrWhiteSpace(state.Name) ? "Channel" : state.Name);

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

        // 3. State page & Grid rendering
        if (state.IsLoading)
        {
            channel_stack.VisibleChildName = "loading";
        }
        else if (!state.IsSuccess)
        {
            channel_error_page.Description = string.IsNullOrWhiteSpace(state.Summary)
                ? "Failed to load channel details."
                : state.Summary;
            channel_stack.VisibleChildName = "error";
        }
        else if (state.Videos.Count == 0)
        {
            channel_empty_page.Description = string.IsNullOrWhiteSpace(state.Summary)
                ? "This channel has no public videos available right now."
                : state.Summary;
            channel_stack.VisibleChildName = "empty";
        }
        else
        {
            ApplyVideos(state.Videos);
            channel_pagination_loading_revealer.RevealChild = state.IsLoadingMore;
            channel_stack.VisibleChildName = "content";
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
            await _viewModel.SetSortSelection(selected);
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


    private void ApplyVideos(IReadOnlyList<VideoSummary> videos)
    {
        var nextVideos = videos.ToArray();
        var prefixLength = 0;
        while (prefixLength < _displayedVideos.Length && prefixLength < nextVideos.Length &&
               _displayedVideos[prefixLength] == nextVideos[prefixLength])
            prefixLength++;

        var suffixLength = 0;
        while (_displayedVideos.Length - suffixLength > prefixLength &&
               nextVideos.Length - suffixLength > prefixLength &&
               _displayedVideos[_displayedVideos.Length - suffixLength - 1] ==
               nextVideos[nextVideos.Length - suffixLength - 1])
            suffixLength++;

        var removedMiddleCount = _displayedVideos.Length - prefixLength - suffixLength;
        var addedMiddleCount = nextVideos.Length - prefixLength - suffixLength;
        _videosById.Clear();
        foreach (var video in nextVideos)
            _videosById[video.Id] = video;

        _displayedVideos = nextVideos;
        if (removedMiddleCount == 0 && addedMiddleCount == 0)
            return;

        var addedMiddleIds = nextVideos.Skip(prefixLength).Take(addedMiddleCount).Select(video => video.Id).ToArray();
        _videoIds.Splice((uint)prefixLength, (uint)removedMiddleCount, addedMiddleIds);
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

    private void OnVideoCardSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem) return;

        var card = new VideoCardView(_thumbnails, _watchProgress, _videoActions);
        listItem.Child = card.Widget;
        _cardsByListItem.Add(listItem, card);
    }

    private void OnVideoCardBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Item: StringObject { String: { } id } } listItem ||
            !_cardsByListItem.TryGetValue(listItem, out var card) ||
            !_videosById.TryGetValue(id, out var video))
            return;

        card.Bind(video);
    }

    private void OnVideoCardUnbind(object? sender, SignalListItemFactory.UnbindSignalArgs args)
    {
        if (args.Object is ListItem listItem && _cardsByListItem.TryGetValue(listItem, out var card))
            card.Unbind();
    }

    private void OnVideoCardTeardown(object? sender, SignalListItemFactory.TeardownSignalArgs args)
    {
        if (args.Object is not ListItem listItem || !_cardsByListItem.TryGetValue(listItem, out var card))
            return;

        _cardsByListItem.Remove(listItem);
        DisposeVideoCardCell(listItem, card);
    }

    private static void DisposeVideoCardCell(ListItem listItem, VideoCardView card)
    {
        listItem.Child = null;
        card.Dispose();
    }

    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_vadjustment is not null) _vadjustment.OnValueChanged -= OnScrollValueChanged;
        _scrollController.OnScroll -= OnScrollEvent;
        channel_scrolled_window.RemoveController(_scrollController);
        _scrollController.Dispose();


        _clueMotionController.OnEnter -= OnCluePointerEnter;
        _clueMotionController.OnMotion -= OnCluePointerMotion;
        channel_reveal_clue.RemoveController(_clueMotionController);
        _clueMotionController.Dispose();

        _clueClickGesture.OnPressed -= OnClueClicked;
        channel_reveal_clue.RemoveController(_clueClickGesture);
        _clueClickGesture.Dispose();

        _viewModel.StateChanged -= OnStateChanged;

        _avatarCancellation?.Cancel();
        _avatarCancellation?.Dispose();
        _avatarCancellation = null;
        ClearAvatar();

        channel_video_grid.Factory = null;
        foreach (var association in _cardsByListItem)
            DisposeVideoCardCell(association.Key, association.Value);
        _cardsByListItem.Clear();

        _videoFactory.OnSetup -= OnVideoCardSetup;
        _videoFactory.OnBind -= OnVideoCardBind;
        _videoFactory.OnUnbind -= OnVideoCardUnbind;
        _videoFactory.OnTeardown -= OnVideoCardTeardown;

        channel_video_grid.Dispose();
        _videoSelection.Dispose();
        _videoFactory.Dispose();
        _videoIds.Dispose();

        base.Dispose();
    }
}