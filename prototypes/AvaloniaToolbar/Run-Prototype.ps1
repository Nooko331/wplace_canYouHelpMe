$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$portableExe = Join-Path $repoRoot 'publish\avalonia-toolbar-prototype\Wplace.ToolbarPrototype.exe'
if (Test-Path -LiteralPath $portableExe) {
    Start-Process -FilePath $portableExe -WindowStyle Normal
    exit
}

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
& $dotnetCommand run --project (Join-Path $PSScriptRoot 'AvaloniaToolbar.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw '样板启动失败。源码运行需要 .NET 10 SDK；打包后的 EXE 不需要另外安装 .NET。' }
