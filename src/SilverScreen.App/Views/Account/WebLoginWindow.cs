using System.Diagnostics.CodeAnalysis;
using GObject;
using Serilog;
using SilverScreen.Features.Session;
using SilverScreen.Infrastructure;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.ViewModels;
using WebKit;
using XSTH.Blueprint.Helpers;
using Window = Adw.Window;

namespace SilverScreen.Views.Account;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public sealed partial class WebLoginWindow : WindowBase<Window>
{
    private const string LoginUri =
        "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";

    private const string YouTubeUri = "https://www.youtube.com/";

    private const string BrowserUserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";

    private static readonly ILogger Logger = Log.ForContext<WebLoginWindow>();

    private readonly AccountViewModel _account;
    private readonly WebLoginCaptureCoordinator _capture;
    private readonly Action _closed;
    private readonly CookieManager _cookieManager;
    private readonly NetworkSession _networkSession;
    private readonly WebView _webView;
    private bool _closedInvoked;
    private bool _disposed;
    private bool _nativeDisposed;

    internal WebLoginWindow(Gtk.Window parent, AccountViewModel account, Action closed)
    {
        Logger.Information("Opening WebLoginWindow for YouTube authentication");
        _account = account;
        _closed = closed;
        Widget.TransientFor = parent;

        _networkSession = NetworkSession.NewEphemeral();
        _cookieManager = _networkSession.GetCookieManager();
        _webView = CreateWebView(_networkSession);
        _webView.Hexpand = true;
        _webView.Vexpand = true;
        _webView.GetSettings().SetUserAgent(BrowserUserAgent);

        web_view_container.Append(_webView);

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

        FinishDisposalAsync(stopped).FireAndForget(Logger);
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

        web_login_status_label.SetText("Finishing sign-in…");
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
        Logger.Information("WebLoginWindow captured and persisted YouTube session");
        if (_disposed)
            return;

        _account.ValidateAsync().FireAndForget(Logger);
        Dispose();
    }

    private void OnReadFailed(Exception exception)
    {
        Logger.Warning(exception, "WebLoginWindow failed to read cookies");
        if (!_disposed)
            web_login_status_label.SetText(
                "Could not read the YouTube session. Continue signing in or close this window to cancel.");
    }

    private void OnPersistenceFailed()
    {
        Logger.Warning("WebLoginWindow failed to save session to secret store");
        if (!_disposed)
            web_login_status_label.SetText(
                "Could not save the YouTube session because the system keyring is unavailable.");
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