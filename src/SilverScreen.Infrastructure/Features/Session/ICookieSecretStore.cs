namespace SilverScreen.Infrastructure.Features.Session;

internal interface ICookieSecretStore
{
    byte[]? Load();

    void Save(byte[] secret);

    void Delete();
}