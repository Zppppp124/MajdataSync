$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'MajdataSync.csproj'
$output = Join-Path $PSScriptRoot 'publish'

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $output
if ($LASTEXITCODE -ne 0) {
    throw 'Publish failed. No new executable was produced.'
}

$launcher = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cmd' -File | Select-Object -First 1
if ($null -ne $launcher) {
    Copy-Item -LiteralPath $launcher.FullName -Destination (Join-Path $output $launcher.Name) -Force
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets\app.ico') -Destination (Join-Path $output 'app.ico') -Force

$web = Join-Path $output 'Web'
if (-not (Test-Path $web)) {
    throw "Published output is missing the Web folder: $web"
}

Write-Output "Publish complete: $output"
