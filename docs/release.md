# Release and distribution gate

## Current status

There is **no public Y-Apex release or supported public installer**. CI artifacts are temporary, unsigned test packages. The current repository has not yet passed a manual, interactive Windows-VM installation and firewall-rule test. Do not publish a binary or describe the project as ready for a primary machine until every gate below has passed.

## Required gates before a public release

1. **CI:** Windows Release build and tests, guarded Windows Firewall COM lifecycle test on an isolated runner, installer install/service/pipe/CLI/uninstall smoke test, dependency vulnerability review, CodeQL, gitleaks, checksums, and SBOM must all pass for the exact release commit.
2. **Interactive Windows VM:** Create a restore snapshot; install as a standard desktop user with elevation; verify desktop SID enrollment and normal-user UI-to-service named-pipe access; test connection refresh, outbound allow, inbound/outbound block, preview details, 30-second rollback, confirm/undo, emergency removal, service restart recovery, upgrade, and interactive/silent uninstall. Confirm unrelated Windows Firewall rules remain unchanged. Record Windows build, outcome, and any limitations.
3. **Safety review:** Confirm there are no unexpected firewall rules, the uninstall policy is understood, and the privacy notice, comparison, and screenshots/documentation match the tested product.
4. **Code signing:** Sign the service, UI, CLI, and installer with a trusted Windows code-signing identity kept outside the repository. Verify Authenticode status and publish the certificate identity/fingerprints needed for user verification. Do not place private keys or passwords in Git.
5. **Release artifacts:** Produce the final x64 package, generate and verify SHA-256 checksums and SBOM, publish concise release notes, and make clear whether any artifacts are unsigned or experimental. Never reuse an artifact from a different commit.
6. **Publication:** Create a version tag and GitHub Release only after explicit release approval and all gates have passed. A Pages workflow is configured for documentation changes on `main`, but Pages configuration and a successful live deployment have not been verified; a Pages deployment is not a software release.

## CI artifact and external scanning policy

The CI artifact `Y-Apex-Windows-x64` is retained for a limited period to support review; it is not a stable download. CI does not upload binaries to VirusTotal. Any external scan requires explicit approval because it sends the executable to a third party. Checksums establish file integrity against the published digest but do not prove the publisher's identity; code signing is a separate gate.

## Uninstall behavior

The interactive installer asks whether to remove Y-Apex-owned Windows Firewall rules. Choosing **Yes** removes only those rules and then deletes shared service data. Choosing **No** stops/removes the service but leaves managed rules and service data for manual cleanup. Silent uninstall defaults to **No** and preserves rules and data. Users should use the emergency-removal flow before uninstalling when they have decided all Y-Apex rules must be removed, or use Windows Firewall management to inspect remaining rules manually.
