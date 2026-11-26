using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using YoutubeAPI;

namespace SilverScreen.Account.Profile;

public sealed class AccountViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<AccountViewModel>();
    private readonly IAccountProfileService _accountProfileService;
    private readonly Action<Action>? _mainThreadDispatch;
    private readonly ISessionService _sessionService;
    private bool _disposed;
    private AccountProfile? _profile;
    private CancellationTokenSource? _profileCancellation;
    private AccountSession _session;

    public AccountViewModel(
        IAccountProfileService accountProfileService,
        ISessionService sessionService,
        Action<Action>? mainThreadDispatch = null)
    {
        _accountProfileService = accountProfileService;
        _sessionService = sessionService;
        _mainThreadDispatch = mainThreadDispatch;
        _session = _sessionService.GetCurrentSession();
        _sessionService.SessionChanged += OnSessionChanged;
        RefreshProfile();
    }

    private AccountSession Session
    {
        get => _session;
        set
        {
            _session = value;
            _profile = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasManualSession));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(AvatarUrl));
            OnPropertyChanged(nameof(PresentationStatus));
            StateChanged?.Invoke(this, EventArgs.Empty);
            RefreshProfile();
        }
    }

    public AccountProfilePresentationStatus ProfileStatus { get; private set; } =
        AccountProfilePresentationStatus.SignedOut;

    public AccountProfilePresentationStatus PresentationStatus =>
        !HasManualSession ? AccountProfilePresentationStatus.SignedOut : ProfileStatus;

    public bool IsProfileChecking => ProfileStatus == AccountProfilePresentationStatus.Checking;

    public bool IsProfileRejected => ProfileStatus == AccountProfilePresentationStatus.Rejected;

    public bool IsProfileUnavailable => ProfileStatus == AccountProfilePresentationStatus.Unavailable;

    public bool HasManualSession => SessionGate.RequireSignedIn(_sessionService);

    public string? ManualSessionError { get; private set; }

    public string DisplayName => _profile?.DisplayName ?? (string.IsNullOrWhiteSpace(Session.DisplayName)
        ? "YouTube session"
        : Session.DisplayName);

    public string? AvatarUrl => _profile?.AvatarUrl ?? Session.AvatarUrl;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _profileCancellation?.Cancel();
        _profileCancellation?.Dispose();
        _sessionService.CancelValidation();
        _sessionService.SessionChanged -= OnSessionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? StateChanged;


    public async Task<bool> SaveManualSessionAsync(string cookieContent)
    {
        Logger.Debug("SaveManualSessionAsync called");
        if (string.IsNullOrWhiteSpace(cookieContent))
        {
            SetManualSessionError("Nothing to save — paste the contents of your cookies.txt file first.");
            return false;
        }

        var trimmed = cookieContent.Trim();
        try
        {
            var auth = YouTubeCookieAuthentication.FromNetscape(trimmed);
            if (!auth.HasAuthenticationCookies)
            {
                SetManualSessionError(
                    "That doesn't look like a cookies.txt file with valid YouTube login credentials — export Netscape-format cookies while logged into YouTube and paste the whole file contents.");
                return false;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            Logger.Warning(exception, "Manual session content was rejected");
            SetManualSessionError(
                "That doesn't look like a cookies.txt file with valid YouTube login credentials — export Netscape-format cookies while logged into YouTube and paste the whole file contents.");
            return false;
        }

        var (succeeded, error) = await PersistSessionAsync(trimmed);
        SetManualSessionError(error);
        return succeeded;
    }


    private async Task<(bool Succeeded, string? Error)> PersistSessionAsync(string cookieContent)
    {
        try
        {
            await _sessionService.SetManualSessionAsync(cookieContent, SessionCookieFormat.NetscapeCookiesText)
                .ConfigureAwait(false);
            return (true, null);
        }
        catch (SessionPersistenceException exception)
        {
            Logger.Warning(exception, "Failed to persist YouTube session");
            return (false, "Could not save the session — the system keyring (Secret Service) is unavailable.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            Logger.Warning(exception, "Manual session content was rejected");
            return (false,
                "That doesn't look like a cookies.txt file with valid YouTube login credentials — export Netscape-format cookies while logged into YouTube and paste the whole file contents.");
        }
    }

    private void SetManualSessionError(string? error)
    {
        if (string.Equals(ManualSessionError, error, StringComparison.Ordinal))
            return;

        ManualSessionError = error;
        OnPropertyChanged(nameof(ManualSessionError));
    }


    public async Task ClearSessionAsync()
    {
        Logger.Debug("Clearing active YouTube session");
        try
        {
            await _sessionService.ClearSessionAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to clear YouTube session");
        }
    }

    private void OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed)
            return;

        DispatchToMainThread(() =>
        {
            if (!_disposed)
                Session = _sessionService.GetCurrentSession();
        });
    }

    public void RefreshProfile()
    {
        _profileCancellation?.Cancel();
        _profileCancellation?.Dispose();
        _profileCancellation = null;
        _profile = null;

        if (_disposed || !SessionGate.RequireSignedIn(_sessionService))
        {
            SetProfileStatus(AccountProfilePresentationStatus.SignedOut);
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(AvatarUrl));
            return;
        }

        var cachedProfile = _accountProfileService.TryGetCachedProfile();
        if (cachedProfile is not null)
        {
            _profile = cachedProfile;
            SetProfileStatus(AccountProfilePresentationStatus.Ready);
        }
        else
        {
            SetProfileStatus(AccountProfilePresentationStatus.Checking);
        }

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(AvatarUrl));
        var cancellation = new CancellationTokenSource();
        _profileCancellation = cancellation;
        LoadProfileAsync(cancellation, cancellation.Token).FireAndForget(Logger);
    }

    private async Task LoadProfileAsync(CancellationTokenSource request, CancellationToken cancellationToken)
    {
        AccountProfileResult result;
        try
        {
            result = await _accountProfileService.GetCurrentProfileAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(
                "Failed to load current YouTube account profile. Failure details: {FailureDetails}",
                DiagnosticSanitizer.Sanitize(exception.ToString()));
            result = new AccountProfileResult.TemporaryFailure();
        }

        if (_disposed || request.IsCancellationRequested || !ReferenceEquals(_profileCancellation, request))
            return;

        DispatchToMainThread(() =>
        {
            if (_disposed || request.IsCancellationRequested || !ReferenceEquals(_profileCancellation, request))
                return;

            if (result is AccountProfileResult.TemporaryFailure &&
                _profile is not null &&
                ProfileStatus == AccountProfilePresentationStatus.Ready)
                return;

            _profile = result is AccountProfileResult.Success(var profile) ? profile : null;
            var status = result switch
            {
                AccountProfileResult.Success => AccountProfilePresentationStatus.Ready,
                AccountProfileResult.AuthenticationRequired or AccountProfileResult.AuthenticationRejected =>
                    AccountProfilePresentationStatus.Rejected,
                AccountProfileResult.TemporaryFailure => AccountProfilePresentationStatus.Unavailable,
                _ => throw new UnreachableException()
            };
            SetProfileStatus(status);
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(AvatarUrl));
        });
    }

    private void DispatchToMainThread(Action action)
    {
        if (_mainThreadDispatch is not null)
            _mainThreadDispatch(action);
        else
            action();
    }

    private void SetProfileStatus(AccountProfilePresentationStatus status)
    {
        if (ProfileStatus == status)
            return;
        ProfileStatus = status;
        OnPropertyChanged(nameof(ProfileStatus));
        OnPropertyChanged(nameof(PresentationStatus));
        OnPropertyChanged(nameof(IsProfileChecking));
        OnPropertyChanged(nameof(IsProfileRejected));
        OnPropertyChanged(nameof(IsProfileUnavailable));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}