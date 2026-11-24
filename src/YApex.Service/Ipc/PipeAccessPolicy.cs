// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using YApex.Service.Storage;

namespace YApex.Service.Ipc;

public sealed partial class PipeAccessPolicy
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private readonly object _gate = new();
    private HashSet<string> _allowedSids = new(StringComparer.OrdinalIgnoreCase);

    private PipeAccessPolicy()
    {
        ServiceDataPaths.EnsurePrivateDirectory();
        Refresh();
    }

    public static PipeAccessPolicy Load() => new();

    public PipeSecurity CreatePipeSecurity()
    {
        Refresh();
        string[] allowed;
        lock (_gate)
        {
            allowed = _allowedSids.ToArray();
        }

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sidValue in allowed)
        {
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(sidValue),
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                AccessControlType.Allow));
        }

        return security;
    }

    public bool IsAllowed(SecurityIdentifier? sid)
    {
        if (sid is null)
        {
            return false;
        }

        Refresh();
        lock (_gate)
        {
            return _allowedSids.Contains(sid.Value);
        }
    }

    public static void RegisterCurrentUser()
    {
        EnsureAdministrator();
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User ?? throw new InvalidOperationException("The current Windows user has no SID.");
        WriteRegisteredSid(userSid);
    }

    // Setup may be elevated with separate administrator credentials; enroll the signed-in desktop account instead.
    public static void RegisterInteractiveUser()
    {
        EnsureAdministrator();
        int currentSessionId;
        using (var currentProcess = Process.GetCurrentProcess())
        {
            currentSessionId = currentProcess.SessionId;
        }

        SecurityIdentifier? interactiveUserSid = null;
        var explorerProcesses = Process.GetProcessesByName("explorer");
        try
        {
            foreach (var process in explorerProcesses)
            {
                try
                {
                    if (process.SessionId == currentSessionId)
                    {
                        interactiveUserSid = TryGetProcessUserSid(process.Id);
                        if (interactiveUserSid is not null)
                        {
                            break;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    continue;
                }
            }
        }
        finally
        {
            foreach (var process in explorerProcesses)
            {
                process.Dispose();
            }
        }

        if (interactiveUserSid is null)
        {
            throw new InvalidOperationException("The interactive user SID could not be resolved. Sign in to the Windows desktop and run setup again.");
        }

        WriteRegisteredSid(interactiveUserSid);
    }

    private static void EnsureAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException("Run this setup command from an elevated installer or administrator session.");
        }
    }

    private static SecurityIdentifier? TryGetProcessUserSid(int processId)
    {
        using var processHandle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
        if (processHandle.IsInvalid)
        {
            return null;
        }

        if (!OpenProcessToken(processHandle, TokenQuery, out var tokenHandle))
        {
            tokenHandle?.Dispose();
            return null;
        }

        using (tokenHandle)
        using (var identity = new WindowsIdentity(tokenHandle.DangerousGetHandle()))
        {
            return identity.User;
        }
    }

    private static void WriteRegisteredSid(SecurityIdentifier userSid)
    {
        ServiceDataPaths.EnsurePrivateDirectory();
        var temporaryPath = ServiceDataPaths.PipeUserSidPath + ".new";
        if ((File.Exists(temporaryPath) && (File.GetAttributes(temporaryPath) & FileAttributes.ReparsePoint) != 0) ||
            (File.Exists(ServiceDataPaths.PipeUserSidPath) && (File.GetAttributes(ServiceDataPaths.PipeUserSidPath) & FileAttributes.ReparsePoint) != 0))
        {
            throw new IOException("A pipe SID file must not be a symbolic link or reparse point.");
        }

        File.WriteAllText(temporaryPath, userSid.Value);
        File.Move(temporaryPath, ServiceDataPaths.PipeUserSidPath, overwrite: true);
    }

    private void Refresh()
    {
        var directory = new DirectoryInfo(ServiceDataPaths.RootDirectory);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The shared Y-Apex data directory must not be a symbolic link or reparse point.");
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value
        };
        if (File.Exists(ServiceDataPaths.PipeUserSidPath))
        {
            var info = new FileInfo(ServiceDataPaths.PipeUserSidPath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length is <= 0 or > 256)
            {
                throw new IOException("The named-pipe user SID file is a reparse point or has an invalid size.");
            }

            var sid = new SecurityIdentifier(File.ReadAllText(ServiceDataPaths.PipeUserSidPath).Trim());
            allowed.Add(sid.Value);
        }

        lock (_gate)
        {
            _allowedSids = allowed;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(
        SafeProcessHandle processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);
}
