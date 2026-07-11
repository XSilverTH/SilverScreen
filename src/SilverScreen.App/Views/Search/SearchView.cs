using Gtk;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Search;

public partial class SearchView : ViewBase<Box, SearchViewModel>
{
    private readonly IThumbnailService _thumbnails;
    private readonly VideoCardActions _videoActions;
    private readonly Label _summary;
    private readonly FlowBox _results;
    private CancellationTokenSource? _thumbnailGeneration;
    private bool _disposed;

    public SearchView(SearchViewModel viewModel, IThumbnailService thumbnails, VideoCardActions videoActions)
    {
        _thumbnails = thumbnails;
        _videoActions = videoActions;
        _summary = GetRequiredObject<Label>("search_summary_label");
        _results = GetRequiredObject<FlowBox>("search_results_flow_box");
        viewModel.StateChanged += OnStateChanged;
        ViewModel = viewModel;
        Render(viewModel.State);
    }

    public Task SubmitAsync(string text) => ViewModel?.SubmitAsync(text) ?? Task.CompletedTask;

    protected override void BindViewModel(SearchViewModel viewModel, BindingScope<SearchViewModel> bindings)
    {
        bindings.Bind(nameof(SearchViewModel.Summary), _summary, static model => model.Summary,
            static (label, value) => label.SetText(value));
    }

    private void OnStateChanged(object? sender, SearchViewState state)
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

    private void Render(SearchViewState state)
    {
        _thumbnailGeneration?.Cancel();
        _thumbnailGeneration?.Dispose();
        _thumbnailGeneration = null;
        Clear(_results);
        if (state.IsLoading || state.Videos.Count == 0)
        {
            return;
        }

        _thumbnailGeneration = new CancellationTokenSource();
        foreach (var video in state.Videos)
        {
            _results.Append(new VideoCardView(video, _thumbnails, _videoActions, _thumbnailGeneration.Token).Widget);
        }
    }

    private static void Clear(FlowBox flowBox)
    {
        while (flowBox.GetFirstChild() is { } child)
        {
            flowBox.Remove(child);
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
        if (ViewModel is { } viewModel)
        {
            viewModel.StateChanged -= OnStateChanged;
            viewModel.Dispose();
        }

        base.Dispose();
    }
}
