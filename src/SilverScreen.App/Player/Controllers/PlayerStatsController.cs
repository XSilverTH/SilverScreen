using System.Threading;
using System.Threading.Tasks;
using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Controllers;

internal interface IPlayerStatsProvider
{
    PlaybackStats? GetPlaybackStats(bool includeTracks = false);
}

internal sealed class PlayerStatsController : IDisposable
{
    private const uint RefreshIntervalMilliseconds = 350;
    private static readonly ILogger Logger = Log.ForContext<PlayerStatsController>();

    private readonly Label _label;
    private readonly DisposeScope _lifetime = new();
    private readonly Revealer _revealer;
    private readonly Func<bool, PlaybackStats?> _statsProvider;

    private uint _refreshTimerSource;
    private int _isRefreshing;
    private bool _pendingRefresh;

    public PlayerStatsController(
        Func<bool, PlaybackStats?> statsProvider,
        Revealer revealer,
        Label label)
    {
        _statsProvider = statsProvider ?? throw new ArgumentNullException(nameof(statsProvider));
        _revealer = revealer ?? throw new ArgumentNullException(nameof(revealer));
        _label = label ?? throw new ArgumentNullException(nameof(label));
    }

    public PlayerStatsController(
        Func<PlaybackStats?> statsProvider,
        Revealer revealer,
        Label label)
        : this(_ => (statsProvider ?? throw new ArgumentNullException(nameof(statsProvider)))(), revealer, label)
    {
    }

    public PlayerStatsController(
        IPlayerStatsProvider statsProvider,
        Revealer revealer,
        Label label)
        : this((statsProvider ?? throw new ArgumentNullException(nameof(statsProvider))).GetPlaybackStats, revealer,
            label)
    {
    }
    public bool IsOpen { get; private set; }

    private int CurrentPage { get; set; } = 1;

    public void Dispose()
    {
        if (_lifetime.IsDisposed) return;
        IsOpen = false;
        _pendingRefresh = false;
        StopTimer();
        _lifetime.Dispose();
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
        if (_lifetime.IsDisposed || IsOpen) return;

        IsOpen = true;
        _revealer.RevealChild = true;
        RefreshContent();
        StartTimer();
    }

    public void Close()
    {
        if (_lifetime.IsDisposed || !IsOpen) return;

        IsOpen = false;
        _pendingRefresh = false;
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
        if (_lifetime.IsDisposed || !IsOpen) return;

        if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) != 0)
        {
            _pendingRefresh = true;
            return;
        }

        var currentPage = CurrentPage;
        var accent = GetAccentColorHex();

        Task.Run(() =>
        {
            try
            {
                var stats = _statsProvider(currentPage == 3);
                if (_lifetime.IsDisposed || !IsOpen)
                {
                    Interlocked.Exchange(ref _isRefreshing, 0);
                    return;
                }

                string markup;
                if (stats is null)
                {
                    markup = "<span foreground=\"#9a9996\">No media loaded or playback inactive.</span>";
                }
                else
                {
                    markup = currentPage switch
                    {
                        1 => PlaybackStatsFormatter.FormatOverviewPageMarkup(stats, accent),
                        2 => PlaybackStatsFormatter.FormatPerformancePageMarkup(stats, accent),
                        3 => PlaybackStatsFormatter.FormatTracksPageMarkup(stats, accent),
                        4 => PlaybackStatsFormatter.FormatEnginePageMarkup(stats, accent),
                        _ => PlaybackStatsFormatter.FormatOverviewPageMarkup(stats, accent)
                    };
                }

                GLib.Functions.IdleAdd(0, () =>
                {
                    try
                    {
                        if (!_lifetime.IsDisposed && IsOpen && CurrentPage == currentPage)
                        {
                            _label.SetMarkup(markup);
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isRefreshing, 0);
                        if (_pendingRefresh && IsOpen && !_lifetime.IsDisposed)
                        {
                            _pendingRefresh = false;
                            RefreshContent();
                        }
                    }

                    return false;
                });
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to refresh playback stats markup");
                Interlocked.Exchange(ref _isRefreshing, 0);
            }
        });
    }

    private void StartTimer()
    {
        StopTimer();
        _refreshTimerSource = _lifetime.Timeout(RefreshIntervalMilliseconds, () =>
        {
            if (_lifetime.IsDisposed || !IsOpen)
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
        _lifetime.Cancel(_refreshTimerSource);
        _refreshTimerSource = 0;
    }
}