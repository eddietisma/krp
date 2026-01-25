buildcharts generate
dotnet tool uninstall --global dotnet-krp
docker buildx bake --file .buildcharts/docker-bake.hcl --no-cache --set *.args.VERSION="9.9.9" --set *.args.COMMIT=$(git rev-parse HEAD) nuget
dotnet tool install --global dotnet-krp --add-source ./.buildcharts/output/nuget