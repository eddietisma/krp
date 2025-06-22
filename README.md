# `krp` - Kubernetes Reverse Proxy

[![NuGet](https://img.shields.io/nuget/v/krp?color=brightgreen&label=krp&logo=nuget&logoColor=white)](https://www.nuget.org/packages/krp)
[![dotnet tool](https://img.shields.io/nuget/v/dotnet-krp?color=brightgreen&label=dotnet-krp&logo=dotnet&logoColor=white)](https://www.nuget.org/packages/dotnet-krp)
[![docker](https://img.shields.io/docker/v/eddietisma/krp?color=brightgreen&label=docker&logo=docker&logoColor=white)](https://hub.docker.com/r/eddietisma/krp)


`krp` is a lightweight Kubernetes reverse proxy designed to provide on-demand port forwarding and seamless forwarding to internal Kubernetes resources. The tool facilitates automatic port forwards and provides dynamic routing via localhost using the hosts file.

### **Features**
- **On-Demand Port Forwarding:** Forward internal Kubernetes resources to your local machine automatically.
- **Context Aware:** Automatically adapts to changes in Kubernetes context and cluster.
- **Automatic Cleanup:** All active port forwards are cleaned up on application exit.
- **Dynamic Traffic Routing:** Routes traffic to localhost through the hosts file.
- **Zero Configuration:** Once running, the tool requires no further setup or user intervention.

### **Dependencies**
- [YARP](https://github.com/dotnet/yarp/): Provides dynamic HTTP(S) traffic routing capabilities.
- [DnsClient](https://github.com/MichaCo/DnsClient.NET): Facilitates DNS lookups when resolving HTTP endpoints.
- [kubectl port-forward](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_port-forward/): Used to forward Kubernetes pod ports to local machine ports.
- [kubernetes-client/csharp](https://github.com/kubernetes-client/csharp): Detects Kubernetes context switching and discovers endpoints.

## **How `krp` works**

1. **Endpoint registration**:  
   Uses static configuration or dynamic discovery for endpoints.

3. **Routing configuration:**  
   Adds endpoints to local hosts files. Each endpoint will get a unique loopback address (eg. `127.0.0.x myapi.namespace`).

5. **Reverse proxying:**  
   Listens on the local machine and proxies requests to endpoint targets.
   
7. **Port forwarding endpoints:**  
   Runs `kubectl port-forward` and forwards traffic to Kubernetes pods to local ports. Re-uses existing process if already exists, and if the pod dies the process also dies in which case a new one will spawn on-demand.
        
9. **HTTP proxy endpoints:**  
    Routes to local port if up, otherwise routes to original IP.

## Examples 

### Use case: Kubernetes endpoint

```
.UseEndpoint(0, 80, "namespace", "myapi") // 0 for dynamic local port selection
```

- Assume your cluster has a service exposed at `myapi.namespace:80`. 
- The hosts file will be modified to resolve `myapi.namespace` to `127.0.0.x`.
- Traffic will be proxied to `krp`.
- `krp` will find corresponding service based on loopback address and run `kubectl port-forward` to forward traffic to local port.
- You can then make requests as if the service was hosted locally: `curl myapi.namespace`

### Use case: HTTP proxy endpoint

```
.UseHttpEndpoint(5001, "domain.com", "api/service/v2")
```

- Assume your API gateway is using `domain.com/api/service/v2`. 
- The hosts file will be modified to resolve `domain.com` to `127.0.0.x`.
- Traffic will be proxied to `krp`.
- `krp` will find corresponding service based on loopback address and forwards traffic to local port 5001 if up.
- You can then make requests to the URL which will be proxied locally: `curl domain.com/api/service/v2`

## **Getting started** 🚀

### Prerequisites
- `kubectl` must be installed and authenticated against your Kubernetes cluster.
- `hosts` file modifications requires administrator privileges.

### Installation

```bash
# Using local code
git clone https://github.com/eddietisma/krp.git
cd krp
dotnet run
```

```bash
# Using dotnet tool
dotnet tool install --global dotnet-krp
krp
```

```bash
# Using docker
docker compose -f https://raw.githubusercontent.com/eddietisma/krp/main/docker-compose.yml up
```

```bash
# Setup HTTPS
dotnet dev-certs https -ep "%USERPROFILE%\.krp\krp.pfx" -p your-cert-password --trust
```

## **Usage**

### Configuration

You can configure port-forwarding and routing behavior by adding service definitions as follows:

```csharp
services.AddKubernetesForwarder()
    .UseHttpEndpoint(5000, "api.domain.com", "/api")
    .UseHttpEndpoint(5001, "api.domain.com", "/api/v2")
    .UseEndpoint(9032, 80, "namespace", "myapi") // Specific local port mappings
    .UseEndpoint(0, 80, "namespace", "myapi") // 0 for dynamic local port selection
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
        // Used for HTTP endpoints as fallback DNS resolver if the local port is not active.
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

#### `HttpForwarder`
- Supports HTTP requests (only).
- Supports domain based routing (using HTTP headers).
- Multiplexing HTTP/1.1 and HTTP/2 over cleartext using same port without TLS [is not supported].
   - Read more at (https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore?view=aspnetcore-8.0&tabs=visual-studio#protocol-negotiation) (https://github.com/dotnet/aspnetcore/issues/13502).
- Uses SSL termination.
  - For HTTPS either disable certificate validation on client or setup certificate for each domain.

#### `TcpForwarder`
- Supports low-level TCP requests.
- Supports domain based routing (using domain-based IP per hostname in hosts file).

#### `TcpWithHttpForwarder` (**default**)
- Supports low-level TCP requests.
- Supports domain based routing (using domain-based IP per hostname in hosts file)
- Forwards HTTP/x request to `HttpForwarder` using packet inspection.
  - Inspects TCP traffic and routes HTTP requests to different server ports based on protocol (81 for HTTP/1.1 and 82 for HTTP/2) without TLS requirement.

> [!NOTE]
> **When running Docker on Windows:** No support for domain based routing **for low-level TCP** due to docker networking limitations. Windows do not yet have full support for host network driver, which results in NAT issues when routing (all loopback IPs will originate from Docker gateway). Limiting routing to HTTP requests only for Windows hosts.
>
> For HTTPS we could use SNI to detect hostnames and use for routing but ran into issues with reacting to network changes due to already established TCP tunnels (need some more work to break existing TCP connections when needed).

## **Running in Docker**

To run `krp` in a Docker container, follow these steps:

1. **Start Docker Desktop** as an administrator (required for hosts file modification).
2. **Build and run the Docker container:**
   ```cli
   docker buildx bake
   docker compose up -d
   ```

3. **Authenticate kubectl with cluster:**  
   Most providers will encrypt the auth config for the specific machine. Hence mounting the config folder won't work inside the container.
   ```bash
   # For AKS
   docker exec -it $(docker ps --filter "name=krp" --format "{{.ID}}") az login  --use-device-code

   # For GKE
   todo..

   # For EKS
   todo...
   ```

### Example `docker-compose.yml`

```yaml
services:
  krp:
    build:
      context: .
    image: eddietisma/krp:latest
    container_name: krp
    restart: unless-stopped
    ports:
      - "80:80"
    #  - "443:443"
    environment:
    #  ASPNETCORE_Kestrel__Certificates__Default__Password: your-cert-password
    #  ASPNETCORE_Kestrel__Certificates__Default__Path: /root/.krp/krp.pfx
      AZURE_CONFIG_DIR: /root/.krp/.azure
      KRP_ENDPOINT_EXPLORER: false
      KRP_WINDOWS_HOSTS: /mnt/hosts
    volumes:
      - ~/.kube:/root/.kube
      - ~/.krp:/root/.krp
      - /c/Windows/System32/drivers/etc/:/host_etc/ # win
      # - /etc/hosts:/mtn/hosts/ # Linux/macOS
```

## Roadmap / Ideas
- [ ] Add integration tests.
- [x] Auto-discovery of Kubernetes services.
- [x] Support for low-level TCP traffic.
- [ ] Support for low-level UDP traffic.
- [ ] Support for translating internal Kubernetes IPs.
- [ ] Eliminate hosts file dependency using **WinDivert**/**PF**/**iptables** (or mitmproxy) for more flexible routing.
- [ ] Cross-platform support (Linux/macOS).
- [ ] User interface.
- [ ] Add GIF recordings of terminal use cases in README.
