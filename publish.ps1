#requires -Version 7.0
<#
.SYNOPSIS
    Publishes the server for Windows and the client for Windows and Apple Silicon macOS.

.DESCRIPTION
    Output layout:

        publish/win-x64/    nscreen-server.exe + nscreen-client.exe (+ client native libs)
        publish/osx-arm64/  nscreen-client (+ native libs)

    The server is Windows-only by nature - DXGI Desktop Duplication has no macOS equivalent - so it
    is never published for osx.

    The server tries NativeAOT first (smallest, no runtime, instant startup) and falls back to a
    trimmed single-file publish when the MSVC linker is missing. The client is always trimmed
    single-file: NativeAOT cannot cross-compile from Windows to macOS, and having the two platforms
    built the same way is worth more than a few MB.

    Neither machine needs .NET installed. Note that the client is not one file: Skia, HarfBuzz and
    the Avalonia native backend are native libraries and stay next to the executable. Copy the whole
    platform folder, not just the binary.

.PARAMETER Runtime
    Which platforms to publish, both by default. `-Runtime win-x64` is the short loop while
    iterating, and it is also how the two CI jobs split the work: each runner builds its own
    platform natively.

.PARAMETER Version
    The version stamped into the binaries, `1.4.0`. Left out, the assemblies carry the SDK default
    of 1.0.0. The release workflow derives the number from the git tags and passes it here.

.PARAMETER NoAot
    Skip the NativeAOT attempt for the server and go straight to single-file.

.PARAMETER RequireAot
    Stop instead of falling back when NativeAOT is unavailable. CI passes this, so a runner image
    that lost its C++ toolchain fails the build rather than quietly shipping a different server.

.EXAMPLE
    ./publish.ps1
    ./publish.ps1 -Runtime win-x64
    ./publish.ps1 -Runtime osx-arm64 -Version 1.4.0
#>
param(
    [ValidateSet('win-x64', 'osx-arm64')]
    [string[]] $Runtime = @('win-x64', 'osx-arm64'),
    [string] $Version,
    [switch] $NoAot,
    [switch] $RequireAot
)

$ErrorActionPreference = 'Stop'
# This script inspects $LASTEXITCODE itself, so a failing dotnet invocation must not throw.
$PSNativeCommandUseErrorActionPreference = $false

Set-Location $PSScriptRoot

if ($NoAot -and $RequireAot) { throw '-NoAot and -RequireAot ask for opposite things.' }

$server = 'src/NScreen.Server/NScreen.Server.csproj'
$client = 'src/NScreen.Client/NScreen.Client.csproj'

$singleFile = @(
    '--self-contained', 'true'
    '-p:PublishSingleFile=true'
    '-p:PublishTrimmed=true'
    '-p:TrimMode=full'
    '-p:EnableCompressionInSingleFile=true'
)

# Typed and assigned in two steps on purpose: PowerShell unrolls a one-element array on its way
# out of an `if`, and a bare string would splat one character at a time.
[string[]] $stamp = @()
if ($Version) { $stamp = @("-p:Version=$Version") }

# Only the platforms being rebuilt are cleared, so `-Runtime win-x64` leaves an osx output in place.
foreach ($rid in $Runtime) {
    $stale = Join-Path 'publish' $rid
    if (Test-Path $stale) { Remove-Item $stale -Recurse -Force }
}

function Invoke-Publish($project, $runtime, $extra, $expectedBinary) {
    $out = Join-Path 'publish' $runtime

    # Out-Host, not bare output: anything a function writes to the pipeline is part of its return
    # value, so without this the caller gets an array of build log lines (always truthy) instead of
    # the boolean, and a failed publish reads as success.
    dotnet publish $project -c Release -r $runtime -o $out @extra @stamp --nologo | Out-Host
    $done = ($LASTEXITCODE -eq 0) -and (Test-Path (Join-Path $out $expectedBinary))

    # ILC writes a native .pdb beside the AOT binary on Windows and StripSymbols does not reach it:
    # 8 MB of symbols for a 2 MB executable, which was 46% of the v0.1.0 archive. A target in the
    # project cannot do this - the SDK copies those symbols from its own AfterTargets="Publish", and
    # Sdk.targets is imported after the project body, so it runs last and puts the file back. Here the
    # publish has already exited. Managed symbols are embedded (DebugType=embedded), so a publish
    # output is the binary and nothing else.
    if ($done) { Get-ChildItem $out -Filter *.pdb -File | Remove-Item -Force }

    return $done
}

# --- server: win-x64 only, AOT if the toolchain is there ---
if ($Runtime -contains 'win-x64') {
    $serverDone = $false
    if (-not $NoAot) {
        Write-Host 'Publishing nscreen-server with NativeAOT...' -ForegroundColor Cyan
        $serverDone = Invoke-Publish $server 'win-x64' @('-p:PublishAot=true', '-p:StripSymbols=true') 'nscreen-server.exe'
        if (-not $serverDone) {
            if ($RequireAot) {
                throw 'NativeAOT publish failed, and -RequireAot rules out the single-file fallback.'
            }

            Write-Host ''
            Write-Host 'NativeAOT unavailable (MSVC linker missing?). Falling back to single-file.' -ForegroundColor Yellow
            Write-Host 'To enable AOT: install the "Desktop development with C++" VS workload.' -ForegroundColor DarkGray
            Write-Host ''
        }
    }
    if (-not $serverDone) {
        Write-Host 'Publishing nscreen-server trimmed single-file...' -ForegroundColor Cyan
        $serverDone = Invoke-Publish $server 'win-x64' $singleFile 'nscreen-server.exe'
    }
    if (-not $serverDone) { throw 'Publish failed for nscreen-server.' }
}

# --- client: every requested platform ---
foreach ($rid in $Runtime) {
    $binary = if ($rid -eq 'win-x64') { 'nscreen-client.exe' } else { 'nscreen-client' }
    Write-Host "Publishing nscreen-client for $rid..." -ForegroundColor Cyan
    if (-not (Invoke-Publish $client $rid $singleFile $binary)) {
        throw "Publish failed for nscreen-client ($rid)."
    }
}

Write-Host ''
foreach ($dir in Get-ChildItem 'publish' -Directory) {
    $total = (Get-ChildItem $dir.FullName -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("  publish/{0}/  ({1:N0} MB total)" -f $dir.Name, [math]::Round($total / 1MB, 0)) -ForegroundColor Green
    foreach ($f in Get-ChildItem $dir.FullName -File | Sort-Object Name) {
        Write-Host ("      {0,-28} {1,6:N1} MB" -f $f.Name, ($f.Length / 1MB)) -ForegroundColor DarkGray
    }
}

Write-Host ''
Write-Host '  Windows sharing machine:  nscreen-server' -ForegroundColor DarkGray
Write-Host '  Any watching machine:     nscreen-client       (finds the server by itself)' -ForegroundColor DarkGray

if ($Runtime -contains 'osx-arm64') {
    Write-Host ''
    Write-Host '  On macOS, copy the whole publish/osx-arm64 folder, then once:' -ForegroundColor DarkGray
    Write-Host '      chmod +x nscreen-client' -ForegroundColor DarkGray
    Write-Host '      xattr -dr com.apple.quarantine .      # only if Gatekeeper blocks it' -ForegroundColor DarkGray
}
Write-Host ''
