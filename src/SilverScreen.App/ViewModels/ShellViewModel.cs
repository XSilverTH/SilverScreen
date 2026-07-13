using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SilverScreen.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status
    {
        get;
        set => SetField(ref field, value);
    } = "Ready";

    public string SelectedPage
    {
        get;
        set => SetField(ref field, value);
    } = "home";

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}