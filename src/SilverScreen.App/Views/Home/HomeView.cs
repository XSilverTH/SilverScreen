using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Home;

public partial class HomeView : ViewBase<Box>
{
    private readonly HomeViewModel _viewModel;
    private readonly IThumbnailService _thumbnails;
    private readonly VideoCardActions _videoActions;
    private readonly Box _content;
    private readonly Button _refreshButton;
    private CancellationTokenSource? _thumbnailGeneration;
    private bool _disposed;

    public HomeView(HomeViewModel viewModel, IThumbnailService thumbnails, VideoCardActions videoActions)
    {
        _viewModel = viewModel;
        _thumbnails = thumbnails;
        _videoActions = videoActions;
        _content = GetRequiredObject<Box>("home_content");
        _refreshButton = GetRequiredObject<Button>("home_refresh_button");
        _viewModel.StateChanged += OnStateChanged;
        Render(_viewModel.State);
    }

    private void OnHomeRefreshButtonClicked(object? sender, EventArgs args) => _ = _viewModel.RefreshAsync();

    private void OnStateChanged(object? sender, HomeFeedState state)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
            {
                Render(state);
            }

            return false;
        });
    }

    private void Render(HomeFeedState state)
    {
        _thumbnailGeneration?.Cancel();
        _thumbnailGeneration?.Dispose();
        _thumbnailGeneration = null;
        Clear(_content);
        _refreshButton.Sensitive = state.Kind != HomeFeedStateKind.SignedOut && !state.IsLoading && !state.IsLoadingMore;

        var videos = state.Videos.Where(video => !video.IsShort).GroupBy(video => video.Id).Select(group => group.First()).ToArray();
        if (videos.Length == 0)
        {
            _content.Append(state.Kind switch
            {
                HomeFeedStateKind.SignedOut => StatusPage("Home", "Sign in to see your YouTube recommendations.", "avatar-default-symbolic"),
                HomeFeedStateKind.InitialLoading => LoadingPage(),
                HomeFeedStateKind.Empty => StatusPage("Home", "No recommendations are available right now.", "applications-internet-symbolic"),
                HomeFeedStateKind.AuthenticationRequired => StatusPage("Home", "Your YouTube session is no longer valid.", "dialog-password-symbolic"),
                _ => StatusPage("Home", "Could not load YouTube recommendations.", "network-error-symbolic")
            });
            return;
        }

        _thumbnailGeneration = new CancellationTokenSource();
        if (state.IsLoading || state.IsLoadingMore)
        {
            _content.Append(LoadingRow(state.IsLoadingMore ? "Loading more recommendations…" : "Loading YouTube recommendations…"));
        }

        if (!string.IsNullOrEmpty(state.Message))
        {
            _content.Append(DimLabel(state.Message));
        }

        var grid = VideoGrid();
        foreach (var video in videos)
        {
            grid.Append(new VideoCardView(video, _thumbnails, _videoActions, _thumbnailGeneration.Token).Widget);
        }

        _content.Append(grid);
        if (state.HasContinuation)
        {
            var loadMore = Button.NewWithLabel(state.IsLoadingMore ? "Loading more…" : "Load more");
            loadMore.Halign = Align.Center;
            loadMore.MarginBottom = 24;
            loadMore.Sensitive = !state.IsLoading && !state.IsLoadingMore;
            loadMore.OnClicked += async (_, _) => await _viewModel.LoadMoreAsync();
            _content.Append(loadMore);
        }
    }

    private static FlowBox VideoGrid()
    {
        var grid = FlowBox.New();
        grid.SelectionMode = SelectionMode.None;
        grid.ActivateOnSingleClick = false;
        grid.ColumnSpacing = 18;
        grid.RowSpacing = 22;
        grid.MinChildrenPerLine = 1;
        grid.MaxChildrenPerLine = 4;
        grid.MarginStart = 18;
        grid.MarginEnd = 18;
        grid.MarginTop = 18;
        grid.MarginBottom = 96;
        grid.Hexpand = true;
        grid.Vexpand = true;
        return grid;
    }

    private static Widget LoadingPage()
    {
        var content = Box.New(Orientation.Vertical, 12);
        content.Halign = Align.Center;
        content.Valign = Align.Center;
        content.Hexpand = true;
        content.Vexpand = true;
        var spinner = Spinner.New();
        spinner.Halign = Align.Center;
        spinner.Spinning = true;
        content.Append(spinner);
        content.Append(DimLabel("Loading YouTube recommendations…"));
        return content;
    }

    private static Widget LoadingRow(string message)
    {
        var row = Box.New(Orientation.Horizontal, 8);
        row.MarginStart = 18;
        row.MarginEnd = 18;
        row.MarginTop = 12;
        var spinner = Spinner.New();
        spinner.Spinning = true;
        row.Append(spinner);
        row.Append(DimLabel(message));
        return row;
    }

    private static Widget StatusPage(string title, string description, string icon)
    {
        var page = Adw.StatusPage.New();
        page.Title = title;
        page.Description = description;
        page.IconName = icon;
        page.Hexpand = true;
        page.Vexpand = true;
        return page;
    }

    private static Label DimLabel(string text)
    {
        var label = Label.New(text);
        label.Xalign = 0;
        label.Wrap = true;
        label.CssClasses = ["dim-label"];
        return label;
    }

    private static void Clear(Box box)
    {
        while (box.GetFirstChild() is { } child)
        {
            box.Remove(child);
        }
    }

    public new void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _thumbnailGeneration?.Cancel();
        _thumbnailGeneration?.Dispose();
        _thumbnailGeneration = null;
        _viewModel.StateChanged -= OnStateChanged;
        _viewModel.Dispose();
        base.Dispose();
    }
}
