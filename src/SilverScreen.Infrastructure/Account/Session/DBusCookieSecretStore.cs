using System.Security.Cryptography;
using DBus.Services.Secrets;
using Serilog;
using SilverScreen.Core.Account.Session;

namespace SilverScreen.Infrastructure.Account.Session;

/// <summary>
///     Implements <see cref="ICookieSecretStore" /> using the Freedesktop Secret Service API via
///     managed D-Bus bindings (<see cref="SecretService" />).
/// </summary>
internal sealed class DBusCookieSecretStore : ICookieSecretStore
{
    private const string ApplicationAttribute = "application";
    private const string ApplicationValue = "SilverScreen";
    private const string CredentialAttribute = "credential";
    private const string CredentialValue = "youtube-manual-session";
    private const string Label = "SilverScreen YouTube session";
    private const string ContentType = "application/octet-stream";
    private const string DefaultCollectionAlias = "default";

    private static readonly ILogger Logger = Log.ForContext<DBusCookieSecretStore>();
    private readonly Func<Task<SecretService>> _secretServiceFactory;

    public DBusCookieSecretStore()
        : this(() => SecretService.ConnectAsync(EncryptionType.Dh))
    {
    }

    internal DBusCookieSecretStore(Func<Task<SecretService>> secretServiceFactory)
    {
        _secretServiceFactory = secretServiceFactory ?? throw new ArgumentNullException(nameof(secretServiceFactory));
    }

    public async Task<byte[]?> LoadAsync()
    {
        byte[]? secret = null;
        try
        {
            var secretService = await ConnectSecretServiceAsync().ConfigureAwait(false);
            var lookupAttributes = CreateLookupAttributes();
            var (unlocked, locked) = await secretService.SearchItemsAsync(lookupAttributes).ConfigureAwait(false);

            Item? targetItem = null;
            if (unlocked.Length > 0)
            {
                targetItem = await SelectNewestItemAsync(unlocked).ConfigureAwait(false);
            }
            else if (locked.Length > 0)
            {
                targetItem = await SelectNewestItemAsync(locked).ConfigureAwait(false);
                if (await targetItem.IsLockedAsync().ConfigureAwait(false))
                {
                    await targetItem.UnlockAsync().ConfigureAwait(false);
                    if (await targetItem.IsLockedAsync().ConfigureAwait(false))
                    {
                        throw new SessionPersistenceException("The Secret Service item is locked and could not be unlocked.");
                    }
                }
            }

            if (targetItem is null)
            {
                return null;
            }

            secret = await targetItem.GetSecretAsync().ConfigureAwait(false);
            return secret;
        }
        catch (SessionPersistenceException)
        {
            if (secret is not null)
            {
                CryptographicOperations.ZeroMemory(secret);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (secret is not null)
            {
                CryptographicOperations.ZeroMemory(secret);
            }

            throw new SessionPersistenceException(ex);
        }
    }

    public async Task SaveAsync(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        try
        {
            await SaveInternalAsync(secret).ConfigureAwait(false);
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

    public async Task DeleteAsync()
    {
        try
        {
            var secretService = await ConnectSecretServiceAsync().ConfigureAwait(false);
            var lookupAttributes = CreateLookupAttributes();
            var (unlocked, locked) = await secretService.SearchItemsAsync(lookupAttributes).ConfigureAwait(false);

            foreach (var item in unlocked.Concat(locked))
            {
                await item.DeleteAsync().ConfigureAwait(false);
            }
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

    private async Task SaveInternalAsync(byte[] secret)
    {
        var secretService = await ConnectSecretServiceAsync().ConfigureAwait(false);
        var collection = await GetOrCreateCollectionAsync(secretService).ConfigureAwait(false);
        var lookupAttributes = CreateLookupAttributes();

        var item = await collection.CreateItemAsync(Label, lookupAttributes, secret, ContentType, true).ConfigureAwait(false);
        if (item is null)
        {
            throw new SessionPersistenceException("Failed to store secret item in Secret Service (prompt dismissed or rejected).");
        }
    }

    private async Task<SecretService> ConnectSecretServiceAsync()
    {
        try
        {
            return await _secretServiceFactory().ConfigureAwait(false);
        }
        catch (SessionPersistenceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SessionPersistenceException("Failed to connect to the Secret Service over D-Bus.", ex);
        }
    }

    private static async Task<Collection> GetOrCreateCollectionAsync(SecretService secretService)
    {
        Collection? collection = null;

        try
        {
            collection = await secretService.GetDefaultCollectionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to retrieve default Secret Service collection by alias");
        }

        if (collection is null)
        {
            try
            {
                var collections = await secretService.GetAllCollectionsAsync().ConfigureAwait(false);
                if (collections.Length > 0)
                {
                    collection = collections[0];
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to retrieve all Secret Service collections");
            }
        }

        if (collection is null)
        {
            collection = await secretService.CreateCollectionAsync("Default", DefaultCollectionAlias).ConfigureAwait(false);
        }

        if (collection is null)
        {
            throw new SessionPersistenceException("No Secret Service collection is available and default collection could not be created.");
        }

        if (await collection.IsLockedAsync().ConfigureAwait(false))
        {
            await collection.UnlockAsync().ConfigureAwait(false);
            if (await collection.IsLockedAsync().ConfigureAwait(false))
            {
                throw new SessionPersistenceException("The Secret Service collection is locked and could not be unlocked.");
            }
        }

        return collection;
    }

    private static async Task<Item> SelectNewestItemAsync(Item[] items)
    {
        if (items.Length == 1)
        {
            return items[0];
        }

        var newest = items[0];
        var newestTime = DateTimeOffset.MinValue;
        try
        {
            newestTime = await newest.GetModifiedAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore failure to query modification timestamp
        }

        for (var i = 1; i < items.Length; i++)
        {
            try
            {
                var mod = await items[i].GetModifiedAsync().ConfigureAwait(false);
                if (mod > newestTime)
                {
                    newest = items[i];
                    newestTime = mod;
                }
            }
            catch
            {
                // Ignore failure to query modification timestamp
            }
        }

        return newest;
    }

    private static Dictionary<string, string> CreateLookupAttributes()
    {
        return new Dictionary<string, string>
        {
            { ApplicationAttribute, ApplicationValue },
            { CredentialAttribute, CredentialValue }
        };
    }
}
