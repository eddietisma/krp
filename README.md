# `krp` - Kubernetes Reverse Proxy

[![NuGet](https://img.shields.io/nuget/v/krp?color=brightgreen&label=krp )](https://www.nuget.org/packages/krp)
[![NuGet](https://img.shields.io/nuget/v/dotnet-krp?color=brightgreen&label=dotnet-krp)](https://www.nuget.org/packages/dotnet-krp)

`krp` is a lightweight Kubernetes reverse proxy designed to provide on-demand port forwarding and seamless forwarding to internal Kubernetes resources. The tool facilitates automatic port forwards and provides dynamic routing via localhost using the hosts file.

### **Features**
- **On-Demand Port Forwarding:** Forward internal Kubernetes resources to your local machine automatically.
- **Context-Sensitive Port Management:** Automatically adapts to changes in Kubernetes context and cluster.
- **Automatic Cleanup:** All active port forwards are cleaned up on application exit.
- **Dynamic Traffic Routing:** Routes traffic to localhost through the hosts file.
- **Zero Configuration:** Once running, the tool requires no further setup or user intervention.

### **Core Dependencies**
- [YARP](https://github.com/dotnet/yarp/): Provides dynamic HTTP(S) traffic routing capabilities.
- [DnsClient](https://github.com/MichaCo/DnsClient.NET): Facilitates DNS lookups when resolving HTTP endpoints.
- [kubectl port-forward](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_port-forward/): Used to forward Kubernetes pod ports to local machine ports.
- [kubernetes-client/csharp](https://github.com/kubernetes-client/csharp): Detects Kubernetes context switching and discovers endpoints.

## **How `krp` works**

1. **Endpoint registration**:  
   Uses static configuration or dynamic endpoint discovery.

3. **Routing configuration:**  
   Adds endpoints to local hosts file, resolving to loopback addresses. Each endpoint will get a unique loopback address (eg. `127.0.0.x myapi.namespace`).

5. **Reverse proxying:**  
   Listens on the local machine and proxies requests to endpoint targets.
   
7. **Port forwarding endpoints:**  
   Runs `kubectl port-forward` and forwards traffic to Kubernetes pods to local ports. Re-uses existing process if already exists, and if the pod dies the process also dies in which case a new one will spawn on-demand.
        
9. **HTTP proxy endpoints:**  
    Routes to local port if up, otherwise routes to original IP.

### Use case

Assume your cluster has a service exposed at `myapi.namespace:80`. 

With `krp` running:
- The hosts file will be modified to resolve `myapi.namespace` to `127.0.0.x`.
- Traffic will be proxied to krp.
- `krp` will find corresponding service based on loopback address and run `kubectl port-forward` to forward traffic to the local port.

You can then make requests as if the service was hosted locally:

```cli
curl http://myapi.namespace:80
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
dotnet dev-certs https -ep "%USERPROFILE%\.krp\krp.pfx" -p your-cert-password --trust
docker compose -f https://raw.githubusercontent.com/eddietisma/krp/main/docker-compose.yml up
docker exec -it $(docker ps --filter "name=krp" --format "{{.ID}}") az login  --use-device-code

docker run -d `
   --name krp `
   -v "${env:USERPROFILE}\.kube:/root/.kube" `
   -v "${env:USERPROFILE}\.krp:/root/.krp" `
   -v "${env:USERPROFILE}\.krp\azure:/root/.azure" `
   -v "c/Windows/System32/drivers/etc/:/windows_etc/" `
   -e ASPNETCORE_Kestrel__Certificates__Default__Password="your-cert-password" `
   -e ASPNETCORE_Kestrel__Certificates__Default__Path="/root/.krp/krp.pfx" `
   eddietisma/krp:latest
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
- Multiplexing HTTP/1.1 and HTTP/2 over cleartext using same port without TLS **is not supported**.
- Uses SSL termination (for HTTPS either disable certificate validation on client or setup certificate for each domain).

#### `TcpForwarder`
- Supports low-level TCP requests.
- Supports domain based routing (using domain-based IP per hostname in hosts file).

#### `TcpWithHttpForwarder` (**default**)
- Supports low-level TCP requests.
- Supports domain based routing (using domain-based IP per hostname in hosts file)
- Forwards HTTP/x request to `HttpForwarder` using packet inspection. Inspects TCP traffic and routes HTTP requests to different server ports based on protocol (81 for HTTP/1.1 and 82 for HTTP/2).

> [!NOTE]
> **When running Docker using Windows hosts:** No support for domain based routing **for low-level TCP** due to docker networking limitations. Windows do not yet have full support for host network driver, which results in NAT issues when routing (all loopback IPs will originate from Docker gateway). Limiting routing to HTTP requests only for Windows hosts.
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

3. **For AKS (Azure Kubernetes Service):**  
   Run `docker exec -it $(docker ps --filter "name=krp" --format "{{.ID}}") az login` to authenticate with Azure.

4. **For Windows environments:**  
   Ensure the **host network mode** is enabled in the Docker configuration.

### Example `docker-compose.yml`

```yaml
services:
  krp:
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
- [x] Support for low-level TCP traffic.
- [ ] Support for low-level UDP traffic.
- [ ] Support for translating internal Kubernetes IPs.
- [ ] Eliminate hosts file dependency using **WinDivert**/**PF**/**iptables** (or mitmproxy) for more flexible routing.
- [ ] Cross-platform support (Linux/macOS).
- [ ] User interface.
