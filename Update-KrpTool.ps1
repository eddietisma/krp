dotnet tool uninstall --global dotnet-krp
$env:VERSION="1.0.1"; $env:COMMIT="abc123"; docker buildx bake 
dotnet tool install --global dotnet-krp --add-source ./output/pack