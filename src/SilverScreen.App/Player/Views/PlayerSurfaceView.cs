using Gtk;
using SilverScreen.Infrastructure.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Views;

/// <summary>
///     Encapsulates the OpenGL GLArea surface, hardware offload, and libmpv render scheduling.
/// </summary>
public partial class PlayerSurfaceView : ViewBase<GraphicsOffload>
{
    private LibMpvPlayer? _player;
    private bool _rendererReady;
    private int _surfacePixelHeight;
    private int _surfacePixelWidth;

    public bool IsRendererReady => _rendererReady;
    public GLArea Surface => player_surface;
    public (int Width, int Height) SurfaceDimensions => (_surfacePixelWidth, _surfacePixelHeight);

    public event EventHandler? Realized;
    public event EventHandler? Unrealized;
    public event EventHandler<string>? PlaybackFailed;

    public void AttachPlayer(LibMpvPlayer player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        Lifetime.Track(
            () => _player.RenderRequested += OnRenderRequested,
            () => _player.RenderRequested -= OnRenderRequested);
    }

    public void GrabFocus()
    {
        player_surface.GrabFocus();
    }

    public void MakeCurrent()
    {
        player_surface.MakeCurrent();
    }

    private void OnRenderRequested(object? sender, EventArgs args)
    {
        player_surface.QueueRender();
    }

    private void OnSurfaceResize(GLArea sender, GLArea.ResizeSignalArgs args)
    {
        _surfacePixelWidth = args.Width;
        _surfacePixelHeight = args.Height;
    }

    private void OnSurfaceRealize(object? sender, EventArgs args)
    {
        player_surface.MakeCurrent();
        if (player_surface.GetError() is not null)
        {
            PlaybackFailed?.Invoke(this, "Unable to create an OpenGL context for embedded playback.");
            return;
        }

        if (_player is not null)
        {
            _player.InitializeRenderer();
            _rendererReady = true;
        }

        Realized?.Invoke(this, EventArgs.Empty);
    }

    private void OnSurfaceUnrealize(object? sender, EventArgs args)
    {
        player_surface.MakeCurrent();
        _player?.ShutdownRenderer();
        _rendererReady = false;
        _surfacePixelWidth = 0;
        _surfacePixelHeight = 0;
        Unrealized?.Invoke(this, EventArgs.Empty);
    }

    private bool OnSurfaceRender(GLArea sender, GLArea.RenderSignalArgs args)
    {
        if (IsDisposed || !_rendererReady || _player is null)
            return false;

        var width = _surfacePixelWidth > 0
            ? _surfacePixelWidth
            : player_surface.GetAllocatedWidth() * player_surface.GetScaleFactor();
        var height = _surfacePixelHeight > 0
            ? _surfacePixelHeight
            : player_surface.GetAllocatedHeight() * player_surface.GetScaleFactor();

        _player.Render(width, height);
        return true;
    }
}
