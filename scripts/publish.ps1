param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "publish",
    [switch]$ReplaceExisting
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectOut = if ([IO.Path]::IsPathRooted($Output)) { [IO.Path]::GetFullPath($Output) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $Output)) }
$destination = Join-Path $projectOut "PortableSecureNotepad.exe"
if ((Test-Path -LiteralPath $destination) -and -not $ReplaceExisting) {
    throw "An EXE already exists at $destination and may contain saved notes. Use a different -Output folder, or -ReplaceExisting to retain a backup before replacement."
}
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
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    New-Item -ItemType Directory -Path $projectOut -Force | Out-Null
    $publishedExe = Join-Path $tempOut "PortableSecureNotepad.exe"
    $publishId = [guid]::NewGuid().ToString("N")
    $stagedExe = Join-Path $projectOut "PortableSecureNotepad.psn-$publishId.publish.exe"
    $backupExe = $null
    [IO.File]::Copy($publishedExe, $stagedExe, $false)
    if ((Get-FileHash -LiteralPath $publishedExe).Hash -ne (Get-FileHash -LiteralPath $stagedExe).Hash) {
        throw "The staged EXE failed verification. The existing EXE was not replaced."
    }
    if (Test-Path -LiteralPath $destination) {
        if (-not $ReplaceExisting) { throw "An EXE appeared at $destination during publishing. It was not replaced." }
        $backupExe = Join-Path $projectOut "PortableSecureNotepad.psn-$publishId.backup.exe"
        $previousHash = (Get-FileHash -LiteralPath $destination).Hash
        [IO.File]::Replace($stagedExe, $destination, $backupExe)
        if ((Get-FileHash -LiteralPath $backupExe).Hash -ne $previousHash) {
            throw "The retained backup differs from the EXE inspected before publishing. Preserve it at $backupExe."
        }
    } else {
        [IO.File]::Move($stagedExe, $destination)
    }
    if ((Get-FileHash -LiteralPath $publishedExe).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
        throw "The published EXE failed verification. Preserve any backup at $backupExe."
    }

    if ($backupExe) { Write-Output "Previous EXE retained at: $backupExe" }
    Get-Item -LiteralPath $destination |
        Select-Object Name,Length,LastWriteTime
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
    $tempBoundary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedTemp.StartsWith($tempBoundary, [StringComparison]::OrdinalIgnoreCase) -and
        ([IO.Path]::GetFileName($resolvedTemp)).StartsWith('psn-publish-')) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
