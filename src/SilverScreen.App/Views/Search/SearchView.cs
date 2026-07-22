using Gtk;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Search;

public class SearchView : ViewBase<Box, SearchViewModel>
{
    private readonly List<VideoCardView> _cards = [];
    private readonly FlowBox _results;
    private readonly Label _summary;
    private readonly IThumbnailService _thumbnails;
    private readonly VideoCardActions _videoActions;
    private bool _disposed;
    private CancellationTokenSource? _thumbnailGeneration;

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

    public Task SubmitAsync(string text)
    {
        return ViewModel?.SubmitAsync(text) ?? Task.CompletedTask;
    }

    protected override void BindViewModel(SearchViewModel viewModel, BindingScope<SearchViewModel> bindings)
    {
        bindings.Bind(nameof(SearchViewModel.Summary), _summary, static model => model.Summary,
            static (label, value) => label.SetText(value));
    }

    private void OnStateChanged(object? sender, SearchViewState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
                Render(state);

            return false;
        });
    }

    private void Render(SearchViewState state)
    {
        _thumbnailGeneration?.Cancel();
        _thumbnailGeneration?.Dispose();
        _thumbnailGeneration = null;
        DisposeCards();
        Clear(_results);
        if (state.IsLoading || state.Videos.Count == 0)
            return;

        _thumbnailGeneration = new CancellationTokenSource();
        foreach (var video in state.Videos)
        {
            var card = new VideoCardView(_thumbnails, _videoActions);
            card.Bind(video, _thumbnailGeneration.Token);
            _cards.Add(card);
            var cardWidget = card.Widget;
            _results.Append(cardWidget);
            if (cardWidget.GetParent() is not FlowBoxChild flowBoxChild) continue;
            flowBoxChild.Halign = Align.Center;
            flowBoxChild.Valign = Align.Start;
        }
    }

    private void DisposeCards()
    {
        foreach (var card in _cards)
            card.Dispose();

        _cards.Clear();
    }

    private static void Clear(FlowBox flowBox)
    {
        while (flowBox.GetFirstChild() is { } child)
            flowBox.Remove(child);
    }

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _thumbnailGeneration?.Cancel();
        _thumbnailGeneration?.Dispose();
        _thumbnailGeneration = null;
        DisposeCards();
        if (ViewModel is { } viewModel)
        {
            viewModel.StateChanged -= OnStateChanged;
            viewModel.Dispose();
        }

        base.Dispose();
    }
}