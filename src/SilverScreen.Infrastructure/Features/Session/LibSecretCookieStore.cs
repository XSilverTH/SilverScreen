using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Session;

internal sealed class LibSecretCookieStore : ICookieSecretStore
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string LibGlib = "libglib-2.0.so.0";
    private const string ApplicationAttribute = "application";
    private const string ApplicationValue = "SilverScreen";
    private const string CredentialAttribute = "credential";
    private const string CredentialValue = "youtube-manual-session";
    private const string Label = "SilverScreen YouTube session";
    private const string ContentType = "application/octet-stream";

    private static readonly object GlibFunctionsGate = new();
    private static IntPtr _sGlibLibrary;
    private static IntPtr _sStringHash;
    private static IntPtr _sStringEqual;
    private static IntPtr _sFree;

    public byte[]? Load()
    {
        try
        {
            return LoadNative();
        }
        catch (SessionPersistenceException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new SessionPersistenceException();
        }
    }

    public void Save(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        try
        {
            SaveNative(secret);
        }
        catch (SessionPersistenceException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new SessionPersistenceException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public void Delete()
    {
        try
        {
            DeleteNative();
        }
        catch (SessionPersistenceException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new SessionPersistenceException();
        }
    }

    private static byte[]? LoadNative()
    {
        var attributes = IntPtr.Zero;
        var secretValue = IntPtr.Zero;
        var error = IntPtr.Zero;
        byte[]? secret = null;
        try
        {
            attributes = CreateAttributes();
            secretValue = SecretPasswordLookupvBinarySync(IntPtr.Zero, attributes, IntPtr.Zero, out error);
            ThrowIfError(error);
            if (secretValue == IntPtr.Zero) return null;

            var valuePointer = SecretValueGet(secretValue, out var length);
            var byteCount = length.ToUInt64();
            if (byteCount > int.MaxValue || (byteCount > 0 && valuePointer == IntPtr.Zero))
                throw new InvalidOperationException();

            secret = new byte[(int)byteCount];
            if (secret.Length > 0) Marshal.Copy(valuePointer, secret, 0, secret.Length);

            return secret;
        }
        catch
        {
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);

            throw;
        }

        finally
        {
            if (secretValue != IntPtr.Zero) SecretValueUnref(secretValue);

            if (attributes != IntPtr.Zero) GHashTableDestroy(attributes);

            if (error != IntPtr.Zero) GErrorFree(error);
        }
    }

    private static void SaveNative(byte[] secret)
    {
        var attributes = IntPtr.Zero;
        var secretValue = IntPtr.Zero;
        var error = IntPtr.Zero;
        GCHandle secretHandle = default;
        try
        {
            attributes = CreateAttributes();
            secretHandle = GCHandle.Alloc(secret, GCHandleType.Pinned);
            try
            {
                secretValue = SecretValueNew(secretHandle.AddrOfPinnedObject(), secret.Length, ContentType);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }

            if (secretValue == IntPtr.Zero) throw new InvalidOperationException();

            var stored = SecretPasswordStorevBinarySync(IntPtr.Zero, attributes, IntPtr.Zero, Label, secretValue,
                IntPtr.Zero, out error);
            ThrowIfError(error);
            if (stored == 0) throw new InvalidOperationException();
        }
        finally
        {
            if (secretHandle.IsAllocated) secretHandle.Free();

            if (secretValue != IntPtr.Zero) SecretValueUnref(secretValue);

            if (attributes != IntPtr.Zero) GHashTableDestroy(attributes);

            if (error != IntPtr.Zero) GErrorFree(error);
        }
    }

    private static void DeleteNative()
    {
        var attributes = IntPtr.Zero;
        var error = IntPtr.Zero;
        try
        {
            attributes = CreateAttributes();
            var cleared = SecretPasswordClearvSync(IntPtr.Zero, attributes, IntPtr.Zero, out error);
            ThrowIfError(error);
            if (cleared == 0) throw new InvalidOperationException();
        }
        finally
        {
            if (attributes != IntPtr.Zero) GHashTableDestroy(attributes);

            if (error != IntPtr.Zero) GErrorFree(error);
        }
    }

    private static IntPtr CreateAttributes()
    {
        var functions = GetGlibFunctions();
        var attributes = GHashTableNewFull(functions.StringHash, functions.StringEqual, functions.Free, functions.Free);
        if (attributes == IntPtr.Zero) throw new InvalidOperationException();

        try
        {
            InsertAttribute(attributes, ApplicationAttribute, ApplicationValue);
            InsertAttribute(attributes, CredentialAttribute, CredentialValue);
            return attributes;
        }
        catch
        {
            GHashTableDestroy(attributes);
            throw;
        }
    }

    private static void InsertAttribute(IntPtr attributes, string key, string value)
    {
        var duplicatedKey = GStrdup(key);
        if (duplicatedKey == IntPtr.Zero) throw new InvalidOperationException();

        var duplicatedValue = GStrdup(value);
        if (duplicatedValue == IntPtr.Zero)
        {
            GFree(duplicatedKey);
            throw new InvalidOperationException();
        }

        GHashTableInsert(attributes, duplicatedKey, duplicatedValue);
    }

    private static GlibFunctions GetGlibFunctions()
    {
        lock (GlibFunctionsGate)
        {
            if (_sGlibLibrary == IntPtr.Zero)
            {
                var library = NativeLibrary.Load(LibGlib);
                try
                {
                    _sStringHash = NativeLibrary.GetExport(library, "g_str_hash");
                    _sStringEqual = NativeLibrary.GetExport(library, "g_str_equal");
                    _sFree = NativeLibrary.GetExport(library, "g_free");
                    _sGlibLibrary = library;
                }
                catch
                {
                    NativeLibrary.Free(library);
                    throw;
                }
            }

            return new GlibFunctions(_sStringHash, _sStringEqual, _sFree);
        }
    }

    private static void ThrowIfError(IntPtr error)
    {
        if (error != IntPtr.Zero) throw new InvalidOperationException();
    }

    [DllImport(LibSecret, EntryPoint = "secret_password_lookupv_binary_sync",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SecretPasswordLookupvBinarySync(IntPtr schema, IntPtr attributes, IntPtr cancellable,
        out IntPtr error);

    [DllImport(LibSecret, EntryPoint = "secret_password_storev_binary_sync",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int SecretPasswordStorevBinarySync(IntPtr schema, IntPtr attributes, IntPtr collection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label, IntPtr value, IntPtr cancellable, out IntPtr error);

    [DllImport(LibSecret, EntryPoint = "secret_password_clearv_sync", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SecretPasswordClearvSync(IntPtr schema, IntPtr attributes, IntPtr cancellable,
        out IntPtr error);

    [DllImport(LibSecret, EntryPoint = "secret_value_new", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SecretValueNew(IntPtr text, IntPtr length,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string contentType);

    [DllImport(LibSecret, EntryPoint = "secret_value_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SecretValueGet(IntPtr value, out UIntPtr length);

    [DllImport(LibSecret, EntryPoint = "secret_value_unref", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SecretValueUnref(IntPtr value);

    [DllImport(LibGlib, EntryPoint = "g_hash_table_new_full", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GHashTableNewFull(IntPtr hashFunc, IntPtr keyEqualFunc, IntPtr keyDestroyFunc,
        IntPtr valueDestroyFunc);

    [DllImport(LibGlib, EntryPoint = "g_hash_table_insert", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GHashTableInsert(IntPtr hashTable, IntPtr key, IntPtr value);

    [DllImport(LibGlib, EntryPoint = "g_hash_table_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GHashTableDestroy(IntPtr hashTable);

    [DllImport(LibGlib, EntryPoint = "g_strdup", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GStrdup([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(LibGlib, EntryPoint = "g_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GFree(IntPtr memory);

    [DllImport(LibGlib, EntryPoint = "g_error_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void GErrorFree(IntPtr error);

    private readonly record struct GlibFunctions(IntPtr StringHash, IntPtr StringEqual, IntPtr Free);
}