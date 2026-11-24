// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using YApex.Core.Abstractions;
using YApex.Core.Domain;
using YApex.Core.Security;

namespace YApex.Service.SystemIntegration;

public sealed partial class WindowsProgramIdentitySource : IProgramIdentitySource
{
    private const int MaximumPathCharacters = 32_768;

    public Task<ProgramFileIdentity> InspectAsync(string programPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = WindowsProgramPath.Normalize(programPath);
        return Task.Run(() => Inspect(normalizedPath, cancellationToken), cancellationToken);
    }

    private static ProgramFileIdentity Inspect(string normalizedPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attributes = File.GetAttributes(normalizedPath);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException("The program path is a directory or reparse point and cannot be used for a rule.");
        }

        using var handle = File.OpenHandle(
            normalizedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.SequentialScan);
        var finalPath = GetFinalPath(handle);
        if (!string.Equals(WindowsProgramPath.Normalize(finalPath), normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The program path resolves through a link or to a different file.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var signature = WindowsSignatureVerifier.Inspect(normalizedPath);
        if (signature.State == SignatureState.Unknown || signature.Sha256 is null)
        {
            throw new IOException("The program file could not be read for identity verification.");
        }

        var publisher = signature.Publisher is { Length: > 256 } longPublisher
            ? longPublisher[..256]
            : signature.Publisher;
        return new ProgramFileIdentity(normalizedPath, signature.Sha256, signature.State, publisher);
    }

    private static string GetFinalPath(SafeFileHandle handle)
    {
        var buffer = Marshal.AllocHGlobal(MaximumPathCharacters * sizeof(char));
        try
        {
            var length = GetFinalPathNameByHandle(handle.DangerousGetHandle(), buffer, MaximumPathCharacters, 0);
            if (length == 0 || length >= MaximumPathCharacters)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not resolve the final program path.");
            }

            var path = Marshal.PtrToStringUni(buffer, checked((int)length))
                ?? throw new IOException("Windows returned an empty final program path.");
            if (!path.StartsWith("\\\\?\\", StringComparison.Ordinal))
            {
                throw new IOException("The final program path is not a local DOS path.");
            }

            return path[4..];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(IntPtr fileHandle, IntPtr filePath, uint bufferLength, uint flags);
}
