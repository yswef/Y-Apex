# Y-Apex

A local Windows connection monitor and firewall-rule manager. The UI runs as the signed-in user and asks for a preview and explicit confirmation before changing Y-Apex-owned Windows Firewall rules. The application does not invoke `netsh` or send telemetry.

## Implementation status

This is an early development build, not a public release or suitable for a primary machine. There is no public installer, and installation plus firewall changes have not yet been exercised manually in an interactive Windows VM. The .NET 10 solution contains a Windows Service, WPF UI, CLI, an unimplemented experimental WFP layer, and tests. The source provides TCP/UDP connection snapshots, Y-Apex-owned Windows Firewall rules with preview, explicit confirmation and timed rollback, a change journal, ETW usage counters from service start, exports, and a first-connection prompt.

Blocking creates inbound and outbound block rules; Allow adds an outbound rule only and does not open inbound access. Windows Firewall rules match the file path, not its hash or publisher. The IP Helper snapshot does not determine connection direction reliably, and UDP entries show local sockets only. Usage counters are in memory and reset when the service restarts. A real WFP provider, strict mode, and network update source are not implemented; the update checker makes no network request. Test firewall changes only inside a Windows VM with a restore snapshot.

## Build

The full solution requires Windows and the .NET 10 SDK:

```powershell
dotnet restore YApex.sln
dotnet build YApex.sln -c Release --no-restore
dotnet test YApex.sln -c Release --no-build
```

See [`docs/build-local.md`](docs/build-local.md), [`docs/testing.md`](docs/testing.md), [`docs/privacy.md`](docs/privacy.md), and [`docs/comparison.md`](docs/comparison.md). The bilingual static documentation site is in [`site/`](site/). Core tests are cross-platform.

## Privacy and data

- UI settings: `%APPDATA%\YSWEF\Y-Apex`.
- Service journal: `%ProgramData%\YSWEF\Y-Apex` with restricted ACLs.
- No automatic update or telemetry network requests.
- Support links are loaded from `config/shared-links.json`.

## License

MIT. Contact: `170477919+yswef@users.noreply.github.com`.
