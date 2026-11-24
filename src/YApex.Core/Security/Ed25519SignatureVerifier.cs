// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace YApex.Core.Security;

public static class Ed25519SignatureVerifier
{
    private const int PublicKeyLength = 32;
    private const int SignatureLength = 64;
    private const int MaximumMessageLength = 1024 * 1024;

    public static bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
    {
        if (message.Length > MaximumMessageLength || signature.Length != SignatureLength || publicKey.Length != PublicKeyLength)
        {
            return false;
        }

        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey.ToArray(), 0));
            var bytes = message.ToArray();
            verifier.BlockUpdate(bytes, 0, bytes.Length);
            return verifier.VerifySignature(signature.ToArray());
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
