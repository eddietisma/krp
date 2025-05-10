# KRP

A lightweight Kubernetes Reverse Proxy.

This project uses:
- [YARP](https://github.com/dotnet/yarp/) (Yet Another Reverse Proxy) to dynamically route HTTP(S) traffic.
- [DnsClient](https://github.com/MichaCo/DnsClient.NET) for DNS lookups when using HTTP endpoints.
- [kubectl port-forward](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_port-forward/) to forward internal Kubernetes services and pods to your local machine.
- [kubernetes-client/csharp](https://github.com/kubernetes-client/csharp) to discover endpoints detect Kubernetes context switching.
- Modifies hosts file for DNS resolution of internal Kubernetes resources.

### Features
- On-demand port forwarding to internal Kubernetes resources.
- Automatically handles active port-forwards when switching Kube context/cluster.
- Automatically removes all active port-fowardings on application exit.
- Uses Windows HOST file to route URLs to KRP.
- Scans active ports to dynamically route HTTP traffic to localhost (eg https://gateway.domain.com/api/).
- Zero manual setup once running.

## How it works 🛠

1. **Hosts modification**:  
   Adds cluster internal names (like `my-service.namespace.svc.cluster.local`) to your local hosts file, resolving to `127.0.0.1`.

2. **Port-forwarding with kubectl**:  
   Forwards Kubernetes service or pod ports to your local machine, using dynamically selected free ports.

3. **Reverse proxying with YARP**:  
   YARP listens on your machine and proxies HTTP(S) requests to the correct port-forwarded target automatically.

## Getting started 🚀

```powershell
git clone https://github.com/eddietisma/krp.git
cd krp
dotnet run
```

Make sure:
- `kubectl` is installed and authenticated against your cluster.
- Because this app modifies the Windows hosts, you **must** run it **as administrator**. ⚠️

## Example 📋

Suppose your cluster has a service like:

```
myservice.myapi:8080
```

Once running:
- The hosts file will resolve `myservice.myapi -> 127.0.0.1`
- Traffic will automatically be routed through a local port-forward and proxied.

You can now just **curl**:

```powershell
curl http://myservice.myapi
```

## Usage

### Configuration

```csharp
services.AddKubernetesForwarder()
    .UseHttpEndpoint(5000, "api.domain.com", "/api")
    .UseHttpEndpoint(5001, "api.domain.com", "/api/v2")
    .UseEndpoint(9032, 80, "namespace", "myapi") // Specify local ports.
    .UseEndpoint(0, 80, "namespace", "myapi") // Use 0 for dynamic ports.
    .UseEndpointExplorer(options =>
    {
        // Specify filters to only map specific namespaces/services/pods.
        options.Filter = [
           "namespace/meetings/*",
           "namespace/*/service/person*",
        ];
        options.RefreshInterval = TimeSpan.FromHours(1);
    })
    .UseDnsLookup(options =>
    {
        // Used to fetch real IP for HTTP endpoints as fall-back when local port is not active.
        options.Nameserver = "8.8.8.8";
    })
    //.UseHttpForwarder()
    //.UseTcpForwarder(options =>
    // {
    //    options.ListenAddress = IPAddress.Any;
    //    options.ListenPort = 80;
    // })
    .UseTcpWithHttpForwarder(options =>
    {
        options.ListenAddress = IPAddress.Any;
        options.ListenPort = 80;
    })
    .UseRouting(DnsOptions.WindowsHostsFile);
```

### Forwarders
`UseHttpForwarder`
- Supports HTTP (only).
- Supports domain based routing.
- Multiplexing HTTP/1.1 and HTTP/2 over cleartext using same port without TLS **is not supported**.

`UseTcpForwarder`
- Supports low-level TCP requests (eg. databases, HTTP/x / gRCP).
- Supports domain based routing (using domain-based IP per hostname in HOSTS file).
- **Docker only:** No support for domain based routing under Windows hosts due to docker networking limitations. Windows do not yet have full support for host network driver, which results in NAT issues when routing (all IP originates from Docker gateway).

`UseTcpWithHttpForwarder` (**default**)
- Supports low-level TCP requests and forwards HTTP/x request to `HttpForwarder` using packet inspection.
- Opens a TCP connection and inspects traffic and routes HTTP to different server ports (81 for HTTP/1.1 and 82 for HTTP/2).
- Supports domain based routing (using domain-based IP per hostname in HOSTS file)
- **Docker only:** Due to limitation with Docker networking NAT all traffic will always originate from Docker gateway - limiting routing to HTTP requests only.

## Running inside Docker
1. Start docker-desktop as administrator (for hosts file access).
1. Run `docker buildx bake`.
1. Run `docker compose up -d`.
1. **AKS only**: Run `docker exec -it $(docker ps --filter "name=krp" --format "{{.ID}}") az login`.
1. **Windows only**: Enable host networking.

```
# Mount the kubeconfig for monitoring context switching
# Mount azure volume to preserve authentication used by Azure CLI
# Mount the Windows hosts file to the container (required to routing traffic on Windows hosts)

services:

  krp:
    build:
      context: .
    image: krp/krp:latest
    container_name: krp
    restart: unless-stopped
    ports:
      - "80:80"
    environment:
      KRP_ENDPOINT_EXPLORER: false
      KRP_WINDOWS_HOSTS: /windows_etc/hosts
    volumes:
      - ~/.kube:/root/.kube
      - azure:/root/.azure 
      - /c/Windows/System32/drivers/etc/:/windows_etc/ 

volumes:
    azure:
```

## 📦 Roadmap / Ideas
- [x] Auto-discovery of k8s services.
- [ ] Cross-platform support (Linux/macOS).
- [x] Low-level TCP support.
- [ ] Low-level UDP support.
- [ ] Remove hosts dependency using WinDivert/PF/iptables.
