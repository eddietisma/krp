# syntax=docker/dockerfile:1.16.0

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk

###################################
# CAKE
###################################
FROM sdk AS cake

WORKDIR /cake
RUN dotnet new tool-manifest
RUN dotnet tool install Cake.Tool --version 5.0.0
RUN curl -Lsfo build.sh https://cakebuild.net/download/bootstrapper/dotnet-tool/linux
RUN chmod +x build.sh

ENTRYPOINT ["/cake/build.sh"]

###################################
# Builder image
###################################
FROM cake AS build

ARG VERSION
ARG COMMIT

RUN --mount=type=cache,id=apt,target=/var/cache/apt \
    apt-get update && \
    apt-get install -y curl jq unzip lsb-release sudo gnupg apt-transport-https ca-certificates

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

WORKDIR /src
COPY . .

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    /cake/build.sh /src/build.cake -- --version=$VERSION --commit=$COMMIT

###################################
# Final image
###################################
FROM base AS final

RUN apt-get update && apt-get install -y curl jq unzip lsb-release sudo gnupg apt-transport-https ca-certificates

#Install Azure CLI
RUN curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

WORKDIR /app
COPY --link --from=build /usr/local/bin/kubectl /usr/local/bin/
COPY --link --from=build /usr/local/bin/kubelogin /usr/local/bin/
COPY --link --from=build /publish .
ENTRYPOINT ["dotnet", "Krp.dll"]

###################################
# Output image
###################################
FROM scratch AS output

COPY --link --from=build /pack /pack/
COPY --link --from=build /publish /publish/
COPY --link --from=build /testresults/*.trx /testresults/