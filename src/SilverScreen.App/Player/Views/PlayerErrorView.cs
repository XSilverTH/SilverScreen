using Adw;
using SilverScreen.Core.Domain;
using SilverScreen.Player.Controllers;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Views;

/// <summary>
///     Encapsulates the player error page, error classification, retry trigger, and technical detail viewer.
/// </summary>
public partial class PlayerErrorView : ViewBase<StatusPage>
{
    public bool HasError => Widget.GetVisible();
    public PlayerErrorInfo? CurrentErrorInfo { get; private set; }

    public event EventHandler? RetryRequested;
    public event EventHandler? BrowserRequested;

    public void ShowError(string detail, VideoSummary? failedVideo)
    {
        CurrentErrorInfo = PlayerErrorClassifier.Classify(detail);

        Widget.IconName = CurrentErrorInfo.IconName;
        Widget.Title = CurrentErrorInfo.Title;
        Widget.Description = CurrentErrorInfo.Message;
        player_error_detail_label.SetText(CurrentErrorInfo.TechnicalDetail);
        player_error_detail_box.SetVisible(false);
        player_error_toggle_detail_button.SetLabel("Show technical details");

        var canOpenBrowser = failedVideo is not null && !string.IsNullOrWhiteSpace(failedVideo.Id);
        player_error_browser_button.SetVisible(canOpenBrowser);

        Widget.SetVisible(true);
    }

    public void ClearError()
    {
        Widget.SetVisible(false);
        CurrentErrorInfo = null;
    }

    private void OnRetryClicked(object? sender, EventArgs args)
    {
        ClearError();
        RetryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnBrowserClicked(object? sender, EventArgs args)
    {
        BrowserRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnToggleDetailClicked(object? sender, EventArgs args)
    {
        var isVisible = !player_error_detail_box.GetVisible();
        player_error_detail_box.SetVisible(isVisible);
        player_error_toggle_detail_button.SetLabel(isVisible ? "Hide technical details" : "Show technical details");
    }
}
