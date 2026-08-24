using SilverScreen.Core.Common;

namespace SilverScreen.Core.Preferences;

public sealed record PlayerShortcutBindings
{
    public EquatableArray<string> TogglePause { get; set; } = ["space", "k"];
    public EquatableArray<string> SeekBackward { get; set; } = ["Left", "j"];
    public EquatableArray<string> SeekForward { get; set; } = ["Right", "l"];
    public EquatableArray<string> StepFrameBackward { get; set; } = ["comma", "less"];
    public EquatableArray<string> StepFrameForward { get; set; } = ["period", "greater"];
    public EquatableArray<string> ToggleMute { get; set; } = ["m"];
    public EquatableArray<string> VolumeUp { get; set; } = ["Up"];
    public EquatableArray<string> VolumeDown { get; set; } = ["Down"];
    public EquatableArray<string> SeekToBeginning { get; set; } = ["0", "Home"];
    public EquatableArray<string> ReturnToShell { get; set; } = ["Escape"];
    public EquatableArray<string> ToggleVideoInfo { get; set; } = ["d"];
    public EquatableArray<string> SpeedDecrease { get; set; } = ["bracketleft", "braceleft"];
    public EquatableArray<string> SpeedIncrease { get; set; } = ["bracketright", "braceright"];
    public EquatableArray<string> NextVideo { get; set; } = ["n"];
    public EquatableArray<string> PreviousVideo { get; set; } = ["p"];
    public EquatableArray<string> ToggleFullscreen { get; set; } = ["f"];
    public EquatableArray<string> PreferredSubtitle { get; set; } = ["c"];
    public EquatableArray<string> ResumeOrSkip { get; set; } = ["Return", "KP_Enter"];
    public EquatableArray<string> ToggleQueue { get; set; } = ["q"];
}