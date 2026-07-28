using System.ComponentModel;
using System.Runtime.CompilerServices;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.ViewModels;

public sealed class AccountViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAccountProfileService _accountProfileService;
    private readonly ISessionService _sessionService;
    private readonly ShellViewModel _shell;
    private readonly SessionValidationCoordinator _validation;
    private bool _disposed;
    private AccountProfile? _profile;
    private CancellationTokenSource? _profileCancellation;
    private AccountSession _session;

    public AccountViewModel(IAccountProfileService accountProfileService, ISessionService sessionService,
        SessionValidationCoordinator validation, ShellViewModel shell)
    {
        _accountProfileService = accountProfileService;
        _sessionService = sessionService;
        _validation = validation;
        _shell = shell;
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


    public bool IsValidating
    {
        get;
        private set
        {
            if (field == value)
                return;

            field = value;
            OnPropertyChanged();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _profileCancellation?.Cancel();
        _profileCancellation?.Dispose();
        _validation.Cancel();
        _sessionService.SessionChanged -= OnSessionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? StateChanged;

    public bool SaveManualSession(string cookieContent)
    {
        if (!string.IsNullOrWhiteSpace(cookieContent))
            return PersistSession(
                cookieContent.Trim(),
                "Manual YouTube session saved securely.");
        _shell.Status = "Manual YouTube session was not saved because no cookie content was entered.";
        return false;
    }

    public bool SaveWebSession(string cookieContent)
    {
        if (!string.IsNullOrWhiteSpace(cookieContent))
            return PersistSession(
                cookieContent.Trim(),
                "YouTube web session saved securely.");
        _shell.Status = "YouTube web session was not saved because no cookie content was captured.";
        return false;
    }

    private bool PersistSession(string cookieContent, string successMessage)
    {
        try
        {
            _sessionService.SetManualSession(cookieContent, SessionCookieFormat.NetscapeCookiesText);
        }
        catch (SessionPersistenceException exception)
        {
            _shell.Status = exception.Message;
            return false;
        }

        _shell.Status = successMessage;
        return true;
    }

    public void ClearSession()
    {
        try
        {
            _sessionService.ClearSession();
        }
        catch (SessionPersistenceException exception)
        {
            _shell.Status = exception.Message;
            return;
        }

        _shell.Status = "YouTube session cleared.";
    }

    public async Task ValidateAsync()
    {
        if (!_validation.IsAvailable || _disposed)
            return;

        IsValidating = true;
        _shell.Status = SessionValidationFormatter.ValidatingMessage;
        try
        {
            _shell.Status = await _validation.ValidateAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            _shell.Status = SessionValidationFormatter.FormatUnexpectedError();
        }
        finally
        {
            if (!_disposed)
                IsValidating = false;
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
        _ = LoadProfileAsync(_profileCancellation.Token);
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
        catch (Exception)
        {
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