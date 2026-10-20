[CmdletBinding()]
param(
    [ValidateSet('All', 'FrameworkDependent', 'SelfContained', 'SingleFile')]
    [string]$Mode = 'All',
    [switch]$IncludeSingleFile
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/publish'))
$project = Join-Path $root 'NovaLauncher.App/NovaLauncher.App.csproj'

function Assert-OwnedPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change a path outside publish outputs: $full"
    }
    # Refuse junctions/symlinks in the output or its parents before deletion or writes.
    $candidate = $full
    while ($candidate -and $candidate.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $candidate) {
            if ((Get-Item -LiteralPath $candidate -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing a reparse point: $candidate"
            }
        }
        $candidate = [IO.Path]::GetDirectoryName($candidate)
    }
    return $full
}

function Reset-OutputDirectory([string]$Path) {
    $full = Assert-OwnedPath $Path
    if (Test-Path -LiteralPath $full) {
        $links = @(Get-ChildItem -LiteralPath $full -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
        if ($links.Count) { throw "Refusing to recursively delete output containing reparse points: $full" }
        Remove-Item -LiteralPath $full -Recurse -Force
    }
    [IO.Directory]::CreateDirectory($full) | Out-Null
}

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}

function Assert-CleanPackage([string]$Directory) {
    $forbidden = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force | Where-Object {
        $relative = $_.FullName.Substring($Directory.Length).TrimStart('\', '/')
        $relative -match '(^|[\\/])(Profile|Background|CustomIcons|Cache|Logs|docs|TestResults|obj|bin)([\\/]|$)' -or
        $_.Name -match '\.(db($|-wal$|-shm$)|sqlite($|-wal$|-shm$)|pdb$|log$|cs$|xaml$|ps1$|sln$|csproj$|ttf$|otf$)' -or
        $_.Name -match 'NovaLauncher\.(Verification|LaunchProbe)' -or
        $_.Name -match '^appsettings.*\.json$' -or
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
    })
    if ($forbidden.Count) { throw "Publish contains forbidden data/development files: $($forbidden.FullName -join ', ')" }
    foreach ($required in @('NovaLauncher.exe', 'ThirdParty/ToolGood.Words.Pinyin.LICENSE.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $required) -PathType Leaf)) { throw "Missing publish file: $required" }
    }
}

Push-Location $root
try {
    # RuntimeIdentifiers in the project makes the committed lock cover win-x64 as well.
    Invoke-DotNet @('restore', $project, '--locked-mode', '-r', 'win-x64', '-p:SelfContained=true')
    $targets = @()
    if ($Mode -in @('All', 'FrameworkDependent')) { $targets += @{ Name = 'framework-dependent'; SelfContained = 'false'; Single = $false } }
    if ($Mode -in @('All', 'SelfContained')) { $targets += @{ Name = 'self-contained'; SelfContained = 'true'; Single = $false } }
    if ($Mode -eq 'SingleFile' -or ($Mode -eq 'All' -and $IncludeSingleFile)) { $targets += @{ Name = 'single-file'; SelfContained = 'true'; Single = $true } }
    foreach ($target in $targets) {
        $output = Assert-OwnedPath (Join-Path $publishRoot $target.Name)
        Reset-OutputDirectory $output
        $arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--no-restore', '--self-contained', $target.SelfContained,
            '-warnaserror', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:PublishTrimmed=false', '-o', $output)
        if ($target.Single) { $arguments += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true') }
        else { $arguments += '-p:PublishSingleFile=false' }
        Invoke-DotNet $arguments
        Assert-CleanPackage $output
        $identity = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output 'NovaLauncher.exe'))
        $version = ($identity.ProductVersion -split '\+')[0]
        if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$' -or $identity.ProductName -ne 'Nova Launcher') {
            throw 'Publish executable does not have the expected product/version metadata.'
        }
        $files = @(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
            [ordered]@{ path = $_.FullName.Substring($output.Length + 1).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
        $manifest = [ordered]@{
            product = $identity.ProductName; version = $version; informationalVersion = $identity.ProductVersion
            fileVersion = $identity.FileVersion; runtimeIdentifier = 'win-x64'; configuration = 'Release'
            distribution = $target.Name; selfContained = ($target.SelfContained -eq 'true'); singleFile = $target.Single
            files = $files
        }
        [IO.File]::WriteAllText((Join-Path $output 'release-manifest.json'), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
        Assert-CleanPackage $output
        $archive = Assert-OwnedPath (Join-Path $publishRoot "NovaLauncher-$version-win-x64-$($target.Name).zip")
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($output, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
        Write-Host "Published $($target.Name): $archive"
    }
} finally { Pop-Location }
