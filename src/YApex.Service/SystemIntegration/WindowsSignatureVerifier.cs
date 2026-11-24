// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using YApex.Core.Domain;

namespace YApex.Service.SystemIntegration;

public sealed record FileSignatureResult(SignatureState State, string? Publisher, string? Sha256)
{
    public bool IsValid => State == SignatureState.Valid;
}

public static partial class WindowsSignatureVerifier
{
    private const int NoUi = 2;
    private const int NoRevocationCheck = 0;
    private const int FileChoice = 1;
    private const int VerifyState = 1;
    private const int CloseState = 2;
    private const uint CacheOnlyUrlRetrieval = 0x00001000;
    private const uint NoSignatureStatus = 0x800B0100;
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static FileSignatureResult Inspect(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var status = VerifySignature(path);
            var signature = status switch
            {
                0 => SignatureState.Valid,
                _ when unchecked((uint)status) == NoSignatureStatus => SignatureState.Unsigned,
                _ => SignatureState.Invalid
            };
            string? publisher = null;
            if (signature == SignatureState.Valid)
            {
                using var certificate = X509Certificate.CreateFromSignedFile(path);
                publisher = certificate.Subject;
            }

            return new FileSignatureResult(signature, publisher, hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or Win32Exception or ArgumentException)
        {
            return new FileSignatureResult(SignatureState.Unknown, null, null);
        }
    }

    private static int VerifySignature(string path)
    {
        var pathPointer = Marshal.StringToCoTaskMemUni(path);
        var fileInfo = new WinTrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = pathPointer
        };
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
        var trustData = new WinTrustData
        {
            Size = (uint)Marshal.SizeOf<WinTrustData>(),
            UiChoice = NoUi,
            RevocationChecks = NoRevocationCheck,
            UnionChoice = FileChoice,
            FileInfo = fileInfoPointer,
            StateAction = VerifyState,
            ProviderFlags = CacheOnlyUrlRetrieval
        };

        try
        {
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
            trustData.StateAction = CloseState;
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);
            return result;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    [LibraryImport("wintrust.dll", EntryPoint = "WinVerifyTrust")]
    private static partial int WinVerifyTrust(IntPtr windowHandle, ref Guid actionId, ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public int UiChoice;
        public int RevocationChecks;
        public int UnionChoice;
        public IntPtr FileInfo;
        public int StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public int UiContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }
}
