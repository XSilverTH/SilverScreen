namespace SilverScreen.Infrastructure.Account.Session;

/// <summary>
/// OS-keyring persistence for the manual-session cookie secret. Secrets are stored via
/// the OS keyring (libsecret), never as plaintext config. Buffer discipline: <c>Save</c>
/// zeroes its input before returning (including on failure); <c>Load</c> hands ownership
/// of the returned buffer to the caller, which MUST zero it after use.
/// </summary>
internal interface ICookieSecretStore
{
    /// <summary>
    /// Loads the persisted secret. The caller owns the returned buffer and MUST zero it
    /// after use. Returns <c>null</c> when no secret is stored.
    /// </summary>
    byte[]? Load();

    /// <summary>
    /// Persists a copy of <paramref name="secret"/>. The input buffer is zeroed before
    /// return, including on failure.
    /// </summary>
    void Save(byte[] secret);

    /// <summary>
    /// Removes the persisted secret from the keyring.
    /// </summary>
    void Delete();
}