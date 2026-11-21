using System.Globalization;
using Gdk;
using Gtk;
using SilverScreen.Core.Domain;
using SilverScreen.Shell;
using XSTH.Blueprint.Helpers;
using Constants = Gdk.Constants;

namespace SilverScreen.Browsing.Components;

public partial class VideoCardMetadataView : ViewBase<Overlay>
{
    private readonly INavigationService _navigation;
    private VideoSummary? _video;

    public MenuButton MenuButton => menu;
    public event Action? ContextMenuRequested;

    public VideoCardMetadataView(INavigationService navigation)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));

        var channelClick = GestureClick.New();
        channelClick.Button = 1;
        Lifetime.Attach(channel, channelClick,
            c => c.OnReleased += OnChannelReleased,
            c => c.OnReleased -= OnChannelReleased);

        var channelKeyController = EventControllerKey.New();
        Lifetime.Attach(channel, channelKeyController,
            c => c.OnKeyPressed += OnChannelKeyPressed,
            c => c.OnKeyPressed -= OnChannelKeyPressed);
    }

    public void Bind(VideoSummary video)
    {
        _video = video;
        title.SetText(video.Title);
        title.TooltipText = video.Title;
        channel.SetText(video.ChannelName);

        if (video.PublishedAt is { } publishedAt)
        {
            upload_date.SetText(VideoCardFormatting.FormatUploadAge(publishedAt, DateTimeOffset.Now));
            upload_date.TooltipText = $"Published: {publishedAt.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";
            upload_date.Visible = true;
        }
        else if (video.ApproximateUploadDate is { } uploadDate)
        {
            upload_date.SetText(VideoCardFormatting.FormatUploadAge(uploadDate, DateOnly.FromDateTime(DateTime.Now)));
            upload_date.TooltipText = $"Published: {uploadDate.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";
            upload_date.Visible = true;
        }
        else
        {
            upload_date.SetText(string.Empty);
            upload_date.TooltipText = string.Empty;
            upload_date.Visible = false;
        }
    }

    public void Unbind()
    {
        _video = null;
        title.SetText(string.Empty);
        title.TooltipText = string.Empty;
        channel.SetText(string.Empty);
        upload_date.SetText(string.Empty);
        upload_date.TooltipText = string.Empty;
        upload_date.Visible = false;
    }

    private void OnChannelReleased(GestureClick sender, GestureClick.ReleasedSignalArgs args)
    {
        if (_video is null) return;
        sender.SetState(EventSequenceState.Claimed);
        NavigateToChannel();
    }

    private bool OnChannelKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        if (IsDisposed || _video is null) return false;
        switch (args.Keyval)
        {
            case Constants.KEY_Return or Constants.KEY_KP_Enter or Constants.KEY_space:
                NavigateToChannel();
                return true;
            case Constants.KEY_Menu:
            case Constants.KEY_F10 when (args.State & ModifierType.ShiftMask) != 0:
                ContextMenuRequested?.Invoke();
                return true;
            default:
                return false;
        }
    }

    public void NavigateToChannel()
    {
        if (_video is { ChannelUrl: { } url, ChannelName: { } name } &&
            !string.IsNullOrWhiteSpace(url) &&
            !url.Contains("UC0000000000000000000000", StringComparison.OrdinalIgnoreCase))
        {
            _navigation.NavigateToChannel(url, name);
        }
    }
}
