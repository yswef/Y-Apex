// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace YApex.Service;

public static partial class WindowsServiceInstaller
{
    public const string ServiceName = "YApexService";
    private const string DisplayName = "Y-Apex Service";
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerCreateService = 0x0002;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint Delete = 0x00010000;
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceAutoStart = 0x00000002;
    private const uint ServiceErrorNormal = 0x00000001;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceRunning = 0x00000004;
    private const int ErrorServiceExists = 1073;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceNotActive = 1062;
    private static readonly TimeSpan ServiceOperationTimeout = TimeSpan.FromSeconds(30);

    public static void InstallOrStart()
    {
        using var manager = OpenSCManager(null, null, ScManagerConnect | ScManagerCreateService);
        ThrowIfInvalid(manager, "Windows Service Control Manager could not be opened.");

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("The service executable path could not be resolved.");
        }

        var binaryPath = $"\"{Path.GetFullPath(executablePath)}\"";
        using var service = CreateService(
            manager,
            ServiceName,
            DisplayName,
            ServiceQueryStatus | ServiceStart | ServiceStop | Delete,
            ServiceWin32OwnProcess,
            ServiceAutoStart,
            ServiceErrorNormal,
            binaryPath,
            null,
            IntPtr.Zero,
            null,
            null,
            null);

        if (service.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorServiceExists)
            {
                throw new Win32Exception(error, "Y-Apex service could not be installed.");
            }

            using var existing = OpenService(manager, ServiceName, ServiceQueryStatus | ServiceStart | ServiceStop | Delete);
            ThrowIfInvalid(existing, "The existing Y-Apex service could not be opened.");
            StartAndWait(existing);
            return;
        }

        StartAndWait(service);
    }

    public static void StopAndDelete()
    {
        using var manager = OpenSCManager(null, null, ScManagerConnect);
        ThrowIfInvalid(manager, "Windows Service Control Manager could not be opened.");
        using var service = OpenService(manager, ServiceName, ServiceQueryStatus | ServiceStop | Delete);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorServiceDoesNotExist)
            {
                return;
            }

            throw new Win32Exception(error, "The Y-Apex service could not be opened for removal.");
        }

        if (QueryStatus(service).CurrentState != ServiceStopped)
        {
            if (!ControlService(service, ServiceControlStop, out _) && Marshal.GetLastWin32Error() != ErrorServiceNotActive)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The Y-Apex service could not be stopped.");
            }

            WaitForState(service, ServiceStopped);
        }

        if (!DeleteService(service))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The Y-Apex service could not be removed.");
        }
    }

    private static void StartAndWait(SafeServiceHandle service)
    {
        if (!StartService(service, 0, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorServiceAlreadyRunning)
            {
                throw new Win32Exception(error, "The Y-Apex service could not be started.");
            }
        }

        WaitForState(service, ServiceRunning);
    }

    private static void WaitForState(SafeServiceHandle service, uint expectedState)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ServiceOperationTimeout)
        {
            var status = QueryStatus(service);
            if (status.CurrentState == expectedState)
            {
                return;
            }

            if (status.CurrentState is ServiceStopped && expectedState == ServiceRunning)
            {
                throw new InvalidOperationException("The Y-Apex service stopped before it became ready.");
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException("The Y-Apex service did not reach the expected state in time.");
    }

    private static ServiceStatus QueryStatus(SafeServiceHandle service)
    {
        if (!QueryServiceStatus(service, out var status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The Y-Apex service status could not be read.");
        }

        return status;
    }

    private static void ThrowIfInvalid(SafeServiceHandle handle, string message)
    {
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), message);
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "CreateServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeServiceHandle CreateService(
        SafeServiceHandle serviceManager,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeServiceHandle OpenService(SafeServiceHandle serviceManager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartService(SafeServiceHandle service, uint argumentCount, IntPtr arguments);

    [LibraryImport("advapi32.dll", EntryPoint = "ControlService", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ControlService(SafeServiceHandle service, uint control, out ServiceStatus status);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceStatus", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(SafeServiceHandle service, out ServiceStatus status);

    [LibraryImport("advapi32.dll", EntryPoint = "DeleteService", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(SafeServiceHandle service);

    [LibraryImport("advapi32.dll", EntryPoint = "CloseServiceHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeServiceHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
}
