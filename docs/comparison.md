# Y-Apex, Portmaster and GlassWire: feature comparison

**Research checked:** 2026-11-22.
**Method:** Y-Apex entries describe the current repository implementation, not verified end-user behavior. Portmaster and GlassWire entries summarize their vendors' public product pages, manuals, pricing pages, license/source references, and release notes. This is not a hands-on benchmark, security audit, or endorsement. Pricing, packaging, and feature availability can change by plan and region.

## Short version

Y-Apex is an unreleased Windows development project. It currently focuses on local connection snapshots and cautious management of Y-Apex-owned Windows Firewall rules. It has not yet passed an interactive Windows-VM install/firewall-change test, so it should not be treated as a production alternative to either established product.

Portmaster is the broadest privacy-oriented application firewall in this comparison: its official materials describe Windows and Linux support, per-app rules, tracker/malware filtering and secure DNS, plus paid investigative features and SPN. GlassWire emphasizes an approachable visual traffic history, alerts, device/network monitoring and Windows Firewall management; its Windows and Android products have different capabilities. Their feature sets should not be conflated with Y-Apex's current prototype.

## Capability matrix

| Dimension | Y-Apex (current source) | Portmaster | GlassWire |
|---|---|---|---|
| Product status | Unreleased development build; public installer and interactive Windows-VM validation are still outstanding. | Published, maintained application with official downloads and documentation. | Published commercial product with official downloads, plans, help guides and release notes. |
| Main purpose | Windows connection snapshots and management of rules created by Y-Apex. | Application firewall and privacy suite. | Visual network monitor, security alerts and firewall management. |
| Connection visibility | TCP/UDP socket-table snapshots linked to process identity where Windows permits. UI refreshes periodically; not a durable connection-history view. | Vendor describes real-time, per-application network monitoring and investigation. | Current and past activity visualized in graphs, with host/app breakdowns and a Network Time Machine. |
| Firewall rule control | Windows Firewall COM. Block creates inbound and outbound block rules. Allow creates outbound allow only; it does not open inbound access. Preview, explicit confirmation, journal and rollback are implemented in source, but still require VM validation. Rules match file paths. | Per-app and global rules, with connection controls and filtering. Windows integration uses a kernel-mode WFP driver according to the project README. | On Windows, manages the built-in Windows Firewall. Its manual documents separate inbound/outbound controls, Ask to Connect, Lock Down and profiles. |
| DNS, ads and tracker filtering | No secure DNS, domain rules, threat feeds, or system-wide tracker filter in the current build. | Vendor advertises secure DNS (DoT/DoH) and system-wide tracker/malware filtering; free and paid plan details differ. | No system-wide secure-DNS resolver is listed in the reviewed product materials. The Windows product does advertise DNS-setting-change alerts and suspicious-host monitoring. |
| Usage and history | ETW byte totals by process from service start; counters are in memory and reset when the service stops. Rule-change journal is not traffic history. | Network history and per-app bandwidth visibility are listed as paid investigative features. | Traffic graphs and time-machine history; official pricing lists 24-hour history/bandwidth for Free and unlimited history/bandwidth for Premium. |
| Other monitoring | No LAN device scanner, remote endpoint monitoring, Wi-Fi threat alerts, or hardware-resource monitor in the current source. | Network privacy/filter controls, with additional investigative features and optional SPN. | Official features include device/network scanning, remote monitoring, suspicious-host and network/security alerts, and hardware resource monitoring. Availability varies by product/plan. |
| Platforms | Windows x64 only in this repository. | Windows and Linux in official materials. | Windows desktop and a separate Android app. Do not assume their features are identical across platforms. |
| Source/license | Repository declares MIT. The WFP project is a stub and is not an active custom packet-filtering provider. | Public source repository; GPL-3.0. | Commercial/proprietary license terms; the official license reserves source-code rights. |
| Privacy/network model | Current code configures no telemetry sender or network updater. Local socket metadata, usage counters and firewall journal are processed on-device. | Vendor describes core local privacy features, while signed application/intelligence updates and the optional SPN involve network activity. | Vendor documentation says personal network/graph history stays local. Its services privacy policy also describes account, device/diagnostic information and traffic counters/IPs for selected advanced functionality; consult the current policy for the specific plan and feature. |
| Price snapshot | No public release or product price. | Free tier; official page lists Plus at €40/year and Pro at €8/month or €80/year. | Free tier; official page lists Personal Premium at $2.99/month billed annually per license before any promotion. |

## What this comparison means

- Choose based on the actual capability you need: Portmaster has broader privacy filtering and DNS functions; GlassWire offers extensive visual history/alerting and network discovery; Y-Apex currently provides a narrower Windows-oriented prototype.
- A preview, signature display, file hash, or rule journal does not make a Windows Firewall rule cryptographically bound to a publisher. Windows Firewall rules in this build match the executable path.
- Y-Apex has not yet demonstrated installer behavior, service ACL behavior, rule enforcement, rollback, or emergency cleanup in an interactive Windows VM. Its source and CI checks are not a substitute for that release gate.
- A feature marked as vendor-advertised is not an independent verification of the competitor's implementation. Read the linked vendor documentation, license, privacy policy and plan details before deciding.

## Official references reviewed

### Y-Apex

- [Repository and source](https://github.com/yswef/Y-Apex)
- [Architecture](https://github.com/yswef/Y-Apex/blob/main/docs/architecture.md)
- [Threat model](https://github.com/yswef/Y-Apex/blob/main/docs/threat-model.md)
- [Privacy notice](https://github.com/yswef/Y-Apex/blob/main/docs/privacy.md)

### Portmaster / Safing

- [Official features](https://safing.io/features/)
- [Official pricing and plan comparison](https://safing.io/pricing/)
- [Official Portmaster source repository and license](https://github.com/safing/portmaster)
- [Official Portmaster releases](https://github.com/safing/portmaster/releases)
- [Official business model](https://safing.io/business-model/)

### GlassWire

- [Official features](https://www.glasswire.com/features/)
- [Official Windows user guide](https://www.glasswire.com/userguide/)
- [Official pricing and plan comparison](https://www.glasswire.com/pricing/)
- [Official privacy policy](https://www.glasswire.com/privacy/)
- [Official license terms](https://www.glasswire.com/license/)
- [Official release notes](https://www.glasswire.com/changes/)
- [Official Android guide](https://www.glasswire.com/android-help/)
