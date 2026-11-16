# Local k3s integration tests

These MSTest tests run the KRP CLI against a temporary k3s cluster. They cover service discovery across namespaces, HTTP forwarding in all three modes, paths and query strings, concurrent requests, recovery after pod replacement, port-forward process cleanup and HTTPS forwarding in HTTP and hybrid modes.

Requirements:

- Docker running Linux containers on Windows or Linux.
- .NET 10 SDK. On Windows, `kubectl` must be on `PATH`. On Linux, the fixture uses the client bundled in the k3s container.
- Local ports 80, 81, 82, 443 and 444 available, with permission to bind them.

Run from the repository root in PowerShell:

```powershell
dotnet test test/Krp.IntegrationTests/Krp.IntegrationTests.csproj -c Release
```

The integration tests run as part of the normal test suite. Class setup starts a shared `Testcontainers.K3s` fixture and deploys the two apps. Test setup creates a fresh KRP process wrapper; test cleanup stops it. Class cleanup disposes the k3s container and deletes the temporary kubeconfig and hosts file. The existing Kubernetes context and system hosts file stay unchanged.

The fixture uses `krp https` to check, create and export the CA, and `krp https --clean` to remove a CA it created. Existing CAs are reused. On Linux the CA lives in the temporary profile. The HTTPS clients validate against the exported CA directly.

BuildCharts runs this project through its standard `test` target using `TestcontainersDinD@v1`.

The HTTPS tests run on both Windows and Linux and cover the Windows Schannel regression caused by ephemeral private keys.
