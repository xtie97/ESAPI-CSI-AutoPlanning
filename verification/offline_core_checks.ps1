# Small, reproducible checks of the supplied ESAPI-independent C# code.
# Run with PowerShell 7 from any directory. No patient or Eclipse connection is used.
$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $PSScriptRoot '..\CsiAutoPlan\src\CsiAutoPlan\Core'
$sourceFiles = @('RangeMarginCalculator.cs','PerturbationPlanner.cs','CsiGeometryPlanner.cs','AirGapCalculator.cs','ShoulderDetector.cs')
$sourcePaths = @($sourceFiles | ForEach-Object { Join-Path $sourceRoot $_ })
Add-Type -Path $sourcePaths
$script:passed = 0
function Assert-Check([bool]$condition, [string]$description) {
    if (-not $condition) { throw "FAIL: $description" }
    $script:passed++
    Write-Output "PASS: $description"
}
Assert-Check ([Math]::Abs([CsiAutoPlan.Core.RangeMarginCalculator]::DistalMm(125,3.5,3,0.1)-7.4) -lt 0.000001) 'Distal worked example gives 7.4 mm'
Assert-Check ([Math]::Abs([CsiAutoPlan.Core.RangeMarginCalculator]::ProximalMm(125,110,3.5,3,0.1)-3.6) -lt 0.000001) 'Proximal worked example gives 3.6 mm'
$reason = ''
Assert-Check (-not [CsiAutoPlan.Core.RangeMarginCalculator]::IsUsable(100,110,[ref]$reason)) 'SOBP greater than range is rejected'
Assert-Check (-not [CsiAutoPlan.Core.RangeMarginCalculator]::IsUsable([double]::NaN,10,[ref]$reason)) 'Missing nominal range is rejected'
Assert-Check ([CsiAutoPlan.Core.RangeMarginCalculator]::RoundUp(6.6,0.1) -eq 6.6) 'An exact tenth is not rounded up again'
$scenarios = [CsiAutoPlan.Core.PerturbationPlanner]::PlanScenarios(0.3,3.5)
Assert-Check ($scenarios.Count -eq 12) 'Default plan scenario count is 12'
Assert-Check (@($scenarios | ForEach-Object { $_ -join ',' } | Select-Object -Unique).Count -eq 12) 'All default plan scenarios are distinct'
$singleAxis = $true
foreach ($scenario in $scenarios) {
    if (@($scenario[0..2] | Where-Object { $_ -ne 0 }).Count -ne 1) { $singleAxis = $false }
}
Assert-Check $singleAxis 'Each plan scenario shifts exactly one axis'
$brainIds = [System.Collections.Generic.List[string]]@('01 RPO','02 LPO')
$posteriorIds = [System.Collections.Generic.List[string]]@('03 POST','04 POST UPPER','05 POST LOWER')
$fieldScenarios = [CsiAutoPlan.Core.PerturbationPlanner]::Build($brainIds,$posteriorIds,0.3,$true,$true,$true,$true)
Assert-Check ($fieldScenarios.Count -eq 16) 'Two obliques plus three posterior fields produce 16 field checklist scenarios'
Assert-Check ($fieldScenarios[0].Shifts.Count -eq 2) 'A paired brain scenario contains both brain fields'
$geometryInput = New-Object CsiAutoPlan.Core.GeometryInput
$geometryInput.BrainIsoZ = 0
$geometryInput.CtvTopZ = 100
$geometryInput.CtvBottomZ = -700
$geometryInput.ShoulderTopZ = -100
$geometryInput.SliceAllowanceMm = 2.5
$geometryInput.BrainMarginSupMm = 3
$geometryInput.BrainMarginInfMm = 3
$geometryInput.SpineMarginSupMm = 3
$geometryInput.SpineMarginInfMm = 3
$geometryInput.PreferredOverlapMm = 50
$geometry = [CsiAutoPlan.Core.CsiGeometryPlanner]::Plan($geometryInput)
Assert-Check $geometry.IsFeasible 'Synthetic Separate layout is feasible'
Assert-Check ($geometry.IsocenterCount -eq 3) 'Synthetic Separate layout uses three isocenters'
$chain = $geometry.Chain()
$overlapsOk = $true
for ($index = 0; $index -lt $chain.Count - 1; $index++) {
    if ($chain[$index+1].PbstvHiZ - $chain[$index].PbstvLoZ -lt 20) { $overlapsOk = $false }
}
Assert-Check $overlapsOk 'Every planned junction overlaps by at least 20 mm'
$usableHalf = 200 - 5 - 5 - 2.5
$withinLimits = $true
foreach ($segment in $geometry.All()) {
    if ($segment.DistSupEdgeMm -gt $usableHalf -or $segment.DistInfEdgeMm -gt $usableHalf) { $withinLimits = $false }
}
Assert-Check $withinLimits 'Synthetic field targets fit the reserved scanning limits'
$impossible = $geometryInput.Clone()
$impossible.CtvTopZ = 500
Assert-Check (-not [CsiAutoPlan.Core.CsiGeometryPlanner]::Plan($impossible).IsFeasible) 'Target beyond brain superior scanning limit is rejected'
Assert-Check ([CsiAutoPlan.Core.AirGapCalculator]::SnoutPositionMm(100,50,65) -eq 215) 'Snout position adds body depth, requested gap and room offset'
$outlines = [System.Collections.Generic.List[CsiAutoPlan.Core.Contour]]::new()
$outlines.Add([CsiAutoPlan.Core.Contour]::new(0,[double[]]@(-50,50,50,-50),[double[]]@(-20,-20,80,80)))
$depth = [CsiAutoPlan.Core.AirGapCalculator]::MaxDepthTowardSource($outlines,0,0,0,180,185,225,2)
Assert-Check ([Math]::Abs($depth-80) -lt 0.000001) 'Posterior beam picks posterior outline depth in a synthetic rectangle'
$profile = [System.Collections.Generic.List[CsiAutoPlan.Core.WidthSample]]::new()
foreach ($pair in @(@(0,150),@(-10,100),@(-20,110),@(-30,140),@(-40,180),@(-50,200))) {
    $profile.Add([CsiAutoPlan.Core.WidthSample]::new($pair[0],$pair[1]))
}
$shoulder = [CsiAutoPlan.Core.ShoulderDetector]::Detect($profile,0,200,0.3,1.4)
Assert-Check ($shoulder.Success -and $shoulder.ShoulderTopZ -eq -30) 'Synthetic shoulder is the first inferior slice above the 30 percent rise threshold'
Write-Output "Completed $script:passed offline checks. These checks do not validate ESAPI integration or clinical plan quality."
