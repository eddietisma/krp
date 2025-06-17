variable "VERSION" {}
variable "COMMIT" {}

target "_common" {
  args = {
    VERSION = "${VERSION}"
    COMMIT = "${COMMIT}"
  }
}

target "build" {
  inherits = ["_common"]
  target = "build"
  context = "."
  args = {
    BUILDCHARTS_SRC = "krp.sln"
    BUILDCHARTS_TYPE = "build"
  }
  output = ["type=cacheonly,mode=max"]
  contexts = {
    base = "docker-image://mcr.microsoft.com/dotnet/sdk:9.0"
  }
  dockerfile = "./.buildcharts/dotnet-build/Dockerfile"
}

target "nuget__src-dotnet-krp-dotnet-krp-csproj" {
  inherits = ["_common"]
  target = "nuget"
  args = {
    BUILDCHARTS_SRC = "src/dotnet-krp/dotnet-krp.csproj"
    BUILDCHARTS_TYPE = "nuget"
  }
  output = ["type=cacheonly,mode=max"]
  contexts = {
    build = "target:build"
  }
  dockerfile = "./.buildcharts/dotnet-nuget/Dockerfile"
}

target "nuget__src-krp-krp-csproj" {
  inherits = ["_common"]
  target = "nuget"
  args = {
    BUILDCHARTS_SRC = "src/Krp/Krp.csproj"
    BUILDCHARTS_TYPE = "nuget"
  }
  output = ["type=cacheonly,mode=max"]
  tags = [
    "docker.io/eddietisma/krp:${VERSION}-${COMMIT}"
  ]
  contexts = {
    build = "target:build"
    base = "docker-image://mcr.microsoft.com/dotnet/aspnet:9.0"
  }
  dockerfile = "./.buildcharts/dotnet-nuget/Dockerfile"
}

target "docker" {
  inherits = ["_common"]
  target = "docker"
  args = {
    BUILDCHARTS_SRC = "src/Krp/Krp.csproj"
    BUILDCHARTS_TYPE = "docker"
  }
  output = ["type=docker"]
  tags = [
    "docker.io/eddietisma/krp:${VERSION}-${COMMIT}"
  ]
  contexts = {
    build = "target:build"
    base = "docker-image://mcr.microsoft.com/dotnet/aspnet:9.0"
  }
  dockerfile = "./.buildcharts/dotnet-docker/Dockerfile"
}

target "test__test-krp-tests-krp-tests-csproj" {
  inherits = ["_common"]
  target = "test"
  args = {
    BUILDCHARTS_SRC = "test/Krp.Tests/Krp.Tests.csproj"
    BUILDCHARTS_TYPE = "test"
  }
  output = ["type=cacheonly,mode=max"]
  contexts = {
    build = "target:build"
  }
  dockerfile = "./.buildcharts/dotnet-test/Dockerfile"
}

target "test__test-krp-tests1-krp-tests1-csproj" {
  inherits = ["_common"]
  target = "test"
  args = {
    BUILDCHARTS_SRC = "test/Krp.Tests1/Krp.Tests1.csproj"
    BUILDCHARTS_TYPE = "test"
  }
  output = ["type=cacheonly,mode=max"]
  contexts = {
    build = "target:build"
  }
  dockerfile = "./.buildcharts/dotnet-test/Dockerfile"
}

target "output" {
  output = [
    "type=local,dest=.buildcharts/output"
  ]
  contexts = {
    nuget__src-dotnet-krp-dotnet-krp-csproj = "target:nuget__src-dotnet-krp-dotnet-krp-csproj"
    nuget__src-krp-krp-csproj = "target:nuget__src-krp-krp-csproj"
    test__test-krp-tests-krp-tests-csproj = "target:test__test-krp-tests-krp-tests-csproj"
    test__test-krp-tests1-krp-tests1-csproj = "target:test__test-krp-tests1-krp-tests1-csproj"
  }
  dockerfile-inline = <<BUILDCHARTS_EOF
FROM scratch AS output
COPY --link --from=nuget__src-dotnet-krp-dotnet-krp-csproj /output /nuget
COPY --link --from=nuget__src-krp-krp-csproj /output /nuget
COPY --link --from=test__test-krp-tests-krp-tests-csproj /output /test
COPY --link --from=test__test-krp-tests1-krp-tests1-csproj /output /test
BUILDCHARTS_EOF
}

group "default" {
  targets = ["build", "nuget__src-dotnet-krp-dotnet-krp-csproj", "nuget__src-krp-krp-csproj", "docker", "test__test-krp-tests-krp-tests-csproj", "test__test-krp-tests1-krp-tests1-csproj", "output"]
}
