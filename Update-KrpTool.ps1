dotnet tool uninstall --global dotnet-krp
$env:VERSION="9.9.9"; $env:COMMIT="abc123"; docker buildx bake 
dotnet tool install --global dotnet-krp --add-source ./output/pack