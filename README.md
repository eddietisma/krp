# `krp` - Kubernetes Reverse Proxy

[![NuGet](https://img.shields.io/nuget/v/krp?color=brightgreen&label=krp )](https://www.nuget.org/packages/krp)
[![NuGet](https://img.shields.io/nuget/v/dotnet-krp?color=brightgreen&label=dotnet-krp)](https://www.nuget.org/packages/dotnet-krp)

`krp` is a lightweight Kubernetes reverse proxy designed to provide on-demand port forwarding and seamless HTTP routing to internal Kubernetes resources. The tool facilitates automatic cleanup of active port forwards and provides dynamic routing of HTTP traffic via localhost using the Windows hosts file.

## **Features**
- **On-Demand Port Forwarding:** Forward internal Kubernetes resources to your local machine automatically.
- **Context-Sensitive Port Management:** Automatically adapts to changes in Kubernetes context and cluster.
- **Automatic Cleanup:** All active port forwards are cleaned up on application exit.
- **Dynamic Traffic Routing:** Routes HTTP(S) traffic to localhost through the Windows hosts file.
- **Zero Configuration:** Once running, the tool requires no further setup or user intervention.

## **Core Dependencies**
- [YARP](https://github.com/dotnet/yarp/): Provides dynamic HTTP(S) traffic routing capabilities.
- [DnsClient](https://github.com/MichaCo/DnsClient.NET): Facilitates DNS lookups when resolving Kubernetes service endpoints.
- [kubectl port-forward](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_port-forward/): Used to forward Kubernetes service or pod ports to local machine ports.
- [kubernetes-client/csharp](https://github.com/kubernetes-client/csharp): Detects Kubernetes context switching and discovers endpoints.

## **How `krp` works**

1. **DNS using hosts file:**  
   Adds internal cluster names to the local hosts file, resolving them to loopback addresses (eg. `127.0.0.x myapi.namespace`).

2. **Reverse proxying:**  
   Listens on the local machine and proxies requests to the appropriate port-forwarded targets.
   
3. **Port forwarding with `kubectl`:**  
   Dynamically runs `kubectl port-forward` to forwards traffic to Kubernetes pods to local machine ports (using looback IPs as routing).

### Use case

Assume your cluster has a service exposed at `myapi.namespace:8080`. With `krp` running:

- The hosts file will be modified to resolve `myapi.namespace` to `127.0.0.x`.
- HTTP traffic will be routed through the local port-forward and proxied via `krp`.

You can then make requests as if the service was hosted locally:

```cli
curl http://myservice.myapi
```

## **Getting started** 🚀

### Prerequisites
- `kubectl` must be installed and authenticated against your Kubernetes cluster.
- `hosts` file modifications requires administrator privileges.

### Installation

```cli
git clone https://github.com/eddietisma/krp.git
cd krp
dotnet run
```

```cli
dotnet tool install --global dotnet-krp
krp
```

```cli
docker pull krp:latest
docker run -d --name krp krp:latest
```

## **Usage**

### Configuration

You can configure port-forwarding and routing behavior by adding service definitions as follows:

```csharp
services.AddKubernetesForwarder()
    .UseHttpEndpoint(5000, "api.domain.com", "/api")
    .UseHttpEndpoint(5001, "api.domain.com", "/api/v2")
    .UseEndpoint(9032, 80, "namespace", "myapi") // Specific local port mappings
    .UseEndpoint(0, 80, "namespace", "myapi") // Dynamic local port selection
    .UseEndpointExplorer(options =>
    {
        // Filters to map specific namespaces, services, or pods
        options.Filter = [
           "namespace/meetings/*",
           "namespace/*/service/person*",
        ];
        options.RefreshInterval = TimeSpan.FromHours(1);
    })
    .UseDnsLookup(options =>
    {
        // Use a fallback DNS resolver if the local port is not active
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

### Forwarders available
`UseHttpForwarder`
- Supports HTTP (only).
- Supports domain based routing.
- Multiplexing HTTP/1.1 and HTTP/2 over cleartext using same port without TLS **is not supported**.
- Uses SSL termination (for HTTPS either disable certificate validation or setup certificate for each domain).

`UseTcpForwarder`
- Supports low-level TCP requests (eg. databases, HTTP/x / gRCP).
- Supports domain based routing (using domain-based IP per hostname in hosts file).
- **Docker only:** No support for domain based routing under Windows hosts due to docker networking limitations. Windows do not yet have full support for host network driver, which results in NAT issues when routing (all IP originates from Docker gateway).

`UseTcpWithHttpForwarder` (**default**)
- Supports low-level TCP requests and forwards HTTP/x request to `HttpForwarder` using packet inspection.
- Opens a TCP connection and inspects traffic and routes HTTP to different server ports (81 for HTTP/1.1 and 82 for HTTP/2).
- Supports domain based routing (using domain-based IP per hostname in hosts file)
- **Docker only:** Due to limitation with Docker networking NAT all traffic will always originate from Docker gateway - limiting routing to HTTP requests only.

## **Running in Docker**

To run `krp` in a Docker container, follow these steps:

1. **Start Docker Desktop** as an administrator (required for hosts file modification).
2. **Build and run the Docker container:**
   ```cli
   docker buildx bake
   docker compose up -d
   ```

3. **For AKS (Azure Kubernetes Service):**  
   Run `docker exec -it $(docker ps --filter "name=krp" --format "{{.ID}}") az login` to authenticate with Azure.

4. **For Windows environments:**  
   Ensure the **host network mode** is enabled in the Docker configuration.

### Example `docker-compose.yml`

```yaml
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

## Roadmap / Ideas
- [ ] Add integration tests.
- [x] Auto-discovery of Kubernetes services.
- [x] Support for low-level TCP traffic forwarding.
- [ ] Support for low-level UDP traffic forwarding.
- [ ] Support for translating internal Kubernetes IPs.
- [ ] Eliminate hosts file dependency using **WinDivert**/**PF**/**iptables** (or mitmproxy) for more flexible routing.
- [ ] Cross-platform support (Linux/macOS).
- [ ] User interface.
