using System.Diagnostics.CodeAnalysis;
using Adw;
using GObject;
using Gtk;
using SilverScreen.Features.Session;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.ViewModels;
using WebKit;
using Window = Adw.Window;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Account;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal sealed class WebLoginWindow : WindowBase<Window>
{
    private const string LoginUri =
        "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";

    private const string YouTubeUri = "https://www.youtube.com/";

    private const string BrowserUserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";

    private readonly AccountViewModel _account;
    private readonly WebLoginCaptureCoordinator _capture;
    private readonly Action _closed;
    private readonly CookieManager _cookieManager;
    private readonly NetworkSession _networkSession;
    private readonly Label _statusLabel;
    private readonly Box _webViewContainer;
    private readonly WebView _webView;
    private bool _closedInvoked;
    private bool _disposed;
    private bool _nativeDisposed;

    internal WebLoginWindow(Gtk.Window parent, AccountViewModel account, Action closed)
    {
        _account = account;
        _closed = closed;
        _statusLabel = GetRequiredObject<Label>("web_login_status_label");
        _webViewContainer = GetRequiredObject<Box>("web_view_container");
        Widget.TransientFor = parent;

        _networkSession = NetworkSession.NewEphemeral();
        _cookieManager = _networkSession.GetCookieManager();
        _webView = CreateWebView(_networkSession);
        _webView.Hexpand = true;
        _webView.Vexpand = true;
        _webView.GetSettings().SetUserAgent(BrowserUserAgent);

        _webViewContainer.Append(_webView);

        _capture = new WebLoginCaptureCoordinator(
            ReadReadyCookiesAsync,
            cookieText => !_disposed && _account.SaveWebSession(cookieText),
            OnPersisted,
            OnReadFailed,
            OnPersistenceFailed);

        _cookieManager.OnChanged += OnCookieChanged;
        _webView.OnLoadChanged += OnLoadChanged;
        Widget.OnCloseRequest += OnCloseRequest;
        _webView.LoadUri(LoginUri);
    }

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cookieManager.OnChanged -= OnCookieChanged;
        _webView.OnLoadChanged -= OnLoadChanged;
        Widget.OnCloseRequest -= OnCloseRequest;
        Widget.Hide();

        if (!_closedInvoked)
        {
            _closedInvoked = true;
            _closed();
        }

        var stopped = _capture.StopAsync();
        if (stopped.IsCompleted)
        {
            TearDownNativeObjects();
            return;
        }

        _ = FinishDisposalAsync(stopped);
    }

    internal void Present()
    {
        if (!_disposed)
            Widget.Present();
    }

    private static WebView CreateWebView(NetworkSession session)
    {
        using var sessionValue = new Value(session);
        return WebView.NewWithProperties(
            [new ConstructArgument("network-session", sessionValue)]);
    }

    private async Task<string?> ReadReadyCookiesAsync()
    {
        var snapshots = await WebLoginCookieReader.GetCookiesAsync(_cookieManager, YouTubeUri);
        if (_disposed)
            return null;

        var cookieText = WebLoginCookieReader.SerializeNetscape(snapshots);
        if (YouTubeCredentials.ParseNetscape(cookieText) is null)
            return null;

        _statusLabel.SetText("Finishing sign-in…");
        return cookieText;
    }

    private void OnCookieChanged(CookieManager sender, EventArgs args)
    {
        if (!_disposed)
            _capture.RequestCapture();
    }

    private void OnLoadChanged(WebView sender, WebView.LoadChangedSignalArgs args)
    {
        if (!_disposed && args.LoadEvent == LoadEvent.Finished)
            _capture.RequestCapture();
    }

    private bool OnCloseRequest(Gtk.Window sender, EventArgs args)
    {
        Dispose();
        return true;
    }

    private void OnPersisted()
    {
        if (_disposed)
            return;

        _ = _account.ValidateAsync();
        Dispose();
    }

    private void OnReadFailed(Exception exception)
    {
        if (!_disposed)
            _statusLabel.SetText(
                "Could not read the YouTube session. Continue signing in or close this window to cancel.");
    }

    private void OnPersistenceFailed()
    {
        if (!_disposed)
            _statusLabel.SetText("Could not save the YouTube session because the system keyring is unavailable.");
    }

    private async Task FinishDisposalAsync(Task stopped)
    {
        try
        {
            await stopped;
        }
        finally
        {
            TearDownNativeObjects();
        }
    }

    private void TearDownNativeObjects()
    {
        if (_nativeDisposed)
            return;

        _nativeDisposed = true;
        _webView.Unparent();
        _webView.Dispose();
        _cookieManager.Dispose();
        _networkSession.Dispose();
        Widget.Dispose();
    }
}