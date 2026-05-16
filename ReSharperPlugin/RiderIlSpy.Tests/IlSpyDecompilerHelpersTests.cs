using Xunit;

namespace RiderIlSpy.Tests;

/// <summary>
/// Tests for SDK-free helpers reachable through
/// <see cref="IlSpyExternalSourcesProviderHelpers"/>. The TFM-bug-detection
/// tests previously lived here but moved to
/// <see cref="DecompilerTypeSystemPatchTests"/> alongside the extracted
/// <see cref="DecompilerTypeSystemPatch"/> they now exercise.
/// </summary>
public class IlSpyDecompilerHelpersTests
{
    [Fact]
    public void ComputePublicKeyToken_matches_mscorlib_known_token()
    {
        byte[] mscorlibPublicKey = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        string token = IlSpyExternalSourcesProviderHelpers.ComputePublicKeyToken(mscorlibPublicKey);
        Assert.Equal(16, token.Length);
        foreach (char c in token)
        {
            Assert.True(char.IsDigit(c) || (c >= 'a' && c <= 'f'), "token must be lowercase hex");
        }
    }

    [Fact]
    public void ComputePublicKeyToken_is_deterministic()
    {
        byte[] key = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        Assert.Equal(IlSpyExternalSourcesProviderHelpers.ComputePublicKeyToken(key), IlSpyExternalSourcesProviderHelpers.ComputePublicKeyToken(key));
    }

    [Fact]
    public void ComputePublicKeyToken_differs_for_different_keys()
    {
        byte[] keyA = new byte[] { 0x01, 0x02, 0x03 };
        byte[] keyB = new byte[] { 0x01, 0x02, 0x04 };
        Assert.NotEqual(IlSpyExternalSourcesProviderHelpers.ComputePublicKeyToken(keyA), IlSpyExternalSourcesProviderHelpers.ComputePublicKeyToken(keyB));
    }
}
