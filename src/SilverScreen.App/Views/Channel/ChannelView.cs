using System.Runtime.CompilerServices;
using Adw;
using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;

namespace SilverScreen.Views.Channel;
public partial class ChannelView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<ChannelView>();
    private const int AvatarSize = 80;

    private readonly Action? _backCallback;
    private readonly Button _backButton;
    private readonly Label _barTitle;
    private readonly Overlay _avatarOverlay;
    private readonly Widget _avatarPlaceholder;
    private readonly Label _channelName;
    private readonly Label _channelHandle;
    private readonly Label _subscribersLabel;
    private readonly Label _metaDot1;
    private readonly Label _descriptionLabel;
    private readonly DropDown _sortDropdown;
    private readonly Stack _stack;
    private readonly GridView _videoGrid;
    private readonly StatusPage _errorPage;
    private readonly StatusPage _emptyPage;
    private readonly Revealer _headerRevealer;
    private readonly Revealer _clueRevealer;
    private readonly Box _revealClue;
    private readonly ScrolledWindow _scrolledWindow;
    private readonly Adjustment? _vadjustment;
    private readonly EventControllerScroll _scrollController;
    private readonly EventControllerMotion _clueMotionController;
    private readonly GestureClick _clueClickGesture;
    private readonly Revealer _paginationLoadingRevealer;

    private bool _isHeaderCollapsed;
    private double _lastScrollY;
    private bool _isUserScrollingUp;
    private long _lastUserScrollTicks;
    private long _lastHeaderStateChangeTicks;
    private const double CollapseThreshold = 50.0;
    private const double TopRevealThreshold = 1.0;
    private const long LayoutStabilizationMs = 350;
    private readonly ChannelViewModel _viewModel;
    private readonly IThumbnailService _thumbnails;
    private readonly IWatchProgressService _watchProgress;
    private readonly VideoCardActions _videoActions;

    private readonly ConditionalWeakTable<ListItem, VideoCardView> _cardsByListItem = new();
    private readonly SignalListItemFactory _videoFactory;
    private readonly StringList _videoIds;
    private readonly NoSelection _videoSelection;
    private readonly Dictionary<string, VideoSummary> _videosById = [];

    private VideoSummary[] _displayedVideos = [];
    private bool _disposed;
    private bool _updatingSortDropdown;
    private string? _currentAvatarUrl;
    private int _avatarBindingGeneration;
    private CancellationTokenSource? _avatarCancellation;
    private Picture? _boundAvatarPicture;
    private Texture? _boundAvatarTexture;

    public event EventHandler? BackRequested;

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

        _backButton = GetRequiredObject<Button>("channel_back_button");
        _barTitle = GetRequiredObject<Label>("channel_bar_title");
        _avatarOverlay = GetRequiredObject<Overlay>("channel_avatar_overlay");
        _avatarPlaceholder = GetRequiredObject<Widget>("channel_avatar_placeholder");
        _channelName = GetRequiredObject<Label>("channel_name");
        _channelHandle = GetRequiredObject<Label>("channel_handle");
        _subscribersLabel = GetRequiredObject<Label>("channel_subscribers");
        _metaDot1 = GetRequiredObject<Label>("channel_meta_dot1");
        _descriptionLabel = GetRequiredObject<Label>("channel_description");
        _sortDropdown = GetRequiredObject<DropDown>("channel_sort_dropdown");
        _stack = GetRequiredObject<Stack>("channel_stack");
        _videoGrid = GetRequiredObject<GridView>("channel_video_grid");
        _emptyPage = GetRequiredObject<StatusPage>("channel_empty_page");
        _errorPage = GetRequiredObject<StatusPage>("channel_error_page");
        _headerRevealer = GetRequiredObject<Revealer>("channel_header_revealer");
        _clueRevealer = GetRequiredObject<Revealer>("channel_clue_revealer");
        _revealClue = GetRequiredObject<Box>("channel_reveal_clue");
        _scrolledWindow = GetRequiredObject<ScrolledWindow>("channel_scrolled_window");
        _paginationLoadingRevealer = GetRequiredObject<Revealer>("channel_pagination_loading_revealer");

        _vadjustment = _scrolledWindow.Vadjustment;
        if (_vadjustment is not null)
        {
            _vadjustment.OnValueChanged += OnScrollValueChanged;
        }
        _scrollController = EventControllerScroll.New(EventControllerScrollFlags.Vertical);
        _scrollController.SetPropagationPhase(PropagationPhase.Capture);
        _scrollController.OnScroll += OnScrollEvent;
        _scrolledWindow.AddController(_scrollController);


        _clueMotionController = EventControllerMotion.New();
        _clueMotionController.OnEnter += OnCluePointerEnter;
        _clueMotionController.OnMotion += OnCluePointerMotion;
        _revealClue.AddController(_clueMotionController);

        _clueClickGesture = GestureClick.New();
        _clueClickGesture.Button = 1;
        _clueClickGesture.OnPressed += OnClueClicked;
        _revealClue.AddController(_clueClickGesture);

        _videoIds = StringList.New([]);
        _videoSelection = NoSelection.New(_videoIds);
        _videoFactory = SignalListItemFactory.New();
        _videoFactory.OnSetup += OnVideoCardSetup;
        _videoFactory.OnBind += OnVideoCardBind;
        _videoFactory.OnUnbind += OnVideoCardUnbind;
        _videoFactory.OnTeardown += OnVideoCardTeardown;
        _videoGrid.Model = _videoSelection;
        _videoGrid.Factory = _videoFactory;

        _viewModel.StateChanged += OnStateChanged;
        Render(_viewModel.State);
    }

    public bool IsLoading => _viewModel.State is { IsLoading: true } or { IsLoadingMore: true };

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

        if (args.Dy < -0.01)
        {
            _isUserScrollingUp = true;
            _lastUserScrollTicks = Environment.TickCount64;
        }
        else if (args.Dy > 0.01)
        {
            _isUserScrollingUp = false;
            _lastUserScrollTicks = Environment.TickCount64;
        }

        return false;
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (_disposed || _vadjustment is null) return;

        double currentY = _vadjustment.Value;
        if (currentY + _vadjustment.PageSize >= _vadjustment.Upper - 240)
            _ = _viewModel.LoadMoreAsync();
        long now = Environment.TickCount64;

        if ((now - _lastHeaderStateChangeTicks) < LayoutStabilizationMs)
        {
            _lastScrollY = currentY;
            return;
        }

        if (!_isHeaderCollapsed)
        {
            if (currentY > CollapseThreshold && currentY > _lastScrollY)
            {
                SetHeaderCollapsed(true);
            }
        }
        else
        {
            bool reachesTop = currentY <= TopRevealThreshold;
            bool userMovingUp = currentY < _lastScrollY || (_isUserScrollingUp && (now - _lastUserScrollTicks) < 500);

            if (reachesTop && userMovingUp)
            {
                SetHeaderCollapsed(false);
            }
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
        _headerRevealer.RevealChild = !collapsed;
        _clueRevealer.RevealChild = collapsed;
        _lastHeaderStateChangeTicks = Environment.TickCount64;
        if (_vadjustment is not null)
        {
            _lastScrollY = _vadjustment.Value;
        }
    }

    private void OnStateChanged(object? sender, ChannelViewState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
            {
                RefreshLoadingChanged?.Invoke(this, state.IsLoading || state.IsLoadingMore);
                Render(state);
            }
            return false;
        });
    }

    private void Render(ChannelViewState state)
    {
        // 1. Metadata & Avatar
        _channelName.SetText(state.Name);
        _barTitle.SetText(string.IsNullOrWhiteSpace(state.Name) ? "Channel" : state.Name);

        if (!string.IsNullOrWhiteSpace(state.Url))
        {
            _channelHandle.SetText(state.Url);
            _channelHandle.Visible = true;
        }
        else
        {
            _channelHandle.Visible = false;
        }

        if (state.SubscriberCount is { } subsCount)
        {
            _subscribersLabel.SetText(FormatSubscriberCount(subsCount));
            _subscribersLabel.Visible = true;
        }
        else
        {
            _subscribersLabel.Visible = false;
        }

        _metaDot1.Visible = _channelHandle.Visible && _subscribersLabel.Visible;

        if (!string.IsNullOrWhiteSpace(state.Description))
        {
            _descriptionLabel.SetText(state.Description);
            _descriptionLabel.Visible = true;
        }
        else
        {
            _descriptionLabel.Visible = false;
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
                _ = LoadAvatarAsync(state.AvatarUrl, gen, _avatarCancellation.Token);
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
            if (_sortDropdown.Selected != sortIndex)
            {
                _sortDropdown.Selected = sortIndex;
            }
        }
        finally
        {
            _updatingSortDropdown = false;
        }

        // 3. State page & Grid rendering
        if (state.IsLoading)
        {
            _stack.VisibleChildName = "loading";
        }
        else if (!state.IsSuccess)
        {
            _errorPage.Description = string.IsNullOrWhiteSpace(state.Summary)
                ? "Failed to load channel details."
                : state.Summary;
            _stack.VisibleChildName = "error";
        }
        else if (state.Videos.Count == 0)
        {
            _emptyPage.Description = string.IsNullOrWhiteSpace(state.Summary)
                ? "This channel has no public videos available right now."
                : state.Summary;
            _stack.VisibleChildName = "empty";
        }
        else
        {
            ApplyVideos(state.Videos);
            _paginationLoadingRevealer.RevealChild = state.IsLoadingMore;
            _stack.VisibleChildName = "content";
        }
    }

    private static string FormatSubscriberCount(long count)
    {
        if (count >= 1_000_000)
            return $"{count / 1_000_000.0:0.#}M subscribers";
        if (count >= 1_000)
            return $"{count / 1_000.0:0.#}K subscribers";
        return count == 1 ? "1 subscriber" : $"{count:N0} subscribers";
    }

    private async void OnSortDropdownNotify(object? sender = null, EventArgs? args = null)
    {
        if (_updatingSortDropdown || _disposed) return;

        var selected = _sortDropdown.Selected;
        try
        {
            await _viewModel.SetSortSelection(selected);
        }
        catch
        {
            // Ignore cancelled or failed sort requests
        }
    }

    private void OnRetryButtonClicked(object? sender = null, EventArgs? args = null)
    {
        if (_viewModel.State.Url is { } url)
            _ = _viewModel.OpenChannelAsync(url, _viewModel.State.Name);
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
        catch
        {
            return;
        }

        var decodedPixbuf = pixbuf ?? throw new InvalidOperationException("Avatar decode returned no pixbuf.");

        Functions.IdleAdd(0, () =>
        {
            try
            {
                if (_disposed || cancellationToken.IsCancellationRequested || _avatarBindingGeneration != generation ||
                    _avatarOverlay.GetRoot() is null)
                    return false;

                Texture? texture = null;
                Picture? picture = null;
                try
                {
                    var pixbufForTexture = decodedPixbuf ??
                                           throw new InvalidOperationException("Avatar decode was released before texture creation.");
                    texture = Texture.NewForPixbuf(pixbufForTexture);
                    pixbufForTexture.Dispose();
                    decodedPixbuf = null;

                    picture = Picture.NewForPaintable(texture);
                    picture.AlternativeText = $"{_channelName.GetText()} avatar";
                    picture.ContentFit = ContentFit.Cover;
                    picture.WidthRequest = AvatarSize;
                    picture.HeightRequest = AvatarSize;

                    ClearAvatar();
                    _avatarOverlay.Child = picture;
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
            catch
            {
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
        _avatarOverlay.Child = _avatarPlaceholder;
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

        if (_vadjustment is not null)
        {
            _vadjustment.OnValueChanged -= OnScrollValueChanged;
        }
        _scrollController.OnScroll -= OnScrollEvent;
        _scrolledWindow.RemoveController(_scrollController);
        _scrollController.Dispose();


        _clueMotionController.OnEnter -= OnCluePointerEnter;
        _clueMotionController.OnMotion -= OnCluePointerMotion;
        _revealClue.RemoveController(_clueMotionController);
        _clueMotionController.Dispose();

        _clueClickGesture.OnPressed -= OnClueClicked;
        _revealClue.RemoveController(_clueClickGesture);
        _clueClickGesture.Dispose();

        _viewModel.StateChanged -= OnStateChanged;

        _avatarCancellation?.Cancel();
        _avatarCancellation?.Dispose();
        _avatarCancellation = null;
        ClearAvatar();

        _videoGrid.Factory = null;
        foreach (var association in _cardsByListItem)
            DisposeVideoCardCell(association.Key, association.Value);
        _cardsByListItem.Clear();

        _videoFactory.OnSetup -= OnVideoCardSetup;
        _videoFactory.OnBind -= OnVideoCardBind;
        _videoFactory.OnUnbind -= OnVideoCardUnbind;
        _videoFactory.OnTeardown -= OnVideoCardTeardown;

        _videoGrid.Dispose();
        _videoSelection.Dispose();
        _videoFactory.Dispose();
        _videoIds.Dispose();

        base.Dispose();
    }
}
