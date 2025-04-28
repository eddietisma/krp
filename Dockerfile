FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
COPY . .
RUN dotnet restore
RUN dotnet build -c Release --no-restore 
RUN dotnet test  -c Release --no-build --logger:trx
RUN dotnet publish --no-build "src/Krp/Krp.csproj" -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

RUN apt-get update && apt-get install -y curl jq unzip lsb-release sudo gnupg apt-transport-https ca-certificates

# Install Azure CLI
RUN curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

# Install kubectl
RUN curl -LO "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl" && \
    chmod +x kubectl && \
    mv kubectl /usr/local/bin/

# Install kubelogin (latest version)
RUN LATEST_KUBELOGIN_VERSION=$(curl -s https://api.github.com/repos/Azure/kubelogin/releases/latest | jq -r .tag_name) && \
    curl -LO "https://github.com/Azure/kubelogin/releases/download/${LATEST_KUBELOGIN_VERSION}/kubelogin-linux-amd64.zip" && \
    unzip kubelogin-linux-amd64.zip && \
    mv bin/linux_amd64/kubelogin /usr/local/bin/ && \
    rm kubelogin-linux-amd64.zip

WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Krp.dll"]