# Changelog

All notable user-facing changes will be recorded here. This project has no public release yet; build artifacts in CI are for testing only.

## Unreleased

- Built a bilingual documentation site with product-status, privacy, security, installation, troubleshooting, and competitor-comparison sections.
- Clarified firewall direction behavior: block creates inbound and outbound block rules; allow creates an outbound allow rule only.
- Improved firewall previews to show the exact current and planned directions/actions.
- Preserved compatibility with legacy Y-Apex inbound allow rules so they can be read, restored, or removed without creating new ones.
- Added guarded Windows Firewall COM lifecycle coverage for an isolated temporary executable and bounded the process-identity cache.
- Added or expanded CI and site validation; no public installer has been released.
