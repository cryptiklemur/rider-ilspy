using System;
using Xunit;

namespace RiderIlSpy.Tests;

public class IlSpyDecompilerHelpersTests
{
    [Fact]
    public void IsTwoComponentTfmVersionBug_returns_false_when_paramName_is_not_fieldCount()
    {
        ArgumentException ex = new ArgumentException("not the bug", "otherParam");
        Assert.False(IlSpyDecompiler.IsTwoComponentTfmVersionBug(ex));
    }

    [Fact]
    public void IsTwoComponentTfmVersionBug_returns_false_for_unrelated_argument_exception_with_fieldCount()
    {
        ArgumentException ex;
        try
        {
            new Version(1, 0).ToString(99);
            throw new InvalidOperationException("expected ToString(99) to throw");
        }
        catch (ArgumentException caught)
        {
            ex = caught;
        }
        Assert.Equal("fieldCount", ex.ParamName);
        Assert.False(IlSpyDecompiler.IsTwoComponentTfmVersionBug(ex));
    }

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
