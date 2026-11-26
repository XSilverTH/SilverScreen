using Functions = GLib.Functions;
using MainContext = GLib.MainContext;

namespace SilverScreen.Player;

internal static class GtkUiDispatch
{
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (MainContext.Default().IsOwner())
        {
            action();
            return;
        }

        Functions.IdleAdd(0, () =>
        {
            action();
            return false;
        });
    }

    public static void Run(Func<bool> isAlive, Action action)
    {
        ArgumentNullException.ThrowIfNull(isAlive);
        ArgumentNullException.ThrowIfNull(action);
        if (MainContext.Default().IsOwner())
        {
            if (isAlive())
                action();
            return;
        }

        Functions.IdleAdd(0, () =>
        {
            if (isAlive())
                action();
            return false;
        });
    }
}