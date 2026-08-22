using System.Security;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.ViewModels;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Popovers;

public partial class SearchPopoverView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<SearchPopoverView>();

    private readonly SearchEntry _searchEntry;
    private readonly Revealer _suggestionsRevealer;
    private readonly ListBox _suggestionsList;
    private readonly EventControllerKey _searchKeyController;
    private readonly SearchViewModel _viewModel;
    private readonly Action<string> _submitCallback;
    private readonly Action _popdownAction;

    private CancellationTokenSource? _suggestionDebounceCts;
    private string[] _currentSuggestions = [];
    private int _selectedSuggestionIndex = -1;
    private string _originalTypedQuery = string.Empty;
    private bool _suppressSearchChanged;
    private bool _disposed;

    public SearchPopoverView(
        SearchViewModel viewModel,
        Action<string> submitCallback,
        Action popdownAction)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _submitCallback = submitCallback ?? throw new ArgumentNullException(nameof(submitCallback));
        _popdownAction = popdownAction ?? throw new ArgumentNullException(nameof(popdownAction));

        _searchEntry = GetRequiredObject<SearchEntry>("search_entry");
        _suggestionsRevealer = GetRequiredObject<Revealer>("suggestions_revealer");
        var suggestionsScroll = GetRequiredObject<ScrolledWindow>("suggestions_scroll");
        _suggestionsList = GetRequiredObject<ListBox>("suggestions_list");

        suggestionsScroll.CanFocus = false;
        suggestionsScroll.FocusOnClick = false;
        _suggestionsList.CanFocus = false;
        _suggestionsList.FocusOnClick = false;

        _suggestionsList.OnRowActivated += OnSuggestionRowActivated;

        _searchKeyController = EventControllerKey.New();
        _searchKeyController.SetPropagationPhase(PropagationPhase.Capture);
        _searchKeyController.OnKeyPressed += OnSearchKeyPressed;
        _searchEntry.AddController(_searchKeyController);
    }

    public void OnOpened()
    {
        if (_disposed)
            return;

        Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
            {
                _searchEntry.GrabFocus();
                _searchEntry.SelectRegion(0, -1);
            }
            return false;
        });
    }

    public void OnClosed()
    {
        if (_disposed)
            return;

        DismissSuggestions();
    }

    private void OnSearchEntryActivated(object? sender = null, EventArgs? args = null)
    {
        Submit(_searchEntry.GetText());
    }

    private void Submit(string query)
    {
        DismissSuggestions();
        _popdownAction();

        var trimmed = query.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            Logger.Information("Search submitted from popover: {Query}", trimmed);
            _submitCallback(trimmed);
        }
    }

    private bool OnSearchKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        if (_disposed)
            return false;

        var keyval = Gdk.Functions.KeyvalToLower(args.Keyval);
        var keyName = Gdk.Functions.KeyvalName(keyval);

        switch (keyName)
        {
            case "Down":
                if (_currentSuggestions.Length == 0)
                    return false;

                if (!_suggestionsRevealer.RevealChild)
                {
                    _suggestionsRevealer.RevealChild = true;
                }

                _selectedSuggestionIndex = Math.Min(_selectedSuggestionIndex + 1, _currentSuggestions.Length - 1);
                if (_selectedSuggestionIndex >= 0)
                {
                    HighlightSuggestion(_selectedSuggestionIndex);
                }
                return true;

            case "Up":
                if (_currentSuggestions.Length == 0)
                    return false;

                if (_selectedSuggestionIndex > 0)
                {
                    _selectedSuggestionIndex--;
                    HighlightSuggestion(_selectedSuggestionIndex);
                    return true;
                }

                if (_selectedSuggestionIndex == 0)
                {
                    _selectedSuggestionIndex = -1;
                    _suggestionsList.UnselectAll();
                    _suppressSearchChanged = true;
                    _searchEntry.SetText(_originalTypedQuery);
                    _searchEntry.SetPosition(-1);
                    _suppressSearchChanged = false;
                    return true;
                }
                return false;

            case "Escape":
                if (_suggestionsRevealer.RevealChild)
                {
                    DismissSuggestions();
                    return true;
                }

                _popdownAction();
                return true;

            case "Return":
            case "KP_Enter":
                if (_selectedSuggestionIndex >= 0 && _selectedSuggestionIndex < _currentSuggestions.Length)
                {
                    var selectedQuery = _currentSuggestions[_selectedSuggestionIndex];
                    _suppressSearchChanged = true;
                    _searchEntry.SetText(selectedQuery);
                    _searchEntry.SetPosition(-1);
                    _suppressSearchChanged = false;
                    Submit(selectedQuery);
                    return true;
                }

                Submit(_searchEntry.GetText());
                return true;

            case "Tab":
                if (_selectedSuggestionIndex >= 0 && _selectedSuggestionIndex < _currentSuggestions.Length)
                {
                    var selectedQuery = _currentSuggestions[_selectedSuggestionIndex];
                    _suppressSearchChanged = true;
                    _searchEntry.SetText(selectedQuery);
                    _searchEntry.SetPosition(-1);
                    _originalTypedQuery = selectedQuery;
                    _suppressSearchChanged = false;
                    _searchEntry.GrabFocus();
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    private void HighlightSuggestion(int index)
    {
        if (index < 0 || index >= _currentSuggestions.Length)
            return;

        var row = _suggestionsList.GetRowAtIndex(index);
        if (row is not null)
        {
            _suggestionsList.SelectRow(row);
        }

        _suppressSearchChanged = true;
        _searchEntry.SetText(_currentSuggestions[index]);
        _searchEntry.SetPosition(-1);
        _suppressSearchChanged = false;
        _searchEntry.GrabFocus();
    }

    private void OnSearchTextChanged(object? sender = null, EventArgs? args = null)
    {
        if (_disposed || _suppressSearchChanged)
            return;

        _suggestionDebounceCts?.Cancel();
        _suggestionDebounceCts?.Dispose();
        _suggestionDebounceCts = new CancellationTokenSource();

        var text = _searchEntry.GetText();
        _originalTypedQuery = text;
        _selectedSuggestionIndex = -1;

        if (string.IsNullOrWhiteSpace(text) ||
            text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            YouTubeUrlParser.Parse(text).Kind != YouTubeUrlKind.NotYouTube)
        {
            DismissSuggestions();
            return;
        }

        var token = _suggestionDebounceCts.Token;
        Task.Delay(200, token).ContinueWith(async task =>
        {
            if (task.IsCanceled || token.IsCancellationRequested || _disposed)
                return;

            var suggestions = await _viewModel.FetchSuggestionsAsync(text, token).ConfigureAwait(false);
            Functions.IdleAdd(0, () =>
            {
                if (!token.IsCancellationRequested && !_disposed)
                {
                    UpdateSuggestions(text, suggestions);
                }
                return false;
            });
        }, TaskScheduler.Default);
    }

    private void UpdateSuggestions(string query, IReadOnlyList<string> suggestions)
    {
        if (_disposed)
            return;

        if (suggestions.Count == 0)
        {
            DismissSuggestions();
            return;
        }

        _currentSuggestions = suggestions.Take(8).ToArray();
        _selectedSuggestionIndex = -1;

        while (_suggestionsList.GetFirstChild() is { } child)
        {
            _suggestionsList.Remove(child);
        }

        foreach (var suggestion in _currentSuggestions)
        {
            var row = ListBoxRow.New();
            row.Selectable = true;
            row.Activatable = true;
            row.CanFocus = false;
            row.FocusOnClick = false;
            row.AddCssClass("search-suggestion-row");

            var rowBox = Box.New(Orientation.Horizontal, 10);
            rowBox.MarginTop = 4;
            rowBox.MarginBottom = 4;
            rowBox.MarginStart = 10;
            rowBox.MarginEnd = 10;

            var icon = Image.NewFromIconName("system-search-symbolic");
            icon.PixelSize = 16;
            icon.Valign = Align.Center;
            icon.AddCssClass("dim-label");
            icon.AddCssClass("suggestion-icon");
            rowBox.Append(icon);

            var label = Label.New(null);
            label.Hexpand = true;
            label.Xalign = 0;
            label.Valign = Align.Center;
            label.Ellipsize = Pango.EllipsizeMode.End;
            label.UseMarkup = true;
            label.SetMarkup(FormatSuggestionMarkup(query, suggestion));
            label.AddCssClass("suggestion-label");
            rowBox.Append(label);

            row.SetChild(rowBox);
            _suggestionsList.Append(row);
        }

        _suggestionsRevealer.RevealChild = true;
    }

    private void OnSuggestionRowActivated(ListBox sender, ListBox.RowActivatedSignalArgs args)
    {
        var index = args.Row.GetIndex();
        if (index >= 0 && index < _currentSuggestions.Length)
        {
            var suggestion = _currentSuggestions[index];
            _suppressSearchChanged = true;
            _searchEntry.SetText(suggestion);
            _searchEntry.SetPosition(-1);
            _suppressSearchChanged = false;
            Submit(suggestion);
        }
    }

    private void DismissSuggestions()
    {
        _suggestionDebounceCts?.Cancel();
        _suggestionDebounceCts?.Dispose();
        _suggestionDebounceCts = null;
        _selectedSuggestionIndex = -1;
        _currentSuggestions = [];
        _suggestionsRevealer.RevealChild = false;
    }

    internal static string FormatSuggestionMarkup(string rawQuery, string suggestion)
    {
        var trimmedQuery = rawQuery.Trim();
        var escapedSuggestion = SecurityElement.Escape(suggestion);

        if (string.IsNullOrWhiteSpace(trimmedQuery))
            return escapedSuggestion;

        if (suggestion.StartsWith(trimmedQuery, StringComparison.OrdinalIgnoreCase))
        {
            var typedPart = SecurityElement.Escape(suggestion[..trimmedQuery.Length]);
            var completionPart = SecurityElement.Escape(suggestion[trimmedQuery.Length..]);
            return $"{typedPart}<b>{completionPart}</b>";
        }

        return escapedSuggestion;
    }

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _suggestionDebounceCts?.Cancel();
        _suggestionDebounceCts?.Dispose();
        _suggestionDebounceCts = null;

        _searchEntry.RemoveController(_searchKeyController);
        _searchKeyController.Dispose();

        _suggestionsList.OnRowActivated -= OnSuggestionRowActivated;

        base.Dispose();
    }
}
