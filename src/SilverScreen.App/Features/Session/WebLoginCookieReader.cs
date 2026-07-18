using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Gio.Internal;
using GLib;
using GLib.Internal;
using GObject.Internal;
using Soup.Internal;
using WebKit;
using Cookie = Soup.Cookie;

namespace SilverScreen.Features.Session;

internal sealed record WebCookieSnapshot(
    string Name,
    string Value,
    string Domain,
    string Path,
    bool Secure,
    bool HttpOnly,
    long ExpiresUnix);

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal static class WebLoginCookieReader
{
    private const string NetscapeHeader = "# Netscape HTTP Cookie File\n";

    [DllImport("libglib-2.0.so.0", EntryPoint = "g_list_free")]
    private static extern void FreeList(IntPtr list);

    internal static Task<IReadOnlyList<WebCookieSnapshot>> GetCookiesAsync(CookieManager manager, string uri)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        var completion = new TaskCompletionSource<IReadOnlyList<WebCookieSnapshot>>();
        var callbackHandler = new AsyncReadyCallbackAsyncHandler((sourceObject, result, _) =>
        {
            if (sourceObject is null)
            {
                completion.SetException(new InvalidOperationException("Missing source object"));
                return;
            }

            ListOwnedHandle? listHandle = null;
            var list = IntPtr.Zero;
            try
            {
                listHandle = WebKit.Internal.CookieManager.GetCookiesFinish(
                    sourceObject.Handle.DangerousGetHandle(),
                    result.Handle.DangerousGetHandle(),
                    out var error);

                if (!error.IsInvalid)
                {
                    completion.SetException(new GException(error));
                    return;
                }

                list = listHandle.DangerousGetHandle();
                listHandle.SetHandleAsInvalid();
                var snapshots = new List<WebCookieSnapshot>();
                var node = list;
                while (node != IntPtr.Zero)
                {
                    var cookiePointer = Marshal.ReadIntPtr(node);
                    node = Marshal.ReadIntPtr(node, IntPtr.Size);
                    Cookie? cookie = null;
                    var ownershipTransferred = false;
                    try
                    {
                        cookie = (Cookie)BoxedWrapper.WrapHandle(
                            cookiePointer,
                            true,
                            Cookie.GetGType());
                        ownershipTransferred = true;

                        var name = cookie.GetName();
                        var value = cookie.GetValue();
                        var domain = cookie.GetDomain();

                        var path = cookie.GetPath();
                        using var expires = cookie.GetExpires();
                        snapshots.Add(new WebCookieSnapshot(
                            name,
                            value,
                            domain,
                            string.IsNullOrEmpty(path) ? "/" : path,
                            cookie.GetSecure(),
                            cookie.GetHttpOnly(),
                            expires?.ToUnix() ?? 0));
                    }
                    catch
                    {
                        if (!ownershipTransferred && cookiePointer != IntPtr.Zero)
                            FreeCookie(cookiePointer);

                        FreeRemainingCookies(node);
                        throw;
                    }
                    finally
                    {
                        cookie?.Dispose();
                    }
                }

                completion.SetResult(snapshots);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                listHandle?.Dispose();
                if (list != IntPtr.Zero)
                    FreeList(list);
            }
        });

        WebKit.Internal.CookieManager.GetCookies(
            manager.Handle.DangerousGetHandle(),
            NonNullableUtf8StringOwnedHandle.Create(uri),
            IntPtr.Zero,
            callbackHandler.NativeCallback,
            IntPtr.Zero);

        return completion.Task;
    }

    internal static string SerializeNetscape(IEnumerable<WebCookieSnapshot> cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);

        var selected = new Dictionary<(string Domain, string Path, string Name), WebCookieSnapshot>();
        foreach (var cookie in cookies)
        {
            var normalizedDomain = cookie.Domain.ToLowerInvariant();
            if (!IsYouTubeDomain(normalizedDomain))
                continue;

            var path = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path;
            ValidateField(normalizedDomain, nameof(cookie.Domain));
            ValidateField(path, nameof(cookie.Path));
            ValidateField(cookie.Name, nameof(cookie.Name));
            ValidateField(cookie.Value, nameof(cookie.Value));

            selected[(normalizedDomain, path, cookie.Name)] = cookie with
            {
                Domain = normalizedDomain,
                Path = path
            };
        }

        var output = new StringBuilder(NetscapeHeader);
        foreach (var cookie in selected.Values
                     .OrderBy(cookie => cookie.Domain, StringComparer.Ordinal)
                     .ThenBy(cookie => cookie.Path, StringComparer.Ordinal)
                     .ThenBy(cookie => cookie.Name, StringComparer.Ordinal))
        {
            if (cookie.HttpOnly)
                output.Append("#HttpOnly_");

            output.Append(cookie.Domain).Append('\t')
                .Append(cookie.Domain.StartsWith(".", StringComparison.Ordinal) ? "TRUE" : "FALSE").Append('\t')
                .Append(cookie.Path).Append('\t')
                .Append(cookie.Secure ? "TRUE" : "FALSE").Append('\t')
                .Append(cookie.ExpiresUnix > 0 ? cookie.ExpiresUnix : 0).Append('\t')
                .Append(cookie.Name).Append('\t')
                .Append(cookie.Value).Append('\n');
        }

        return output.ToString();
    }

    private static bool IsYouTubeDomain(string domain)
    {
        var host = domain.StartsWith(".", StringComparison.Ordinal) ? domain[1..] : domain;
        return host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateField(string value, string fieldName)
    {
        if (value.IndexOfAny(['\t', '\r', '\n']) >= 0)
            throw new ArgumentException($"Cookie {fieldName} contains a character that cannot be serialized.",
                fieldName);
    }

    private static void FreeRemainingCookies(IntPtr node)
    {
        while (node != IntPtr.Zero)
        {
            var cookiePointer = Marshal.ReadIntPtr(node);
            node = Marshal.ReadIntPtr(node, IntPtr.Size);
            if (cookiePointer != IntPtr.Zero)
                FreeCookie(cookiePointer);
        }
    }

    private static void FreeCookie(IntPtr cookiePointer)
    {
        using var cookieHandle = new CookieUnownedHandle(cookiePointer);
        Soup.Internal.Cookie.Free(cookieHandle);
    }
}