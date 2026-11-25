// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Security;
using Xunit;

namespace YApex.Core.Tests;

public sealed class Ed25519Tests
{
    [Fact]
    public void VerifiesRfc8032EmptyMessageVector()
    {
        var publicKey = Convert.FromHexString("D75A980182B10AB7D54BFED3C964073A0EE172F3DAA62325AF021A68F707511A");
        var signature = Convert.FromHexString("E5564300C360AC729086E2CC806E828A84877F1EB8E5D974D873E06522490155" +
                                              "5FB8821590A33BACC61E39701CF9B46BD25BF5F0595BBE24655141438E7A100B");

        Assert.True(Ed25519SignatureVerifier.Verify(ReadOnlySpan<byte>.Empty, signature, publicKey));
        signature[0] ^= 1;
        Assert.False(Ed25519SignatureVerifier.Verify(ReadOnlySpan<byte>.Empty, signature, publicKey));
    }
}
