# KRP (Kubernetes Reverse Proxy)

A lightweight dynamic reverse proxy for Kubernetes clusters on Windows.

This project uses:
- [YARP](https://github.com/dotnet/yarp/) (Yet Another Reverse Proxy) to dynamically route HTTP(S) traffic.
- [kubectl port-forward](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_port-forward/) to forward internal Kubernetes services and pods to your local machine.
- [kubernetes-client/csharp](https://github.com/kubernetes-client/csharp) to discover endpoints detect Kubernetes context switching.
- Windows hosts file modification for DNS resolution of internal cluster URLs.

## ✨ Features
- On-demand port forwarding to internal Kubernetes resources.
- Automatically handles active port-forwards when switching Kube context/cluster.
- Uses Windows HOST file to route URLs to KRP.
- Removes all active port-fowardings on application exit.
- Zero manual setup once running.

## 🛠 How it works

1. **Hosts modification**:  
   Adds cluster internal names (like `my-service.namespace.svc.cluster.local`) to your local hosts file, resolving to `127.0.0.1`.

2. **Port-forwarding with kubectl**: 
   Forwards Kubernetes service or pod ports to your local machine, using dynamically selected free ports.

3. **Reverse proxying with YARP**: 
   YARP listens on your machine and proxies HTTP(S) requests to the correct port-forwarded target automatically.

## 🚀 Quick Start

```powershell
git clone https://github.com/eddietisma/krp.git
cd krp
dotnet run
```

Make sure:
- `kubectl` is installed and authenticated against your cluster.
- Because this app modifies the Windows hosts, you **must** run it **as administrator**. ⚠️

## 📋 Example

Suppose your cluster has a service like:

```
my-api-service.default.svc.cluster.local:8080
```

Once running:
- The hosts file will resolve `my-api-service.default.svc.cluster.local -> 127.0.0.1`
- Traffic will automatically be routed through a local port-forward and proxied.

You can now just **curl**:

```powershell
curl http://my-api-service.default.svc.cluster.local/
```

🎯 without needing a VPN or complicated network setups!

## 📦 Roadmap / Ideas
- [ ] Auto-discovery of services from the cluster.
- [ ] Cross-platform support (Linux/macOS).
- [ ] Low-level TCP/UDP support.

## Running inside Docker

1. Start docker-desktop as administrator (for access to Windows hosts file).
1. Run `docker buildx bake`
1. Run `docker compose up -d`
1. Run `docker exec -it $(docker ps --filter "name=krp" --format "{{.ID}}") az login` (for Azure AKS)

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
