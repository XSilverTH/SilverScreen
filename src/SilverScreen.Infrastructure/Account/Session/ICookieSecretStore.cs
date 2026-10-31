namespace SilverScreen.Infrastructure.Account.Session;

/// <summary>
///     OS-keyring persistence for the manual-session cookie secret. Secrets are stored via
///     the OS keyring (libsecret), never as plaintext config. Buffer discipline: <c>SaveAsync</c>
///     zeroes its input before returning (including on failure); <c>LoadAsync</c> hands ownership
///     of the returned buffer to the caller, which MUST zero it after use.
/// </summary>
internal interface ICookieSecretStore
{
    /// <summary>
    ///     Asynchronously loads the persisted secret. The caller owns the returned buffer and MUST zero it
    ///     after use. Returns <c>null</c> when no secret is stored.
    /// </summary>
    Task<byte[]?> LoadAsync();

    /// <summary>
    ///     Asynchronously persists a copy of <paramref name="secret" />. The input buffer is zeroed before
    ///     return, including on failure.
    /// </summary>
    Task SaveAsync(byte[] secret);

    /// <summary>
    ///     Asynchronously removes the persisted secret from the keyring.
    /// </summary>
    Task DeleteAsync();
}