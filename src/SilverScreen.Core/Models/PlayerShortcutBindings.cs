namespace SilverScreen.Core.Models;

public sealed class PlayerShortcutBindings : IEquatable<PlayerShortcutBindings>
{
    public string[] TogglePause { get; set; } = ["space", "k"];
    public string[] SeekBackward { get; set; } = ["Left", "j"];
    public string[] SeekForward { get; set; } = ["Right", "l"];
    public string[] StepFrameBackward { get; set; } = ["comma", "less"];
    public string[] StepFrameForward { get; set; } = ["period", "greater"];
    public string[] ToggleMute { get; set; } = ["m"];
    public string[] VolumeUp { get; set; } = ["Up"];
    public string[] VolumeDown { get; set; } = ["Down"];
    public string[] SeekToBeginning { get; set; } = ["0", "Home"];
    public string[] ReturnToShell { get; set; } = ["Escape"];
    public string[] ToggleVideoInfo { get; set; } = ["d"];
    public string[] SpeedDecrease { get; set; } = ["bracketleft", "braceleft"];
    public string[] SpeedIncrease { get; set; } = ["bracketright", "braceright"];
    public string[] NextVideo { get; set; } = ["n"];
    public string[] PreviousVideo { get; set; } = ["p"];
    public string[] ToggleFullscreen { get; set; } = ["f"];
    public string[] PreferredSubtitle { get; set; } = ["c"];
    public string[] ResumeOrSkip { get; set; } = ["Return", "KP_Enter"];
    public string[] ToggleQueue { get; set; } = ["q"];

    public bool Equals(PlayerShortcutBindings? other)
    {
        return other is not null &&
               TogglePause.SequenceEqual(other.TogglePause, StringComparer.Ordinal) &&
               SeekBackward.SequenceEqual(other.SeekBackward, StringComparer.Ordinal) &&
               SeekForward.SequenceEqual(other.SeekForward, StringComparer.Ordinal) &&
               StepFrameBackward.SequenceEqual(other.StepFrameBackward, StringComparer.Ordinal) &&
               StepFrameForward.SequenceEqual(other.StepFrameForward, StringComparer.Ordinal) &&
               ToggleMute.SequenceEqual(other.ToggleMute, StringComparer.Ordinal) &&
               VolumeUp.SequenceEqual(other.VolumeUp, StringComparer.Ordinal) &&
               VolumeDown.SequenceEqual(other.VolumeDown, StringComparer.Ordinal) &&
               SeekToBeginning.SequenceEqual(other.SeekToBeginning, StringComparer.Ordinal) &&
               ReturnToShell.SequenceEqual(other.ReturnToShell, StringComparer.Ordinal) &&
               ToggleVideoInfo.SequenceEqual(other.ToggleVideoInfo, StringComparer.Ordinal) &&
               SpeedDecrease.SequenceEqual(other.SpeedDecrease, StringComparer.Ordinal) &&
               SpeedIncrease.SequenceEqual(other.SpeedIncrease, StringComparer.Ordinal) &&
               NextVideo.SequenceEqual(other.NextVideo, StringComparer.Ordinal) &&
               PreviousVideo.SequenceEqual(other.PreviousVideo, StringComparer.Ordinal) &&
               ToggleFullscreen.SequenceEqual(other.ToggleFullscreen, StringComparer.Ordinal) &&
               PreferredSubtitle.SequenceEqual(other.PreferredSubtitle, StringComparer.Ordinal) &&
               ResumeOrSkip.SequenceEqual(other.ResumeOrSkip, StringComparer.Ordinal) &&
               ToggleQueue.SequenceEqual(other.ToggleQueue, StringComparer.Ordinal);
    }

    public PlayerShortcutBindings Clone()
    {
        return new PlayerShortcutBindings
        {
            TogglePause = [.. TogglePause],
            SeekBackward = [.. SeekBackward],
            SeekForward = [.. SeekForward],
            StepFrameBackward = [.. StepFrameBackward],
            StepFrameForward = [.. StepFrameForward],
            ToggleMute = [.. ToggleMute],
            VolumeUp = [.. VolumeUp],
            VolumeDown = [.. VolumeDown],
            SeekToBeginning = [.. SeekToBeginning],
            ReturnToShell = [.. ReturnToShell],
            ToggleVideoInfo = [.. ToggleVideoInfo],
            SpeedDecrease = [.. SpeedDecrease],
            SpeedIncrease = [.. SpeedIncrease],
            NextVideo = [.. NextVideo],
            PreviousVideo = [.. PreviousVideo],
            ToggleFullscreen = [.. ToggleFullscreen],
            PreferredSubtitle = [.. PreferredSubtitle],
            ResumeOrSkip = [.. ResumeOrSkip],
            ToggleQueue = [.. ToggleQueue]
        };
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as PlayerShortcutBindings);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        AddToHash(ref hash, TogglePause);
        AddToHash(ref hash, SeekBackward);
        AddToHash(ref hash, SeekForward);
        AddToHash(ref hash, StepFrameBackward);
        AddToHash(ref hash, StepFrameForward);
        AddToHash(ref hash, ToggleMute);
        AddToHash(ref hash, VolumeUp);
        AddToHash(ref hash, VolumeDown);
        AddToHash(ref hash, SeekToBeginning);
        AddToHash(ref hash, ReturnToShell);
        AddToHash(ref hash, ToggleVideoInfo);
        AddToHash(ref hash, SpeedDecrease);
        AddToHash(ref hash, SpeedIncrease);
        AddToHash(ref hash, NextVideo);
        AddToHash(ref hash, PreviousVideo);
        AddToHash(ref hash, ToggleFullscreen);
        AddToHash(ref hash, PreferredSubtitle);
        AddToHash(ref hash, ResumeOrSkip);
        AddToHash(ref hash, ToggleQueue);
        return hash.ToHashCode();

        static void AddToHash(ref HashCode hash, IEnumerable<string> values)
        {
            foreach (var value in values) hash.Add(value, StringComparer.Ordinal);
        }
    }
}