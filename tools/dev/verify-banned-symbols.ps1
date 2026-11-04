<#
.SYNOPSIS
    Proves that every entry of BannedSymbols.txt really stops a build (and that allowed APIs are not caught).

.DESCRIPTION
    The banned-API analyzer silently ignores a METHOD entry whose parameter list does not match, so "the build is green" proves
    nothing. This script compiles tools/dev/analyzer-coverage/Coverage.cs.txt in a throw-away project OUTSIDE the repository, with
    the same package version and BannedSymbols.txt, and checks:
      1. every "// EXPECT <doc id>" line raises RS0030;
      2. every "// OK" line does not;
      3. the set of doc ids in the EXPECT markers equals the set of entries in BannedSymbols.txt (none missing, none unknown).
    Exit code 0 = proven, 1 = not proven. Needs only the NuGet package cache (restore may use the network if it is empty).
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

$bannedFile = Join-Path $root 'BannedSymbols.txt'
$coverageFile = Join-Path $PSScriptRoot 'analyzer-coverage\Coverage.cs.txt'
$packages = Get-Content (Join-Path $root 'Directory.Packages.props') -Raw
$m = [regex]::Match($packages, 'Include="Microsoft\.CodeAnalysis\.BannedApiAnalyzers"\s+Version="([^"]+)"')
if (-not $m.Success) { Write-Host 'BannedApiAnalyzers is not in Directory.Packages.props'; exit 1 }
$version = $m.Groups[1].Value
$mm = [regex]::Match($packages, 'Include="System\.Management"\s+Version="([^"]+)"')
if (-not $mm.Success) { Write-Host 'System.Management is not in Directory.Packages.props'; exit 1 }
$mgmt = $mm.Groups[1].Value

# Entries of BannedSymbols.txt: "<doc id>;<message>", comments start with //.
$entries = @(Get-Content $bannedFile | Where-Object { $_ -and -not $_.TrimStart().StartsWith('//') } | ForEach-Object { ($_ -split ';', 2)[0].Trim() })

$lines = Get-Content $coverageFile
$expect = @{}   # line number -> doc id
$ok = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    $e = [regex]::Match($lines[$i], '//\s*EXPECT\s+(\S+)\s*$')
    if ($e.Success) { $expect[$i + 1] = $e.Groups[1].Value }
    elseif ($lines[$i] -match '//\s*OK\s*$') { $ok += ($i + 1) }
}

$problems = New-Object System.Collections.Generic.List[string]
$declared = @($expect.Values | Sort-Object -Unique)
foreach ($id in $entries) { if ($declared -notcontains $id) { $problems.Add("BannedSymbols.txt entry has no EXPECT proof line: $id") } }
foreach ($id in $declared) { if ($entries -notcontains $id) { $problems.Add("EXPECT marker names an id that is not in BannedSymbols.txt: $id") } }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('jankscope-bancheck-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Copy-Item $bannedFile (Join-Path $work 'BannedSymbols.txt')
    Copy-Item $coverageFile (Join-Path $work 'Coverage.cs')
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <OutputType>Library</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="$version" PrivateAssets="all" />
    <PackageReference Include="System.Management" Version="$mgmt" />
    <AdditionalFiles Include="BannedSymbols.txt" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path (Join-Path $work 'Probe.csproj') -Encoding ASCII

    Push-Location $work
    try {
        $out = & $Dotnet build Probe.csproj -c Release --nologo 2>&1 | Out-String
    } finally {
        Pop-Location
    }

    if ($out -match 'error (CS|MSB|NU)\d+') {
        $problems.Add('The coverage project did not compile, so nothing was proven:')
        ([regex]::Matches($out, '[^\r\n]*error (CS|MSB|NU)\d+[^\r\n]*') | Select-Object -First 5) | ForEach-Object { $problems.Add('    ' + $_.Value.Trim()) }
    } else {
        $flagged = @{}   # line number -> symbols named by the RS0030 diagnostics on that line
        foreach ($hit in [regex]::Matches($out, 'Coverage\.cs\((\d+),\d+\):\s+(?:warning|error)\s+RS0030:\s+The symbol ''([^'']+)''')) {
            $n = [int]$hit.Groups[1].Value
            if (-not $flagged.ContainsKey($n)) { $flagged[$n] = New-Object System.Collections.Generic.List[string] }
            $flagged[$n].Add($hit.Groups[2].Value)
        }
        foreach ($n in ($expect.Keys | Sort-Object)) {
            if (-not $flagged.ContainsKey([int]$n)) { $problems.Add("Line $n is NOT banned although BannedSymbols.txt should stop it: $($expect[$n])"); continue }
            # The diagnostic must come from the entry the line names (not from some other, broader entry): compare the symbol it reports.
            $id = $expect[$n]
            $body = ($id.Substring(2) -replace '\(.*$', '') -replace '``\d+$', ''
            $parts = $body.Split('.')
            if ($id.StartsWith('T:')) { $want = $parts[$parts.Length - 1] } else { $want = $parts[$parts.Length - 2] + '.' + $parts[$parts.Length - 1] }
            if (-not ($flagged[[int]$n] | Where-Object { $_.Contains($want) })) { $problems.Add("Line $n is banned, but not by the entry it names ($id); the diagnostics name: $($flagged[[int]$n] -join ' | ')") }
        }
        foreach ($n in $ok) {
            if ($flagged.ContainsKey([int]$n)) { $problems.Add("Line $n is banned but must stay allowed: $($lines[$n - 1].Trim())") }
        }
        Write-Host ("    {0} banned entries, {1} proof lines flagged, {2} allowed controls clean (analyzer {3})" -f $entries.Count, $flagged.Count, $ok.Count, $version)
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    exit 1
}

Write-Host '    banned-symbol coverage proven'
exit 0
