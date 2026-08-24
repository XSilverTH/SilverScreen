namespace SilverScreen.Infrastructure.Account.Session;

internal interface ICookieSecretStore
{
    byte[]? Load();

    void Save(byte[] secret);

    void Delete();
}