$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$localSdk = Join-Path $repoRoot 'artifacts\dotnet10\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $repoRoot 'artifacts\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $repoRoot 'artifacts\nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $repoRoot 'artifacts\nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $repoRoot 'artifacts\nuget-plugins'
$env:NUGET_SCRATCH = Join-Path $repoRoot 'artifacts\nuget-scratch'
$outputDirectory = Join-Path $repoRoot 'publish\avalonia-toolbar-prototype'
& $dotnetCommand publish (Join-Path $PSScriptRoot 'AvaloniaToolbar.csproj') --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false --output $outputDirectory --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw '样板打包失败。需要 .NET 10 SDK。' }
foreach ($symbolName in @('libHarfBuzzSharp.pdb', 'libSkiaSharp.pdb')) {
    $symbolPath = Join-Path $outputDirectory $symbolName
    if (Test-Path -LiteralPath $symbolPath) { Remove-Item -LiteralPath $symbolPath }
}
Write-Output (Join-Path $outputDirectory 'Wplace.ToolbarPrototype.exe')
