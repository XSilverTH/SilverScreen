using SilverScreen.Core.Common;

namespace SilverScreen.Tests.Common;

public sealed class DiagnosticSanitizerTests
{
    [Theory]
    [InlineData("Authorization: Bearer secret-token", "secret-token")]
    [InlineData("Cookie: SAPISID=secret-cookie; SID=another-secret", "secret-cookie")]
    [InlineData("SAPISIDHASH 123_secret-hash", "123_secret-hash")]
    [InlineData("SAPISIDHASH=secret-hash", "secret-hash")]
    [InlineData("https://example.test/?continuation=secret-continuation&key=secret-key", "secret-continuation")]
    [InlineData("{\"access_token\":\"secret-access-token\"}", "secret-access-token")]
    [InlineData("--cookies /tmp/cookies.txt --token=secret-token", "secret-token")]
    public void Sanitize_RemovesSecretMaterial(string input, string secret)
    {
        var sanitized = DiagnosticSanitizer.Sanitize(input);

        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_PreservesNonSensitiveDiagnostics()
    {
        const string message = "ERROR: [youtube] format not found";

        Assert.Equal(message, DiagnosticSanitizer.Sanitize(message));
    }
}
