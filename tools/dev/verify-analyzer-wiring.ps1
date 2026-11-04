<#
.SYNOPSIS
    Proves, by asking MSBuild, that every product project really loads the banned-API analyzer with BannedSymbols.txt.

.DESCRIPTION
    Reading project files cannot prove this: a nested Directory.Build.targets, ImportDirectoryBuildTargets=false, a conditional NoWarn
    or a reference to a project that is exempt from every check would all switch the analyzer layer off while the text stays plausible
    (stage-0 re-verification R2). So for every project outside tests/ this script evaluates the project and requires:
      - the BannedApiAnalyzers assemblies are among the resolved Analyzer items (at the version in Directory.Packages.props);
      - the repository's BannedSymbols.txt is among the AdditionalFiles;
      - TreatWarningsAsErrors is true, and neither NoWarn nor WarningsNotAsErrors mentions RS0030, and RunAnalyzers is not false.
    Every project under tests/ must evaluate with IsTestProject = true (the only way to be exempt).
    Exit code 0 = proven, 1 = not proven.
#>
[CmdletBinding()]
param([string]$Dotnet)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $Dotnet) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $Dotnet = $cmd.Source } else { $Dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
}

$packages = Get-Content (Join-Path $root 'Directory.Packages.props') -Raw
$m = [regex]::Match($packages, 'Include="Microsoft\.CodeAnalysis\.BannedApiAnalyzers"\s+Version="([^"]+)"')
if (-not $m.Success) { Write-Host 'BannedApiAnalyzers is not in Directory.Packages.props'; exit 1 }
$version = $m.Groups[1].Value
$bannedList = [System.IO.Path]::GetFullPath((Join-Path $root 'BannedSymbols.txt'))

$problems = New-Object System.Collections.Generic.List[string]
$productCount = 0
$testCount = 0

$projects = Get-ChildItem -Path $root -Filter *.csproj -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }

foreach ($project in $projects) {
    $rel = $project.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
    $text = & $Dotnet msbuild $project.FullName -nologo -p:Configuration=Release -t:ResolveLockFileAnalyzers `
        -getItem:Analyzer -getItem:AdditionalFiles -getProperty:IsTestProject -getProperty:TreatWarningsAsErrors `
        -getProperty:NoWarn -getProperty:WarningsNotAsErrors -getProperty:RunAnalyzers 2>&1 | Out-String
    try {
        $json = $text | ConvertFrom-Json
    } catch {
        $problems.Add("${rel}: MSBuild evaluation did not return JSON (restore first?): " + $text.Substring(0, [Math]::Min(200, $text.Length)))
        continue
    }

    $isTest = ($json.Properties.IsTestProject -eq 'true')
    if ($rel.StartsWith('tests/')) {
        $testCount++
        if (-not $isTest) { $problems.Add("${rel}: a project under tests/ must evaluate with IsTestProject=true") }
        continue
    }

    $productCount++
    if ($isTest) { $problems.Add("${rel}: a product project must not be a test project") }

    $analyzers = @($json.Items.Analyzer | ForEach-Object { $_.Identity })
    foreach ($dll in @('Microsoft.CodeAnalysis.BannedApiAnalyzers.dll', 'Microsoft.CodeAnalysis.CSharp.BannedApiAnalyzers.dll')) {
        $needle = ('microsoft.codeanalysis.bannedapianalyzers\' + $version + '\analyzers\').ToLowerInvariant()
        if (-not ($analyzers | Where-Object { $_.ToLowerInvariant().Contains($needle) -and $_.EndsWith($dll) })) {
            $problems.Add("${rel}: analyzer $dll $version is not among the resolved analyzers")
        }
    }

    $files = @($json.Items.AdditionalFiles | ForEach-Object { [System.IO.Path]::GetFullPath($_.FullPath) })
    if ($files -notcontains $bannedList) { $problems.Add("${rel}: BannedSymbols.txt is not among the AdditionalFiles") }

    if ($json.Properties.TreatWarningsAsErrors -ne 'true') { $problems.Add("${rel}: TreatWarningsAsErrors is '$($json.Properties.TreatWarningsAsErrors)', so RS0030 would only warn") }
    if ($json.Properties.NoWarn -match '(?i)RS0030') { $problems.Add("${rel}: NoWarn mentions RS0030") }
    if ($json.Properties.WarningsNotAsErrors -match '(?i)RS0030') { $problems.Add("${rel}: WarningsNotAsErrors mentions RS0030") }
    if ($json.Properties.RunAnalyzers -eq 'false') { $problems.Add("${rel}: RunAnalyzers is false") }
}

if ($productCount -lt 3) { $problems.Add("only $productCount product projects were found; the check would pass vacuously") }
if ($testCount -lt 2) { $problems.Add("only $testCount test projects were found") }

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    exit 1
}

Write-Host ("    analyzer wiring proven for {0} product projects ({1} test projects exempt via IsTestProject)" -f $productCount, $testCount)
exit 0
