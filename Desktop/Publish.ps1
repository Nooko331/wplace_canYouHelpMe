param([string]$OutputDirectory = '', [switch]$SkipZip)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Wplace.Desktop.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$bundledSdk = Join-Path $projectRoot 'artifacts\dotnet10\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $bundledSdk) { $bundledSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'artifacts\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot 'artifacts\nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot 'artifacts\nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $projectRoot 'artifacts\nuget-plugins'
$env:NUGET_SCRATCH = Join-Path $projectRoot 'artifacts\nuget-scratch'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot "publish\avalonia-v$version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
& $dotnetCommand publish (Join-Path $PSScriptRoot 'Wplace.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Avalonia publish failed.' }
$exe = Join-Path $OutputDirectory "wplace_canYouHelpMe_Avalonia_v$version.exe"
if (!(Test-Path -LiteralPath $exe)) { throw "Expected executable was not produced: $exe" }
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
Write-Output "EXE: $exe"
Write-Output "SHA256: $hash"
if (!$SkipZip) {
    # Archive only the executable: never include local user settings, logs or notes history.
    $zip = Join-Path (Split-Path -Parent $OutputDirectory) "wplace_canYouHelpMe_Avalonia_v${version}_win-x64.zip"
    if (Test-Path -LiteralPath $zip) { throw "Archive already exists; preserve it or choose -SkipZip: $zip" }
    Compress-Archive -LiteralPath $exe -DestinationPath $zip
    Write-Output "ZIP: $zip"
}
