param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "publish"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("psn-publish-" + [guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    Copy-Item -LiteralPath (Join-Path $repoRoot "src") -Destination $tempRoot -Recurse

    $tempOut = Join-Path $tempRoot "publish"
    $projectPath = Join-Path $tempRoot "src\PortableSecureNotepad\PortableSecureNotepad.csproj"

    dotnet publish $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:UseAppHost=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $tempOut

    $projectOut = Join-Path $repoRoot $Output
    if (Test-Path -LiteralPath $projectOut) {
        Remove-Item -LiteralPath $projectOut -Recurse -Force
    }

    New-Item -ItemType Directory -Path $projectOut | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $tempOut "PortableSecureNotepad.exe") `
        -Destination (Join-Path $projectOut "PortableSecureNotepad.exe")

    Get-ChildItem -LiteralPath $projectOut | Select-Object Name,Length,LastWriteTime
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
