using System.Text.RegularExpressions;
using GObject;
using Serilog;
using SilverScreen.Account.Profile;
using SilverScreen.Core.Common;
using WebKit;
using XSTH.Blueprint.Helpers;
using YoutubeAPI;
using Functions = GLib.Functions;
using Window = Adw.Window;

namespace SilverScreen.Account.Auth;

public sealed partial class WebLoginWindow : WindowBase<Window>
{
    private const string LoginUri =
        "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";

    private const string YouTubeUri = "https://www.youtube.com/";

    private const string BrowserUserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";

    private static readonly ILogger Logger = Log.ForContext<WebLoginWindow>();

    private static readonly HashSet<string> CompanionCookieNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "LOGIN_INFO",
            "SID",
            "SSID",
            "HSID",
            "__Secure-1PSID",
            "__Secure-3PSID"
        };

    private readonly AccountViewModel _account;
    private readonly WebLoginCaptureCoordinator _capture;
    private readonly Action _closed;
    private readonly CookieManager _cookieManager;
    private readonly NetworkSession _networkSession;
    private readonly ManualResetEventSlim _teardownEvent = new(false);
    private readonly int _uiThreadId;
    private readonly WebView _webView;
    private bool _closedInvoked;
    private int _disposeState;
    private bool _loadFinished;
    private bool _nativeDisposed;
    private int _terminalState;

    internal WebLoginWindow(Gtk.Window parent, AccountViewModel account, Action closed)
    {
        Logger.Information("Opening WebLoginWindow for YouTube authentication");
        _uiThreadId = Environment.CurrentManagedThreadId;
        _account = account;
        _closed = closed;
        Widget.TransientFor = parent;

        // Ephemeral WebKit session per login attempt; discarded in TearDownNativeObjects.
        _networkSession = NetworkSession.NewEphemeral();
        _cookieManager = _networkSession.GetCookieManager();
        _webView = CreateWebView(_networkSession);
        _webView.Hexpand = true;
        _webView.Vexpand = true;
        _webView.GetSettings().SetUserAgent(BrowserUserAgent);

        web_view_container.SetChild(_webView);

        _capture = new WebLoginCaptureCoordinator(
            ReadReadyCookiesAsync,
            TryPersistSession,
            OnPersisted,
            OnReadFailed,
            OnPersistenceFailed);

        _cookieManager.OnChanged += OnCookieChanged;
        _webView.OnLoadChanged += OnLoadChanged;
        _webView.OnDecidePolicy += OnDecidePolicy;
        Widget.OnCloseRequest += OnCloseRequest;
        _webView.LoadUri(LoginUri);
    }

    public new void Dispose()
    {
        // Double-dispose guard: exactly one thread wins disposal initiation.
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            if (Environment.CurrentManagedThreadId == _uiThreadId)
                DisposeOnMainThread();
            else
                Functions.IdleAdd(0, () =>
                {
                    try
                    {
                        DisposeOnMainThread();
                    }
                    catch (Exception exception)
                    {
                        Logger.Warning(exception, "WebLoginWindow UI-thread disposal failed");
                        _teardownEvent.Set();
                    }

                    return false;
                });
        }

        // WebKit/GTK natives must be torn down on the UI thread, never on a
        // threadpool thread. Marshal synchronously so the caller never
        // outlives the teardown ordering (capture stopped and native handles disposed).
        if (Environment.CurrentManagedThreadId == _uiThreadId) return;
        if (!_teardownEvent.Wait(TimeSpan.FromSeconds(10)))
            Logger.Warning("WebLoginWindow UI-thread disposal timed out; teardown remains queued on the main loop");
    }

    private void DisposeOnMainThread()
    {
        _cookieManager.OnChanged -= OnCookieChanged;
        _webView.OnLoadChanged -= OnLoadChanged;
        _webView.OnDecidePolicy -= OnDecidePolicy;
        Widget.OnCloseRequest -= OnCloseRequest;

        try
        {
            _webView.StopLoading();
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "WebLoginWindow WebView stop loading failed");
        }

        try
        {
            Widget.Hide();
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "WebLoginWindow hide during disposal failed");
        }

        if (!_closedInvoked)
        {
            _closedInvoked = true;
            _closed();
        }

        Task stopped;
        try
        {
            stopped = _capture.StopAsync();
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "WebLoginWindow capture stop failed during disposal");
            TearDownNativeObjects();
            return;
        }

        if (stopped.IsCompleted)
        {
            if (stopped is { IsFaulted: true, Exception: not null })
                Logger.Warning(stopped.Exception, "WebLoginWindow capture drain faulted during disposal");
            TearDownNativeObjects();
            return;
        }

        // In-flight cookie read holds native handles: tear down on the UI
        // thread only after the drain finishes, never by blocking it.
        _ = stopped.ContinueWith(static (task, state) =>
        {
            var self = (WebLoginWindow)state!;
            if (task is { IsFaulted: true, Exception: not null })
                Logger.Warning(task.Exception, "WebLoginWindow capture drain faulted during disposal");
            try
            {
                Functions.IdleAdd(0, () =>
                {
                    self.TearDownNativeObjects();
                    return false;
                });
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Failed to schedule native teardown on UI thread");
                self._teardownEvent.Set();
            }
        }, this, CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default);
    }

    internal void Present()
    {
        if (Volatile.Read(ref _disposeState) == 0)
            Widget.Present();
    }

    private static WebView CreateWebView(NetworkSession session)
    {
        using var sessionValue = new Value(session);
        return WebView.NewWithProperties(
            [new ConstructArgument("network-session", sessionValue)]);
    }

    private static bool HasCompanionCookies(IEnumerable<WebCookieSnapshot> snapshots)
    {
        return snapshots.Any(s => CompanionCookieNames.Contains(s.Name));
    }

    private static bool HasAuthenticationCookies(string cookieText)
    {
        try
        {
            return YouTubeCookieAuthentication.FromNetscape(cookieText).HasAuthenticationCookies;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<string?> ReadReadyCookiesAsync()
    {
        var snapshots = await WebLoginCookieReader.GetCookiesAsync(_cookieManager, YouTubeUri).ConfigureAwait(false);
        if (Volatile.Read(ref _disposeState) != 0)
            return null;
        var cookieText = WebLoginCookieReader.SerializeNetscape(snapshots);

        if (!HasAuthenticationCookies(cookieText))
            return null;

        if (!HasCompanionCookies(snapshots))
        {
            if (!_loadFinished)
            {
                Logger.Debug("Solitary SAPISID detected before load finished; waiting for companion cookies");
                return null;
            }

            Logger.Debug("Solitary SAPISID detected after load finished; waiting for debounce before finalize");
            await Task.Delay(500).ConfigureAwait(false);
            if (Volatile.Read(ref _disposeState) != 0)
                return null;

            snapshots = await WebLoginCookieReader.GetCookiesAsync(_cookieManager, YouTubeUri).ConfigureAwait(false);
            if (Volatile.Read(ref _disposeState) != 0)
                return null;
            cookieText = WebLoginCookieReader.SerializeNetscape(snapshots);
            // The cookie store can change while the debounce window is open. Revalidate
            // the snapshot that will actually be persisted instead of trusting the first read.
            if (!HasAuthenticationCookies(cookieText))
                return null;
        }

        PostStatus("Finishing sign-in…");
        return cookieText;
    }

    private bool TryPersistSession(string cookieText)
    {
        return Volatile.Read(ref _disposeState) == 0 &&
               // Failed saves return false without touching the stored session, so a
               // refresh never clears the previous session unless a new capture
               // succeeds; failures stay retryable while terminal completion below
               // fires exactly once.
               _account.SaveWebSession(cookieText);
    }

    private void OnCookieChanged(CookieManager sender, EventArgs args)
    {
        if (Volatile.Read(ref _disposeState) == 0)
            _capture.RequestCapture();
    }

    private void OnLoadChanged(WebView sender, WebView.LoadChangedSignalArgs args)
    {
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        switch (args.LoadEvent)
        {
            case LoadEvent.Started:
                _loadFinished = false;
                break;
            case LoadEvent.Finished:
                _loadFinished = true;
                _capture.RequestCapture();
                break;
        }
    }

    private static bool OnDecidePolicy(WebView sender, WebView.DecidePolicySignalArgs args)
    {
        if (args.DecisionType is not (PolicyDecisionType.NavigationAction or PolicyDecisionType.NewWindowAction)
            || args.Decision is not NavigationPolicyDecision navDecision) return false;
        var uriString = navDecision.GetNavigationAction().GetRequest().Uri;
        if (string.IsNullOrEmpty(uriString) || !Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
            return false;
        if (string.IsNullOrEmpty(uri.Host) || IsAllowedHost(uri.Host)) return false;
        Logger.Warning("Blocked navigation to untrusted host: {Host}", uri.Host);
        args.Decision.Ignore();
        return true;
    }

    [GeneratedRegex(@"^(?:[a-z0-9-]+\.)*(?:google|youtube)\.(?:[a-z]{2,3}(?:\.[a-z]{2})?)$", RegexOptions.IgnoreCase)]
    private static partial Regex AllowedHostRegex();

    internal static bool IsAllowedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var trimmed = host.Trim().TrimEnd('.');
        if (trimmed.Equals("google", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".google", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("youtube", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".youtube", StringComparison.OrdinalIgnoreCase))
            return true;

        return AllowedHostRegex().IsMatch(trimmed);
    }

    private bool OnCloseRequest(Gtk.Window sender, EventArgs args)
    {
        Dispose();
        return true;
    }

    private void OnPersisted()
    {
        // Terminal capture completion: exactly once across racing drains.
        if (Interlocked.Exchange(ref _terminalState, 1) != 0)
            return;

        Logger.Information("WebLoginWindow captured and persisted YouTube session");
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        _account.ValidateAsync().FireAndForget(Logger);
        Dispose();
    }

    private void OnReadFailed(Exception exception)
    {
        Logger.Warning(exception, "WebLoginWindow failed to read cookies");
        PostStatus("Could not read the YouTube session. Continue signing in or close this window to cancel.");
    }

    private void OnPersistenceFailed()
    {
        Logger.Warning("WebLoginWindow failed to save session to secret store");
        PostStatus("Could not save the YouTube session because the system keyring is unavailable.");
    }

    private void PostStatus(string message)
    {
        if (Volatile.Read(ref _disposeState) != 0)
            return;

        // Capture callbacks run on the drain (threadpool) thread; widgets are
        // UI-thread only.
        Functions.IdleAdd(0, () =>
        {
            if (Volatile.Read(ref _disposeState) == 0)
                web_login_status_label.SetText(message);
            return false;
        });
    }

    private void TearDownNativeObjects()
    {
        // UI thread only. Disposing WebView/NetworkSession/CookieManager from
        // the threadpool tears down WebKit natives off the main loop.
        if (_nativeDisposed)
            return;

        _nativeDisposed = true;
        try
        {
            try
            {
                _webView.StopLoading();
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "WebLoginWindow WebView stop loading failed");
            }

            try
            {
                _webView.Unparent();
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "WebLoginWindow WebView unparent failed");
            }

            try
            {
                _webView.Dispose();
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "WebLoginWindow WebView dispose failed");
            }

            try
            {
                _cookieManager.Dispose();
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "WebLoginWindow CookieManager dispose failed");
            }

            try
            {
                _networkSession.Dispose();
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "WebLoginWindow NetworkSession dispose failed");
            }

            try
            {
                Widget.Dispose();
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "WebLoginWindow widget dispose failed");
            }

            try
            {
                _capture.Dispose();
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "WebLoginWindow capture coordinator dispose failed");
            }
        }
        finally
        {
            _teardownEvent.Set();
        }
    }
}