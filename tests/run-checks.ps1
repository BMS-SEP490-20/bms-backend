param(
    [string]$TargetUC = ""
)

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "BMS BACKEND - AUTOMATED STANDARDS & SECURITY VERIFICATION SUITE" -ForegroundColor Cyan
Write-Host "Standards: ISO 15189 | ISO 20658 | ISO 27001 | ISO 27789 | ISO 25010 | OWASP ASVS" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan

$testProjects = Get-ChildItem -Path "$PSScriptRoot" -Directory -Filter "BMS.*.Checks" | Sort-Object Name

if (-not [string]::IsNullOrWhiteSpace($TargetUC)) {
    $normalized = $TargetUC.Trim().ToUpper()
    $testProjects = $testProjects | Where-Object { $_.Name.ToUpper().Contains($normalized) }
    if ($testProjects.Count -eq 0) {
        Write-Host "No test project found matching '$TargetUC'." -ForegroundColor Red
        exit 1
    }
}

$results = @()
$totalPassed = 0
$totalFailed = 0

foreach ($proj in $testProjects) {
    Write-Host "`n--------------------------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "RUNNING: $($proj.Name)" -ForegroundColor Yellow
    Write-Host "--------------------------------------------------------------------------------" -ForegroundColor Yellow

    $startTime = Get-Date
    dotnet run --project "$($proj.FullName)"
    $exitCode = $LASTEXITCODE
    $duration = [Math]::Round(((Get-Date) - $startTime).TotalSeconds, 2)

    if ($exitCode -eq 0) {
        $results += [PSCustomObject]@{
            Suite = $proj.Name
            Status = "PASS"
            DurationSec = $duration
        }
        $totalPassed++
    } else {
        $results += [PSCustomObject]@{
            Suite = $proj.Name
            Status = "FAIL"
            DurationSec = $duration
        }
        $totalFailed++
    }
}

Write-Host "`n================================================================================" -ForegroundColor Cyan
Write-Host "TEST SUITE SUMMARY" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan

$results | Format-Table -AutoSize

Write-Host "Total: $($totalPassed + $totalFailed) suites | Passed: $totalPassed | Failed: $totalFailed" -ForegroundColor $(if ($totalFailed -eq 0) { "Green" } else { "Red" })
Write-Host "================================================================================" -ForegroundColor Cyan

if ($totalFailed -gt 0) {
    exit 1
}
