$ErrorActionPreference = 'Stop'

# A unique version prevents NuGet from reusing a previous local build.
$version = "9.9.9-local.$([DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))"
$output = Join-Path $PSScriptRoot '.buildcharts/output/nuget'
$project = Join-Path $PSScriptRoot 'src/dotnet-krp/dotnet-krp.csproj'
$commit = & git -C $PSScriptRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the current Git commit.' }

# Build on the host so updating the tool does not depend on generated Docker charts.
dotnet pack $project --configuration Release --output $output -p:Version=$version -p:SourceRevisionId=$commit -p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) { throw 'Package build failed. The installed tool was left unchanged.' }

$package = Join-Path $output "dotnet-krp.$version.nupkg"
if (!(Test-Path -LiteralPath $package)) { throw "Expected package was not created: $package" }

dotnet tool update --global dotnet-krp --version $version --source $output --no-http-cache --allow-downgrade
if ($LASTEXITCODE -ne 0) { throw 'Installing the locally built tool failed.' }

# Run the installed assembly directly because the Windows shim requests elevation.
$installedAssembly = Join-Path $HOME ".dotnet/tools/.store/dotnet-krp/$version/dotnet-krp/$version/tools/net10.0/any/dotnet-krp.dll"
dotnet $installedAssembly --version
if ($LASTEXITCODE -ne 0) { throw 'The installed tool failed its startup check.' }
