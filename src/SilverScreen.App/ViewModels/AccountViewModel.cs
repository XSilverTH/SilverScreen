using System.ComponentModel;
using System.Runtime.CompilerServices;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.ViewModels;

public sealed class AccountViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISessionService _sessionService;
    private readonly ShellViewModel _shell;
    private readonly SessionValidationCoordinator _validation;
    private bool _disposed;
    private AccountSession _session;

    public AccountViewModel(ISessionService sessionService, SessionValidationCoordinator validation,
        ShellViewModel shell)
    {
        _sessionService = sessionService;
        _validation = validation;
        _shell = shell;
        _session = _sessionService.GetCurrentSession();
        _sessionService.SessionChanged += OnSessionChanged;
    }

    private AccountSession Session
    {
        get => _session;
        set
        {
            _session = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasManualSession));
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasManualSession => Session.HasManualSession;

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
        _validation.Cancel();
        _sessionService.SessionChanged -= OnSessionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? StateChanged;

    public bool SaveManualSession(string cookieContent)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
        {
            _shell.Status = "Manual YouTube session was not saved because no cookie content was entered.";
            return false;
        }

        return PersistSession(
            cookieContent.Trim(),
            "Manual YouTube session saved securely.",
            "Manual YouTube session could not be saved because the system keyring is unavailable.");
    }

    public bool SaveWebSession(string cookieContent)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
        {
            _shell.Status = "YouTube web session was not saved because no cookie content was captured.";
            return false;
        }

        return PersistSession(
            cookieContent.Trim(),
            "YouTube web session saved securely.",
            "YouTube session could not be saved because the system keyring is unavailable.");
    }

    private bool PersistSession(string cookieContent, string successMessage, string failureMessage)
    {
        try
        {
            _sessionService.SetManualSession(cookieContent, SessionCookieFormat.NetscapeCookiesText);
        }
        catch (SessionPersistenceException)
        {
            _shell.Status = failureMessage;
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
        catch (SessionPersistenceException)
        {
            _shell.Status = "YouTube session could not be cleared because the system keyring is unavailable.";
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}