# Third-party notices

Y-Apex source code and original SVG artwork are licensed under the repository's MIT license unless a separate notice states otherwise.

Runtime/build dependencies used by the .NET projects:

- BouncyCastle.Cryptography 2.4.0 — MIT License; used for offline Ed25519 signature verification.
- Microsoft.Data.Sqlite 10.0.15 — MIT License.
- Microsoft.Extensions.Hosting.WindowsServices 10.0.15 — MIT License.
- Microsoft.Diagnostics.Tracing.TraceEvent 4.0.2 — MIT License; used for local Windows kernel TCP/IP ETW byte accounting.
- System.Security.Cryptography.ProtectedData 10.0.15 — MIT License.
- xUnit 2.9.3, xUnit runner for Visual Studio 3.1.1, and Microsoft.NET.Test.Sdk 17.14.1 — see the package metadata for their Apache-2.0/MIT notices.

The icon export script has optional developer-only Python dependencies listed in `tools/requirements-icons.txt`: Pillow 11.1.0 (HPND) and svgpathtools 1.8.0 (MIT). Neither package is shipped with or loaded by the application. Their upstream license texts apply to those tools.
