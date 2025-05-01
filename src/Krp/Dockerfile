# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
#USER $APP_UID
WORKDIR /app

# Install dependencies
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


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Krp.csproj", "."]
RUN dotnet restore "./Krp.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./Krp.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Krp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Krp.dll"]