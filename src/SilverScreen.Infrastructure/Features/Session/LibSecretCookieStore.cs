using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Session;

internal sealed partial class LibSecretCookieStore : ICookieSecretStore
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string LibGlib = "libglib-2.0.so.0";
    private const string ApplicationAttribute = "application";
    private const string ApplicationValue = "SilverScreen";
    private const string CredentialAttribute = "credential";
    private const string CredentialValue = "youtube-manual-session";
    private const string Label = "SilverScreen YouTube session";
    private const string ContentType = "application/octet-stream";

    private static readonly Lock GlibFunctionsGate = new();
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
        catch (Exception ex)
        {
            throw new SessionPersistenceException(ex);
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
        catch (Exception ex)
        {
            throw new SessionPersistenceException(ex);
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
        catch (Exception ex)
        {
            throw new SessionPersistenceException(ex);
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
                throw new InvalidOperationException("libsecret returned invalid secret data length or pointer.");

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

            if (secretValue == IntPtr.Zero)
                throw new InvalidOperationException("Failed to allocate secret value in libsecret.");

            var stored = SecretPasswordStorevBinarySync(IntPtr.Zero, attributes, IntPtr.Zero, Label, secretValue,
                IntPtr.Zero, out error);
            ThrowIfError(error);
            if (stored == 0) throw new InvalidOperationException("libsecret failed to store the secret.");
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
            if (cleared == 0) throw new InvalidOperationException("libsecret failed to clear the secret.");
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
        if (attributes == IntPtr.Zero)
            throw new InvalidOperationException("Failed to allocate GLib hash table for secret attributes.");

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
        if (duplicatedKey == IntPtr.Zero)
            throw new InvalidOperationException("Failed to allocate attribute key string in GLib.");

        var duplicatedValue = GStrdup(value);
        if (duplicatedValue == IntPtr.Zero)
        {
            GFree(duplicatedKey);
            throw new InvalidOperationException("Failed to allocate attribute value string in GLib.");
        }

        GHashTableInsert(attributes, duplicatedKey, duplicatedValue);
    }

    private static GlibFunctions GetGlibFunctions()
    {
        lock (GlibFunctionsGate)
        {
            if (_sGlibLibrary != IntPtr.Zero) return new GlibFunctions(_sStringHash, _sStringEqual, _sFree);
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

            return new GlibFunctions(_sStringHash, _sStringEqual, _sFree);
        }
    }

    private static void ThrowIfError(IntPtr error)
    {
        if (error == IntPtr.Zero) return;
        var messagePtr = Marshal.ReadIntPtr(error, 8);
        var message = messagePtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(messagePtr) : null;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
            ? "libsecret operation failed."
            : $"libsecret operation failed: {message}");
    }

    [LibraryImport(LibSecret, EntryPoint = "secret_password_lookupv_binary_sync")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr SecretPasswordLookupvBinarySync(IntPtr schema, IntPtr attributes,
        IntPtr cancellable, out IntPtr error);

    [LibraryImport(LibSecret, EntryPoint = "secret_password_storev_binary_sync",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int SecretPasswordStorevBinarySync(IntPtr schema, IntPtr attributes, IntPtr collection,
        string label, IntPtr value, IntPtr cancellable, out IntPtr error);

    [LibraryImport(LibSecret, EntryPoint = "secret_password_clearv_sync")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int SecretPasswordClearvSync(IntPtr schema, IntPtr attributes, IntPtr cancellable,
        out IntPtr error);

    [LibraryImport(LibSecret, EntryPoint = "secret_value_new", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr SecretValueNew(IntPtr text, IntPtr length, string contentType);

    [LibraryImport(LibSecret, EntryPoint = "secret_value_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr SecretValueGet(IntPtr value, out UIntPtr length);

    [LibraryImport(LibSecret, EntryPoint = "secret_value_unref")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SecretValueUnref(IntPtr value);

    [LibraryImport(LibGlib, EntryPoint = "g_hash_table_new_full")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr GHashTableNewFull(IntPtr hashFunc, IntPtr keyEqualFunc, IntPtr keyDestroyFunc,
        IntPtr valueDestroyFunc);

    [LibraryImport(LibGlib, EntryPoint = "g_hash_table_insert")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void GHashTableInsert(IntPtr hashTable, IntPtr key, IntPtr value);

    [LibraryImport(LibGlib, EntryPoint = "g_hash_table_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void GHashTableDestroy(IntPtr hashTable);

    [LibraryImport(LibGlib, EntryPoint = "g_strdup", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial IntPtr GStrdup(string value);

    [LibraryImport(LibGlib, EntryPoint = "g_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void GFree(IntPtr memory);

    [LibraryImport(LibGlib, EntryPoint = "g_error_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void GErrorFree(IntPtr error);

    private readonly record struct GlibFunctions(IntPtr StringHash, IntPtr StringEqual, IntPtr Free);
}