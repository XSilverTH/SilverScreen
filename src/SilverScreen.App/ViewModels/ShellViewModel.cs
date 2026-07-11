using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SilverScreen.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private string _status = "Ready";
    private string _selectedPage = "home";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string SelectedPage
    {
        get => _selectedPage;
        set => SetField(ref _selectedPage, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
