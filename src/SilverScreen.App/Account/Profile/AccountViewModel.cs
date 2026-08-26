using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Account.Session;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Account.Profile;

public sealed class AccountViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<AccountViewModel>();
    private readonly IAccountProfileService _accountProfileService;
    private readonly ISessionService _sessionService;
    private bool _disposed;
    private AccountProfile? _profile;
    private CancellationTokenSource? _profileCancellation;
    private AccountSession _session;

    public AccountViewModel(IAccountProfileService accountProfileService, ISessionService sessionService)
    {
        _accountProfileService = accountProfileService;
        _sessionService = sessionService;
        _session = _sessionService.GetCurrentSession();
        if (_session.HasManualSession)
            _profile = _accountProfileService.GetCachedProfile();

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
            StateChanged?.Invoke(this, EventArgs.Empty);
            RefreshProfile();
        }
    }

    public bool HasManualSession => Session.HasManualSession;

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

    public bool SaveManualSession(string cookieContent)
    {
        Logger.Information("SaveManualSession called");
        if (!string.IsNullOrWhiteSpace(cookieContent))
            return PersistSession(cookieContent.Trim());
        Logger.Warning("Manual session save aborted: empty cookie content");
        return false;
    }

    public bool SaveWebSession(string cookieContent)
    {
        return !string.IsNullOrWhiteSpace(cookieContent) && PersistSession(cookieContent.Trim());
    }

    private bool PersistSession(string cookieContent)
    {
        try
        {
            _sessionService.SetManualSession(cookieContent, SessionCookieFormat.NetscapeCookiesText);
            return true;
        }
        catch (SessionPersistenceException exception)
        {
            Logger.Warning(exception, "Failed to persist YouTube session");
            return false;
        }
    }

    public void ClearSession()
    {
        Logger.Information("Clearing active YouTube session");
        try
        {
            _sessionService.ClearSession();
        }
        catch (SessionPersistenceException exception)
        {
            Logger.Error(exception, "Failed to clear YouTube session");
        }
    }

    public async Task ValidateAsync()
    {
        if (_disposed)
            return;

        try
        {
            await _sessionService.ValidateSessionAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to validate YouTube session");
        }
    }

    private void OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        if (!_disposed)
            Session = _sessionService.GetCurrentSession();
    }

    private void RefreshProfile()
    {
        _profileCancellation?.Cancel();
        _profileCancellation?.Dispose();
        _profileCancellation = null;

        if (_disposed || !Session.HasManualSession)
            return;

        _profileCancellation = new CancellationTokenSource();
        LoadProfileAsync(_profileCancellation.Token).FireAndForget(Logger);
    }

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        AccountProfile? profile;
        try
        {
            profile = await _accountProfileService.GetCurrentProfileAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load current YouTube account profile");
            return;
        }

        if (_disposed || cancellationToken.IsCancellationRequested || profile is null)
            return;

        _profile = profile;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(AvatarUrl));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}