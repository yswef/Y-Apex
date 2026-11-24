// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Security.AccessControl;
using System.Security.Principal;

namespace YApex.Service.Storage;

public static class ServiceDataPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "YSWEF",
        "Y-Apex");

    public static string DatabasePath => Path.Combine(RootDirectory, "service-data.sqlite3");

    public static string PipeUserSidPath => Path.Combine(RootDirectory, "pipe-user.sid");

    public static void EnsurePrivateDirectory()
    {
        Directory.CreateDirectory(RootDirectory);
        var directory = new DirectoryInfo(RootDirectory);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The shared Y-Apex data directory must not be a symbolic link or reparse point.");
        }

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        directory.SetAccessControl(security);
    }

    private static void AddFullControl(DirectorySecurity security, SecurityIdentifier identity)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }
}
