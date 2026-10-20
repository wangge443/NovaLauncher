param(
    [Parameter(Mandatory)][string]$Label,
    [string]$Executable = "$PSScriptRoot\..\NovaLauncher.App\bin\Release\net9.0-windows\NovaLauncher.exe",
    [int[]]$Counts = @(10, 100, 300)
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$output = Join-Path $repo "artifacts\verification\8.2\$Label"
New-Item -ItemType Directory -Path $output -Force | Out-Null
$verifier = Join-Path $repo 'NovaLauncher.Verification\bin\Release\net9.0-windows\NovaLauncher.Verification.exe'
foreach ($count in $Counts) {
    $root = Join-Path $repo "artifacts\verification\8.2\fixture-$count"
    if (-not (Test-Path -LiteralPath $root)) {
        & $verifier --performance-seed $root $count
        if ($LASTEXITCODE -ne 0) { throw 'Fixture failed' }
    }
    for ($run = 0; $run -le 5; $run++) {
        $report = Join-Path $root 'performance.json'
        if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
        $process = Start-Process -FilePath $Executable -ArgumentList @("--verification-data-root=`"$root`"", '--performance-test') -WindowStyle Hidden -PassThru
        $null = $process.Handle
        if (-not $process.WaitForExit(60000)) {
            Stop-Process -Id $process.Id
            throw "Performance process timeout: $count/$run"
        }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw "Performance process failed: $count/$run" }
        Copy-Item -LiteralPath $report -Destination (Join-Path $output "$count-$run.json")
        if ($run -eq 5) {
            Copy-Item -LiteralPath (Join-Path $root 'first.png') -Destination (Join-Path $output "$count-first.png")
            Copy-Item -LiteralPath (Join-Path $root 'full.png') -Destination (Join-Path $output "$count-full.png")
        }
        $result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
        Write-Output "$Label count=$count run=$run layout=$([math]::Round($result.Timings.HomeFirstLayout)) ready=$([math]::Round($result.Timings.StartupReady)) cards=$($result.CardCount)"
    }
}
