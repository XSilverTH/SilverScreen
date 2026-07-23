using Gtk;
using SilverScreen.Core.Models;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Player;

internal interface IEmbeddedPlayerPresenter
{
    Task<string> PresentAsync(PlaybackRequest request);
}

public partial class EmbeddedPlayerView : ViewBase<Overlay>, IEmbeddedPlayerPresenter
{
    private readonly Action _backRequested;
    private readonly Label _channelLabel;
    private readonly Label _durationLabel;
    private readonly Action _presentRequested;
    private readonly Label _titleLabel;

    public EmbeddedPlayerView(Action presentRequested, Action backRequested)
    {
        _presentRequested = presentRequested;
        _backRequested = backRequested;
        _titleLabel = GetRequiredObject<Label>("player_title_label");
        _channelLabel = GetRequiredObject<Label>("player_channel_label");
        _durationLabel = GetRequiredObject<Label>("player_duration_label");
    }

    public Task<string> PresentAsync(PlaybackRequest request)
    {
        var video = request.Videos.IsDefaultOrEmpty ? null : request.Videos[0];
        if (video is null)
            return Task.FromResult("No video is available to open in the embedded player.");

        Functions.IdleAdd(0, () =>
        {
            _titleLabel.SetText(video.Title);
            _channelLabel.SetText(video.ChannelName);
            _durationLabel.SetText(video.Duration == TimeSpan.Zero ? "Live" : video.Duration.ToString("m\\:ss"));
            _presentRequested();
            return false;
        });

        return Task.FromResult("Opening embedded player.");
    }

    private void OnBackButtonClicked(object? sender, EventArgs args)
    {
        _backRequested();
    }
}
