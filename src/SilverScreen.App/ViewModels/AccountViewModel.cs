using System.ComponentModel;
using System.Runtime.CompilerServices;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.ViewModels;

public sealed class AccountViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISessionService _sessionService;
    private readonly SessionValidationCoordinator _validation;
    private readonly ShellViewModel _shell;
    private AccountSession _session;
    private bool _isValidating;
    private bool _disposed;

    public AccountViewModel(ISessionService sessionService, SessionValidationCoordinator validation,
        ShellViewModel shell)
    {
        _sessionService = sessionService;
        _validation = validation;
        _shell = shell;
        _session = _sessionService.GetCurrentSession();
        _sessionService.SessionChanged += OnSessionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? StateChanged;

    public AccountSession Session
    {
        get => _session;
        private set
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
        get => _isValidating;
        private set
        {
            if (_isValidating == value)
                return;

            _isValidating = value;
            OnPropertyChanged();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool SaveManualSession(string cookieContent)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
        {
            _shell.Status = "Manual YouTube session was not saved because no cookie content was entered.";
            return false;
        }

        try
        {
            _sessionService.SetManualSession(cookieContent.Trim(), SessionCookieFormat.NetscapeCookiesText);
        }
        catch (SessionPersistenceException)
        {
            _shell.Status = "Manual YouTube session could not be saved because the system keyring is unavailable.";
            return false;
        }

        _shell.Status = "Manual YouTube session saved securely.";
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
            _shell.Status = "Manual YouTube session could not be cleared because the system keyring is unavailable.";
            return;
        }

        _shell.Status = "Manual YouTube session cleared.";
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _validation.Cancel();
        _sessionService.SessionChanged -= OnSessionChanged;
    }
}