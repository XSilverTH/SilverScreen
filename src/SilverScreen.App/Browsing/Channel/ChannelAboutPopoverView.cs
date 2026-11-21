using Gtk;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Browsing.Channel;

public partial class ChannelAboutPopoverView : ViewBase<Popover, ChannelViewModel>
{
    protected override void BindViewModel(ChannelViewModel viewModel, BindingScope<ChannelViewModel> bindings)
    {
        bindings.Bind(
            nameof(ChannelViewModel.State),
            this,
            static vm => vm.State,
            static (view, state) => view.Render(state));
    }

    private void Render(ChannelViewState state)
    {
        about_channel_title.SetText(state.Name);

        var handle = ChannelFormatter.ExtractHandle(state.Url);
        about_handle_label.Visible = !string.IsNullOrWhiteSpace(handle);
        if (!string.IsNullOrWhiteSpace(handle))
            about_handle_label.SetText($"Handle: {handle}");

        about_subscribers_label.Visible = state.SubscriberCount is not null;
        if (state.SubscriberCount is { } subs)
            about_subscribers_label.SetText($"Subscribers: {ChannelFormatter.FormatSubscribers(subs)}");

        var hasUrl = !string.IsNullOrWhiteSpace(state.Url);
        about_channel_link.Visible = hasUrl;
        about_links_section.Visible = hasUrl;
        if (hasUrl)
        {
            about_channel_link.SetUri(state.Url!);
            about_channel_link.SetLabel(state.Url!);
        }

        var contactInfo = ChannelFormatter.ExtractContactInfo(state.Description);
        about_contact_section.Visible = !string.IsNullOrWhiteSpace(contactInfo);
        about_contact_label.SetText(contactInfo ?? string.Empty);

        var hasDescription = !string.IsNullOrWhiteSpace(state.Description);
        about_description_section.Visible = hasDescription;
        about_description_label.SetText(state.Description ?? string.Empty);
    }
}
