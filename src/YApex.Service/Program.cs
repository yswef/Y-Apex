// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.ComponentModel;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YApex.Core.Abstractions;
using YApex.Core.Services;
using YApex.Service;
using YApex.Service.Firewall;
using YApex.Service.Ipc;
using YApex.Service.Storage;
using YApex.Service.SystemIntegration;
using YApex.Wfp;

if (args.Length > 0)
{
    try
    {
        switch (args[0])
        {
            case "--install-service" when args.Length == 1:
                WindowsServiceInstaller.InstallOrStart();
                Console.WriteLine("Y-Apex service installed or started successfully.");
                return;
            case "--uninstall-service" when args.Length == 1:
                WindowsServiceInstaller.StopAndDelete();
                Console.WriteLine("Y-Apex service stopped and removed successfully.");
                return;
            case "--remove-owned-rules-confirmed" when args.Length == 1:
                RemoveOwnedRules();
                Console.WriteLine("All Y-Apex-owned firewall rules were removed.");
                return;
            case "--service" when args.Length == 1:
                break;
            default:
                Console.Error.WriteLine("Usage: YApex.Service.exe [--service | --install-service | --uninstall-service | --remove-owned-rules-confirmed]");
                Environment.ExitCode = 2;
                return;
        }
    }
    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or TimeoutException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or IOException)
    {
        Console.Error.WriteLine($"Service operation failed: {exception.Message}");
        Environment.ExitCode = 1;
        return;
    }
}
else if (Environment.UserInteractive)
{
    Console.WriteLine("Use --install-service to install the privileged Y-Apex background service.");
    Environment.ExitCode = 2;
    return;
}

var builder = Host.CreateApplicationBuilder(args.Length == 1 && args[0] == "--service" ? Array.Empty<string>() : args);
var featureOptions = ServiceFeatureOptionsLoader.Load();
builder.Services.AddWindowsService(options => options.ServiceName = WindowsServiceInstaller.ServiceName);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<WindowsFirewallBackend>();
builder.Services.AddSingleton<ExperimentalWfpFirewallBackend>();
builder.Services.AddSingleton<IFirewallBackend>(services => new WfpFallbackFirewallBackend(
    featureOptions.ExperimentalWfpEnabled,
    services.GetRequiredService<ExperimentalWfpFirewallBackend>(),
    services.GetRequiredService<WindowsFirewallBackend>()));
builder.Services.AddSingleton<IConnectionSource, WindowsConnectionSource>();
builder.Services.AddSingleton<IProgramIdentitySource, WindowsProgramIdentitySource>();
builder.Services.AddSingleton<WindowsUsageSource>();
builder.Services.AddSingleton<IUsageSource>(services => services.GetRequiredService<WindowsUsageSource>());
builder.Services.AddHostedService<WindowsUsageSource>(services => services.GetRequiredService<WindowsUsageSource>());
builder.Services.AddSingleton<IChangeJournal, SqliteChangeJournal>();
builder.Services.AddSingleton<PipeAccessPolicy>(_ => PipeAccessPolicy.Load());
builder.Services.AddSingleton<RuleChangeCoordinator>(services => new RuleChangeCoordinator(
    services.GetRequiredService<IFirewallBackend>(),
    services.GetRequiredService<IChangeJournal>(),
    services.GetRequiredService<IClock>(),
    identitySource: services.GetRequiredService<IProgramIdentitySource>()));
builder.Services.AddHostedService<PipeServer>();

await builder.Build().RunAsync();

static void RemoveOwnedRules()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
    {
        throw new UnauthorizedAccessException("Run this cleanup command from an elevated administrator session.");
    }

    using var firewall = new WindowsFirewallBackend();
    firewall.RemoveOwnedRulesAsync(CancellationToken.None).GetAwaiter().GetResult();
}
