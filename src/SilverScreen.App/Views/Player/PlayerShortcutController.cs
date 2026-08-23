using Gtk;
using SilverScreen.Core.Models;
using Functions = Gdk.Functions;

namespace SilverScreen.Views.Player;

public static class PlayerShortcutActions
{
    public const string TogglePause = nameof(PlayerShortcutBindings.TogglePause);
    public const string SeekBackward = nameof(PlayerShortcutBindings.SeekBackward);
    public const string SeekForward = nameof(PlayerShortcutBindings.SeekForward);
    public const string StepFrameBackward = nameof(PlayerShortcutBindings.StepFrameBackward);
    public const string StepFrameForward = nameof(PlayerShortcutBindings.StepFrameForward);
    public const string ToggleMute = nameof(PlayerShortcutBindings.ToggleMute);
    public const string VolumeUp = nameof(PlayerShortcutBindings.VolumeUp);
    public const string VolumeDown = nameof(PlayerShortcutBindings.VolumeDown);
    public const string SeekToBeginning = nameof(PlayerShortcutBindings.SeekToBeginning);
    public const string ReturnToShell = nameof(PlayerShortcutBindings.ReturnToShell);
    public const string ToggleVideoInfo = nameof(PlayerShortcutBindings.ToggleVideoInfo);
    public const string SpeedDecrease = nameof(PlayerShortcutBindings.SpeedDecrease);
    public const string SpeedIncrease = nameof(PlayerShortcutBindings.SpeedIncrease);
    public const string NextVideo = nameof(PlayerShortcutBindings.NextVideo);
    public const string PreviousVideo = nameof(PlayerShortcutBindings.PreviousVideo);
    public const string ToggleFullscreen = nameof(PlayerShortcutBindings.ToggleFullscreen);
    public const string PreferredSubtitle = nameof(PlayerShortcutBindings.PreferredSubtitle);
    public const string ResumeOrSkip = nameof(PlayerShortcutBindings.ResumeOrSkip);
    public const string ToggleQueue = nameof(PlayerShortcutBindings.ToggleQueue);
}

internal sealed class PlayerShortcutController : IDisposable
{
    private readonly Dictionary<string, List<Action>> _actionHandlers = new(StringComparer.Ordinal);
    private readonly Func<bool> _hasMedia;
    private readonly Action _registerActivity;
    private readonly Dictionary<uint, string> _shortcutMap = [];
    private readonly Widget _viewWidget;
    private bool _disposed;

    private EventControllerKey? _keyboardController;
    private Widget? _keyboardRoot;

    public PlayerShortcutController(Widget viewWidget, Func<bool> hasMedia, Action registerActivity)
    {
        _viewWidget = viewWidget;
        _hasMedia = hasMedia;
        _registerActivity = registerActivity;

        var key = EventControllerKey.New();
        key.SetPropagationPhase(PropagationPhase.Capture);
        key.OnKeyPressed += OnKeyPressed;
        _keyboardController = key;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        _actionHandlers.Clear();
        _shortcutMap.Clear();
        _keyboardController?.Dispose();
        _keyboardController = null;
    }

    public void RegisterAction(string actionName, Action action)
    {
        if (!_actionHandlers.TryGetValue(actionName, out var list))
        {
            list = [];
            _actionHandlers[actionName] = list;
        }

        list.Add(action);
    }

    public void UpdateBindings(PlayerShortcutBindings shortcuts)
    {
        _shortcutMap.Clear();
        Bind(PlayerShortcutActions.TogglePause, shortcuts.TogglePause);
        Bind(PlayerShortcutActions.SeekBackward, shortcuts.SeekBackward);
        Bind(PlayerShortcutActions.SeekForward, shortcuts.SeekForward);
        Bind(PlayerShortcutActions.StepFrameBackward, shortcuts.StepFrameBackward);
        Bind(PlayerShortcutActions.StepFrameForward, shortcuts.StepFrameForward);
        Bind(PlayerShortcutActions.ToggleMute, shortcuts.ToggleMute);
        Bind(PlayerShortcutActions.VolumeUp, shortcuts.VolumeUp);
        Bind(PlayerShortcutActions.VolumeDown, shortcuts.VolumeDown);
        Bind(PlayerShortcutActions.SeekToBeginning, shortcuts.SeekToBeginning);
        Bind(PlayerShortcutActions.ReturnToShell, shortcuts.ReturnToShell);
        Bind(PlayerShortcutActions.ToggleVideoInfo, shortcuts.ToggleVideoInfo);
        Bind(PlayerShortcutActions.SpeedDecrease, shortcuts.SpeedDecrease);
        Bind(PlayerShortcutActions.SpeedIncrease, shortcuts.SpeedIncrease);
        Bind(PlayerShortcutActions.NextVideo, shortcuts.NextVideo);
        Bind(PlayerShortcutActions.PreviousVideo, shortcuts.PreviousVideo);
        Bind(PlayerShortcutActions.ToggleFullscreen, shortcuts.ToggleFullscreen);
        Bind(PlayerShortcutActions.PreferredSubtitle, shortcuts.PreferredSubtitle);
        Bind(PlayerShortcutActions.ResumeOrSkip, shortcuts.ResumeOrSkip);
        Bind(PlayerShortcutActions.ToggleQueue, shortcuts.ToggleQueue);
    }

    public void Attach()
    {
        if (_disposed || _keyboardController is null || _keyboardRoot is not null) return;
        if (_viewWidget.GetRoot() is not Widget root) return;

        root.AddController(_keyboardController);
        _keyboardRoot = root;
    }

    public void Detach()
    {
        if (_keyboardRoot is not null && _keyboardController is not null)
        {
            _keyboardRoot.RemoveController(_keyboardController);
            _keyboardRoot = null;
        }
    }

    private void Bind(string actionName, IEnumerable<string> keyNames)
    {
        foreach (var keyName in keyNames)
        {
            if (string.IsNullOrWhiteSpace(keyName)) continue;
            var keyval = Functions.KeyvalFromName(keyName.Trim());
            if (keyval == 0) continue;

            _shortcutMap[Functions.KeyvalToLower(keyval)] = actionName;
        }
    }

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        if (_disposed || !_hasMedia()) return false;
        var keyval = Functions.KeyvalToLower(args.Keyval);
        if (!_shortcutMap.TryGetValue(keyval, out var actionName)) return false;
        if (!_actionHandlers.TryGetValue(actionName, out var handlers) || handlers.Count == 0) return false;

        foreach (var handler in handlers) handler();

        _registerActivity();
        return true;
    }
}