using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Player;
using static GLib.Functions;

namespace SilverScreen.Player.Controllers;

internal sealed class PlayerStatsController(
    LibMpvPlayer player,
    Revealer revealer,
    Label label)
    : IDisposable
{
    private const uint RefreshIntervalMilliseconds = 350;
    private static readonly ILogger Logger = Log.ForContext<PlayerStatsController>();

    private readonly Label _label = label ?? throw new ArgumentNullException(nameof(label));
    private readonly LibMpvPlayer _player = player ?? throw new ArgumentNullException(nameof(player));
    private readonly Revealer _revealer = revealer ?? throw new ArgumentNullException(nameof(revealer));

    private bool _disposed;
    private uint _refreshTimerSource;

    public bool IsOpen { get; private set; }

    private int CurrentPage { get; set; } = 1;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTimer();
    }

    private static string GetAccentColorHex()
    {
        try
        {
            var sm = StyleManager.GetDefault();
            var rgba = sm.AccentColorRgba;
            var r = (int)Math.Round(Math.Clamp(rgba.Red, 0, 1) * 255);
            var g = (int)Math.Round(Math.Clamp(rgba.Green, 0, 1) * 255);
            var b = (int)Math.Round(Math.Clamp(rgba.Blue, 0, 1) * 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch
        {
            return "#78aeed";
        }
    }

    private void Show()
    {
        if (_disposed || IsOpen) return;

        IsOpen = true;
        _revealer.RevealChild = true;
        RefreshContent();
        StartTimer();
    }

    public void Close()
    {
        if (_disposed || !IsOpen) return;

        IsOpen = false;
        _revealer.RevealChild = false;
        StopTimer();
    }

    public void Toggle()
    {
        if (!IsOpen)
        {
            CurrentPage = 1;
            Show();
        }
        else
        {
            CyclePage();
        }
    }

    private void CyclePage()
    {
        if (!IsOpen)
        {
            Show();
            return;
        }

        if (CurrentPage < 4)
        {
            CurrentPage++;
            RefreshContent();
        }
        else
        {
            Close();
        }
    }

    private void SetPage(int page)
    {
        if (page is < 1 or > 4) return;
        CurrentPage = page;
        if (!IsOpen) Show();
        else RefreshContent();
    }

    public bool HandleKeyPress(uint keyVal)
    {
        if (!IsOpen) return false;

        // Number keys 1-4 (standard top-row and keypad)
        switch (keyVal)
        {
            case 0x31: // '1'
            case 0xFFB1: // KP_1
                SetPage(1);
                return true;
            case 0x32: // '2'
            case 0xFFB2: // KP_2
                SetPage(2);
                return true;
            case 0x33: // '3'
            case 0xFFB3: // KP_3
                SetPage(3);
                return true;
            case 0x34: // '4'
            case 0xFFB4: // KP_4
                SetPage(4);
                return true;
            default:
                return false;
        }
    }

    private void RefreshContent()
    {
        if (_disposed || !IsOpen) return;

        try
        {
            var stats = _player.GetPlaybackStats();
            if (stats is null)
            {
                _label.SetMarkup("<span foreground=\"#9a9996\">No media loaded or playback inactive.</span>");
                return;
            }

            var accent = GetAccentColorHex();
            var markup = CurrentPage switch
            {
                1 => PlaybackStatsFormatter.FormatOverviewPageMarkup(stats, accent),
                2 => PlaybackStatsFormatter.FormatPerformancePageMarkup(stats, accent),
                3 => PlaybackStatsFormatter.FormatTracksPageMarkup(stats, accent),
                4 => PlaybackStatsFormatter.FormatEnginePageMarkup(stats, accent),
                _ => PlaybackStatsFormatter.FormatOverviewPageMarkup(stats, accent)
            };

            _label.SetMarkup(markup);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to refresh playback stats markup");
        }
    }

    private void StartTimer()
    {
        StopTimer();
        _refreshTimerSource = TimeoutAdd(0, RefreshIntervalMilliseconds, () =>
        {
            if (_disposed || !IsOpen)
            {
                _refreshTimerSource = 0;
                return false;
            }

            RefreshContent();
            return true;
        });
    }

    private void StopTimer()
    {
        if (_refreshTimerSource == 0) return;
        SourceRemove(_refreshTimerSource);
        _refreshTimerSource = 0;
    }
}