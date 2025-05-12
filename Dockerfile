
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore && \
    dotnet build -c Release --no-restore && \
    dotnet test -c Release --no-build --logger:trx && \
    dotnet publish src/Krp/Krp.csproj -c Release --no-build -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

RUN apt-get update && apt-get install -y curl jq unzip lsb-release sudo gnupg apt-transport-https ca-certificates

# Install Azure CLI
RUN curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

# Install kubectl
RUN KUBECTL_VERSION=$(curl -sL https://dl.k8s.io/release/stable.txt) && \
    curl -LO "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/amd64/kubectl" && \
    install -m 0755 kubectl /usr/local/bin/ && \
    rm kubectl

# Install kubelogin
RUN KUBELOGIN_VERSION=$(curl -s https://api.github.com/repos/Azure/kubelogin/releases/latest | jq -r .tag_name) && \
    curl -LO "https://github.com/Azure/kubelogin/releases/download/${KUBELOGIN_VERSION}/kubelogin-linux-amd64.zip" && \
    unzip kubelogin-linux-amd64.zip && \
    install -m 0755 bin/linux_amd64/kubelogin /usr/local/bin/ && \
    rm -rf bin kubelogin-linux-amd64.zip

WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Krp.dll"]