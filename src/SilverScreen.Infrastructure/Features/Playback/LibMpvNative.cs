using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SilverScreen.Infrastructure.Features.Playback;

internal enum LibMpvFormat
{
    None = 0,
    String = 1,
    Flag = 3,
    Int64 = 4,
    Double = 5
}

internal enum LibMpvEventId
{
    None = 0,
    Shutdown = 1,
    StartFile = 6,
    EndFile = 7,
    FileLoaded = 8,
    PropertyChange = 22
}

internal enum LibMpvEndFileReason
{
    Eof = 0,
    Stop = 2,
    Quit = 3,
    Error = 4,
    Redirect = 5
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LibMpvEvent(int eventId, int error, ulong replyUserdata, nint data)
{
    public readonly int EventId = eventId;
    public readonly int Error = error;
    public readonly ulong ReplyUserdata = replyUserdata;
    public readonly nint Data = data;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LibMpvEventProperty(nint name, LibMpvFormat format, nint data)
{
    public readonly nint Name = name;
    public readonly LibMpvFormat Format = format;
    public readonly nint Data = data;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LibMpvEventEndFile(
    LibMpvEndFileReason reason,
    int error,
    long playlistEntryId,
    long playlistInsertId,
    int playlistInsertEntries)
{
    public readonly LibMpvEndFileReason Reason = reason;
    public readonly int Error = error;
    public readonly long PlaylistEntryId = playlistEntryId;
    public readonly long PlaylistInsertId = playlistInsertId;
    public readonly int PlaylistInsertEntries = playlistInsertEntries;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LibMpvRenderParam(int type, nint data)
{
    public readonly int Type = type;
    public readonly nint Data = data;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LibMpvOpenGlInitParams(nint getProcAddress, nint getProcAddressContext)
{
    public readonly nint GetProcAddress = getProcAddress;
    public readonly nint GetProcAddressContext = getProcAddressContext;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LibMpvOpenGlFbo(int fbo, int width, int height, int internalFormat)
{
    public readonly int Fbo = fbo;
    public readonly int Width = width;
    public readonly int Height = height;
    public readonly int InternalFormat = internalFormat;
}

internal interface ILibMpvNativeApi : IDisposable
{
    bool IsAvailable { get; }
    string? AvailabilityError { get; }
    nint Create();
    int SetOptionString(nint handle, string name, string value);
    int Initialize(nint handle);
    int ObserveProperty(nint handle, ulong replyUserdata, string name, LibMpvFormat format);
    int SetPropertyString(nint handle, string name, string value);
    int SetPropertyDouble(nint handle, string name, double value);
    int SetPropertyFlag(nint handle, string name, bool value);
    int SetPropertyInt64(nint handle, string name, long value);
    int Command(nint handle, params string[] arguments);
    LibMpvEvent WaitEvent(nint handle, double timeout);
    void Wakeup(nint handle);
    string ErrorString(int error);
    int CreateRenderContext(out nint context, nint handle);
    void SetRenderUpdateCallback(nint context, nint callback, nint callbackData);
    int GetFramebufferBinding();
    int Render(nint context, int framebuffer, int width, int height);
    void FreeRenderContext(nint context);
    void Destroy(nint handle);
}

internal sealed unsafe class LibMpvNative : ILibMpvNativeApi
{
    private static readonly string[] MpvLibraryNames = ["libmpv.so.2", "libmpv.so.1", "libmpv.so"];
    private static readonly string[] EpoxyLibraryNames = ["libepoxy.so.0", "libepoxy.so"];
    private static LibMpvNative? CurrentForOpenGl;
    private readonly delegate* unmanaged[Cdecl]<nint, byte**, int> _command;
    private readonly delegate* unmanaged[Cdecl]<nint> _create;
    private readonly delegate* unmanaged[Cdecl]<nint*, nint, LibMpvRenderParam*, int> _createRenderContext;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _destroy;
    private readonly delegate* unmanaged[Cdecl]<byte*, nint> _eglGetProcAddress;
    private readonly delegate* unmanaged[Cdecl]<int, byte*> _errorString;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _freeRenderContext;
    private readonly delegate* unmanaged[Cdecl]<byte*, nint> _glxGetProcAddress;
    private readonly delegate* unmanaged[Cdecl]<nint, int> _initialize;
    private readonly delegate* unmanaged[Cdecl]<nint, ulong, byte*, LibMpvFormat, int> _observeProperty;
    private readonly delegate* unmanaged[Cdecl]<nint, LibMpvRenderParam*, int> _render;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, byte*, int> _setOptionString;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, LibMpvFormat, void*, int> _setProperty;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, byte*, int> _setPropertyString;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, nint, void> _setRenderUpdateCallback;
    private readonly delegate* unmanaged[Cdecl]<nint, double, LibMpvEvent*> _waitEvent;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _wakeup;
    private nint _epoxyLibrary;
    private nint _mpvLibrary;

    public LibMpvNative()
    {
        EnsureMpvNumericLocale();
        if (!TryLoad(MpvLibraryNames, out _mpvLibrary))
        {
            AvailabilityError = "libmpv could not be loaded.";
            return;
        }

        if (!TryLoad(EpoxyLibraryNames, out _epoxyLibrary))
        {
            AvailabilityError = "libepoxy could not be loaded.";
            Dispose();
            return;
        }

        try
        {
            _create = (delegate* unmanaged[Cdecl]<nint>)GetExport("mpv_create");
            _setOptionString = (delegate* unmanaged[Cdecl]<nint, byte*, byte*, int>)GetExport("mpv_set_option_string");
            _initialize = (delegate* unmanaged[Cdecl]<nint, int>)GetExport("mpv_initialize");
            _observeProperty =
                (delegate* unmanaged[Cdecl]<nint, ulong, byte*, LibMpvFormat, int>)GetExport("mpv_observe_property");
            _setProperty =
                (delegate* unmanaged[Cdecl]<nint, byte*, LibMpvFormat, void*, int>)GetExport("mpv_set_property");
            _setPropertyString =
                (delegate* unmanaged[Cdecl]<nint, byte*, byte*, int>)GetExport("mpv_set_property_string");
            _command = (delegate* unmanaged[Cdecl]<nint, byte**, int>)GetExport("mpv_command");
            _waitEvent = (delegate* unmanaged[Cdecl]<nint, double, LibMpvEvent*>)GetExport("mpv_wait_event");
            _wakeup = (delegate* unmanaged[Cdecl]<nint, void>)GetExport("mpv_wakeup");
            _errorString = (delegate* unmanaged[Cdecl]<int, byte*>)GetExport("mpv_error_string");
            _createRenderContext =
                (delegate* unmanaged[Cdecl]<nint*, nint, LibMpvRenderParam*, int>)GetExport(
                    "mpv_render_context_create");
            _setRenderUpdateCallback =
                (delegate* unmanaged[Cdecl]<nint, nint, nint, void>)GetExport("mpv_render_context_set_update_callback");
            _render = (delegate* unmanaged[Cdecl]<nint, LibMpvRenderParam*, int>)GetExport("mpv_render_context_render");
            _freeRenderContext = (delegate* unmanaged[Cdecl]<nint, void>)GetExport("mpv_render_context_free");
            _destroy = (delegate* unmanaged[Cdecl]<nint, void>)GetExport("mpv_terminate_destroy");
            _eglGetProcAddress = GetEpoxyResolver("epoxy_eglGetProcAddress");
            _glxGetProcAddress = GetEpoxyResolver("epoxy_glXGetProcAddress");
            CurrentForOpenGl = this;
            IsLoaded = true;
        }
        catch (Exception exception)
        {
            AvailabilityError = exception.Message;
            Dispose();
        }
    }

    public bool IsLoaded { get; }

    bool ILibMpvNativeApi.IsAvailable => IsLoaded;
    public string? AvailabilityError { get; }

    public nint Create()
    {
        return _create();
    }

    public int SetOptionString(nint handle, string name, string value)
    {
        var nativeName = Utf8(name);
        var nativeValue = Utf8(value);
        fixed (byte* namePointer = nativeName)
        fixed (byte* valuePointer = nativeValue)
        {
            return _setOptionString(handle, namePointer, valuePointer);
        }
    }

    public int Initialize(nint handle)
    {
        return _initialize(handle);
    }

    public int ObserveProperty(nint handle, ulong replyUserdata, string name, LibMpvFormat format)
    {
        var nativeName = Utf8(name);
        fixed (byte* namePointer = nativeName)
        {
            return _observeProperty(handle, replyUserdata, namePointer, format);
        }
    }

    public int SetPropertyString(nint handle, string name, string value)
    {
        var nativeName = Utf8(name);
        var nativeValue = Utf8(value);
        fixed (byte* namePointer = nativeName)
        fixed (byte* valuePointer = nativeValue)
        {
            return _setPropertyString(handle, namePointer, valuePointer);
        }
    }

    public int SetPropertyDouble(nint handle, string name, double value)
    {
        var nativeName = Utf8(name);
        fixed (byte* namePointer = nativeName)
        {
            return _setProperty(handle, namePointer, LibMpvFormat.Double, &value);
        }
    }

    public int SetPropertyFlag(nint handle, string name, bool value)
    {
        var nativeName = Utf8(name);
        var nativeValue = value ? 1 : 0;
        fixed (byte* namePointer = nativeName)
        {
            return _setProperty(handle, namePointer, LibMpvFormat.Flag, &nativeValue);
        }
    }

    public int SetPropertyInt64(nint handle, string name, long value)
    {
        var nativeName = Utf8(name);
        fixed (byte* namePointer = nativeName)
        {
            return _setProperty(handle, namePointer, LibMpvFormat.Int64, &value);
        }
    }

    public int Command(nint handle, params string[] arguments)
    {
        var utf8 = arguments.Select(Utf8).ToArray();
        var pointers = stackalloc byte*[utf8.Length + 1];
        var pins = new GCHandle[utf8.Length];
        try
        {
            for (var index = 0; index < utf8.Length; index++)
            {
                pins[index] = GCHandle.Alloc(utf8[index], GCHandleType.Pinned);
                pointers[index] = (byte*)pins[index].AddrOfPinnedObject();
            }

            pointers[utf8.Length] = null;
            return _command(handle, pointers);
        }
        finally
        {
            foreach (var pin in pins)
                if (pin.IsAllocated)
                    pin.Free();
        }
    }

    public LibMpvEvent WaitEvent(nint handle, double timeout)
    {
        return *_waitEvent(handle, timeout);
    }

    public void Wakeup(nint handle)
    {
        _wakeup(handle);
    }

    public string ErrorString(int error)
    {
        return Marshal.PtrToStringUTF8((nint)_errorString(error)) ?? $"mpv error {error}";
    }

    public int CreateRenderContext(out nint context, nint handle)
    {
        var apiType = Utf8("opengl");
        fixed (byte* apiTypePointer = apiType)
        {
            var initParameters =
                new LibMpvOpenGlInitParams((nint)(delegate* unmanaged[Cdecl]<nint, byte*, nint>)&GetOpenGlProcAddress,
                    0);
            var parameters = stackalloc LibMpvRenderParam[3];
            nint nativeContext = 0;
            parameters[0] = new LibMpvRenderParam(1, (nint)apiTypePointer);
            parameters[1] = new LibMpvRenderParam(2, (nint)(&initParameters));
            parameters[2] = new LibMpvRenderParam(0, 0);
            var result = _createRenderContext(&nativeContext, handle, parameters);
            context = nativeContext;
            return result;
        }
    }

    public void SetRenderUpdateCallback(nint context, nint callback, nint callbackData)
    {
        _setRenderUpdateCallback(context, callback, callbackData);
    }

    public int GetFramebufferBinding()
    {
        var functionName = Utf8("glGetIntegerv");
        fixed (byte* functionNamePointer = functionName)
        {
            var function = (delegate* unmanaged[Cdecl]<uint, int*, void>)ResolveOpenGlProcAddress(functionNamePointer);
            if (function == null) throw new InvalidOperationException("libepoxy could not resolve glGetIntegerv.");
            var framebuffer = 0;
            function(0x8CA6, &framebuffer);
            return framebuffer;
        }
    }

    public int Render(nint context, int framebuffer, int width, int height)
    {
        var fbo = new LibMpvOpenGlFbo(framebuffer, width, height, 0);
        var flipY = 1;
        var parameters = stackalloc LibMpvRenderParam[3];
        parameters[0] = new LibMpvRenderParam(3, (nint)(&fbo));
        parameters[1] = new LibMpvRenderParam(4, (nint)(&flipY));
        parameters[2] = new LibMpvRenderParam(0, 0);
        return _render(context, parameters);
    }

    public void FreeRenderContext(nint context)
    {
        if (context != 0) _freeRenderContext(context);
    }

    public void Destroy(nint handle)
    {
        if (handle != 0) _destroy(handle);
    }

    public void Dispose()
    {
        if (ReferenceEquals(CurrentForOpenGl, this)) CurrentForOpenGl = null;
        if (_epoxyLibrary != 0)
        {
            NativeLibrary.Free(_epoxyLibrary);
            _epoxyLibrary = 0;
        }

        if (_mpvLibrary != 0)
        {
            NativeLibrary.Free(_mpvLibrary);
            _mpvLibrary = 0;
        }
    }

    public static bool IsAvailable()
    {
        if (!TryLoad(MpvLibraryNames, out var library)) return false;
        NativeLibrary.Free(library);
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint GetOpenGlProcAddress(nint context, byte* name)
    {
        var native = CurrentForOpenGl;
        return native is null ? 0 : native.ResolveOpenGlProcAddress(name);
    }

    private nint GetExport(string name)
    {
        return NativeLibrary.GetExport(_mpvLibrary, name);
    }

    private delegate* unmanaged[Cdecl]<byte*, nint> GetEpoxyResolver(string exportName)
    {
        if (!NativeLibrary.TryGetExport(_epoxyLibrary, exportName, out var export)) return null;
        return (delegate* unmanaged[Cdecl]<byte*, nint>)(*(nint*)export);
    }

    private nint ResolveOpenGlProcAddress(byte* name)
    {
        var address = _eglGetProcAddress == null ? 0 : _eglGetProcAddress(name);
        return address != 0 || _glxGetProcAddress == null ? address : _glxGetProcAddress(name);
    }

    private static void EnsureMpvNumericLocale()
    {
        const int lcNumeric = 1;
        if (SetLocale(lcNumeric, "C") == 0)
            throw new InvalidOperationException("libmpv requires the process LC_NUMERIC locale to be C.");
    }

    [DllImport("libc", EntryPoint = "setlocale", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SetLocale(int category, [MarshalAs(UnmanagedType.LPUTF8Str)] string locale);

    private static bool TryLoad(IEnumerable<string> names, out nint library)
    {
        foreach (var name in names)
            if (NativeLibrary.TryLoad(name, out library))
                return true;

        library = 0;
        return false;
    }

    private static byte[] Utf8(string value)
    {
        return Encoding.UTF8.GetBytes(value + "\0");
    }
}