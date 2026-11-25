# Y-Apex privacy notice

**Reviewed:** 2026-11-22
**Applies to:** the current development source and installer configuration. This is not a statement that Y-Apex has been released or independently audited.

## What the current build processes

Y-Apex is designed to process connection metadata locally. The Windows service reads the current TCP and UDP socket tables and associates entries with process IDs and, when Windows permits, process names, file paths, hashes, and Authenticode signer information. The current API cannot reliably determine a connection's direction; UDP rows contain local sockets only. Y-Apex does not capture packet contents.

The service also counts local TCP/IP ETW bytes by process. Those counters are held in memory from service start and reset when the service stops; they are not a durable traffic-history database. The change journal records Y-Apex firewall-rule changes, including program paths and before/after rule snapshots. The SQLite journal payloads are protected with Windows DPAPI in LocalMachine scope and stored under `%ProgramData%\YSWEF\Y-Apex`, whose ACL is restricted to LocalSystem and Administrators. Local administrators can still inspect, alter, or delete local data.

The UI stores its language, theme, and bounded hashes of paths already shown in the first-connection prompt under `%APPDATA%\YSWEF\Y-Apex`. It does not store those full paths in that prompt-history setting. The service SID enrollment file is stored under the protected ProgramData directory.

## Network activity and third parties

The current source contains no telemetry sender, automatic updater, or configured network update source. The update-check action returns a local message and makes no network request. Restoring NuGet packages during development is a build-time action, not an application runtime request. External support and documentation links are copied or visited only after a user action.

The published documentation site is static and loads no third-party fonts, images, analytics, or advertising scripts. It stores the selected language in browser `localStorage`. GitHub Pages may process ordinary web-request metadata under GitHub's policies, and opening an external source link takes the browser to that third party.

## Exports and uninstall

Connection and journal exports may contain process names, local paths, IP addresses, ports, and timestamps. The app asks before exporting; the user chooses the destination. Treat exported files as sensitive and share them only when appropriate.

The interactive uninstaller asks whether to remove Y-Apex-managed firewall rules. Answering **Yes** removes only rules carrying Y-Apex's ownership metadata and then removes the shared service data directory. Answering **No** stops/removes the service but leaves its rules and shared data for manual cleanup. A silent uninstall defaults to **No** and preserves managed rules and service data. The current user's UI settings are removed by the installer configuration.

## User choices and limitations

Firewall-rule changes are not automatic: the UI previews and requests confirmation, and an unconfirmed change has a 30-second rollback window. Blocking creates inbound and outbound block rules. Allowing creates an outbound allow rule only; it does not add an inbound allow rule. Windows Firewall matches the executable path, not the displayed hash or publisher identity.

Y-Apex is not an antivirus, packet-content inspector, VPN, DNS privacy resolver, or tracker-filtering product. The custom WFP provider, strict mode, persistent usage history, and automatic update source are not implemented. Do not use the current development build on a primary machine; test firewall changes only in a Windows VM with a restore snapshot.

## Contact and changes

For questions or corrections, use the project issue tracker: <https://github.com/yswef/Y-Apex/issues>. This notice should be revised if the implementation, data flow, or hosting changes.
