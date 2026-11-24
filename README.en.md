# Y-Apex

A local Windows connection monitor and firewall-rule manager. The UI runs as the signed-in user and asks for a preview and explicit confirmation before changing Y-Apex-owned Windows Firewall rules. The application does not invoke `netsh` or send telemetry.

## Implementation status

This is an early development build, not a release for a primary machine. The .NET 10 solution contains a Windows Service, WPF UI, CLI, experimental WFP layer, and tests. The source includes TCP/UDP monitoring, Y-Apex-owned Windows Firewall rules with previews, confirmation and timed rollback, a change journal, ETW usage accounting since service startup, exports, and a first-connection prompt. **This environment did not build or test the solution on Windows**; verify it in CI and in a VM before use. Some connection directions are unknown, and the UDP table contains local sockets only.

Usage counters are in memory and do not survive a service restart. A real WFP provider, strict mode, and a network update source are not implemented; the current update checker intentionally makes no network request. Test firewall changes only inside a VM with a restore snapshot.

## Build

The full solution requires Windows and the .NET 10 SDK:

```powershell
dotnet restore YApex.sln
dotnet build YApex.sln -c Release --no-restore
dotnet test YApex.sln -c Release --no-build
```

See [`docs/build-local.md`](docs/build-local.md) and [`docs/testing.md`](docs/testing.md). Core tests are cross-platform.

## Privacy and data

- UI settings: `%APPDATA%\YSWEF\Y-Apex`.
- Service journal: `%ProgramData%\YSWEF\Y-Apex` with restricted ACLs.
- No automatic update or telemetry network requests.
- Support links are loaded from `config/shared-links.json`.

## License

MIT. Contact: `170477919+yswef@users.noreply.github.com`.
