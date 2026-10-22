param([switch]$IncludeGpuSample)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $repo 'artifacts/verification/8.2/fixture-300'
$output = Join-Path $repo 'artifacts/verification/8.4/final-performance'
$verifier = Join-Path $repo 'NovaLauncher.Verification/bin/Release/net9.0-windows/NovaLauncher.Verification.exe'
$executable = Join-Path $repo 'NovaLauncher.App/bin/Release/net9.0-windows/NovaLauncher.exe'
if (-not (Test-Path -LiteralPath (Join-Path $fixture '.nova-verification'))) { throw 'Missing isolated 300-app fixture' }
foreach ($mode in @('Default', 'CustomImage', 'Transparent')) {
    $value = switch ($mode) { 'Default' { -1 } 'CustomImage' { 12 } 'Transparent' { -2 } }
    & $verifier --performance-background $fixture $value
    if ($LASTEXITCODE -ne 0) { throw 'Cannot configure isolated fixture' }
    & "$PSScriptRoot/Test-Performance.ps1" -Label $mode -Counts 300 -OutputDirectory (Join-Path $output $mode)
    if ($IncludeGpuSample) {
        # Supplemental run: not included in timing medians. WMI observes the process GPU engines.
        $samples = @()
        $failure = $null
        $process = Start-Process -FilePath $executable -ArgumentList @("--verification-data-root=`"$fixture`"", '--performance-test') -WindowStyle Hidden -PassThru
        $null = $process.Handle
        try {
            while (-not $process.HasExited) {
                $engines = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -Filter "Name LIKE 'pid_$($process.Id)_%'" |
                    Select-Object Name, UtilizationPercentage)
                $samples += [pscustomobject]@{ ElapsedMs = [math]::Round(((Get-Date) - $process.StartTime).TotalMilliseconds); Engines = $engines }
                if (((Get-Date) - $process.StartTime).TotalSeconds -gt 60) { throw 'GPU sample process timeout' }
                Start-Sleep -Milliseconds 1000
            }
        } catch { $failure = $_.Exception.Message }
        finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id } }
        [pscustomobject]@{ Mode = $mode; Error = $failure; Samples = $samples } | ConvertTo-Json -Depth 6 |
            Set-Content -Encoding utf8 (Join-Path $output "$mode-gpu.json")
    }
}
# Leave the shared isolated performance fixture on its original stable default.
& $verifier --performance-background $fixture -1
