# Local k3s integration tests

These MSTest tests run the KRP CLI against a temporary k3s cluster. They cover service discovery across namespaces, HTTP forwarding in all three modes, paths and query strings, concurrent requests, recovery after pod replacement, port-forward process cleanup and HTTPS forwarding in HTTP and hybrid modes.

Requirements:

- Docker running Linux containers on Windows or Linux.
- .NET 10 SDK. On Windows, `kubectl` must be on `PATH`. On Linux, the fixture uses the client bundled in the k3s container.

Run from the repository root in PowerShell:

```powershell
dotnet test test/Krp.IntegrationTests/Krp.IntegrationTests.csproj -c Release
```

The integration tests run as part of the normal test suite, with up to four test methods in parallel. Class setup starts a shared `Testcontainers.K3s` fixture and obtains its kubeconfig using the randomly assigned API endpoint. Each test deploys the two apps into its own namespaces and starts a KRP process with its own hosts file and dynamically allocated listener ports. Test cleanup stops that process and deletes only its namespaces. Class cleanup disposes the k3s container and deletes the temporary files. The existing Kubernetes context and system hosts file stay unchanged.

All forwarding modes use `Krp:HttpPort` and `Krp:HttpsPort` for the public listener ports. Hybrid mode assigns its internal HTTP/1.1, HTTP/2 and HTTPS ports automatically. In HTTP mode, tests set `Krp:Http2Port` to `0` for automatic allocation of the separate HTTP/2 listener. Discovery filters come from `Krp:EndpointExplorer`. Tests pass those settings through environment variables.

The fixture uses `krp https` to check, create and export the CA, and `krp https --clean` to remove a CA it created. Existing CAs are reused. On Linux the CA lives in the temporary profile. The HTTPS clients validate against the exported CA directly.

BuildCharts runs this project through its standard `test` target using `TestcontainersDinD@v1`.

The HTTPS tests run on both Windows and Linux and cover the Windows Schannel regression caused by ephemeral private keys.
