[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$generatedRoot = Join-Path $projectRoot 'Generated\Luban'
New-Item -ItemType Directory -Path $generatedRoot -Force | Out-Null

Push-Location $projectRoot
try {
    $arguments = @(
        'Tools/Luban/Luban.dll', '--conf', 'luban.conf', '-t', 'all',
        '-c', 'cs-bin', '-d', 'bin', '-d', 'json',
        '--strict', '--locale', 'zh', '--errorFormat', 'json',
        '-x', 'codeStyle=csharp-default',
        '-x', 'cs-bin.outputCodeDir=Generated/Luban/csharp',
        '-x', 'bin.outputDataDir=Generated/Luban/bin',
        '-x', 'json.outputDataDir=Generated/Luban/json'
    )
    & dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $generatedRoot 'generation.log')
    if ($LASTEXITCODE -ne 0) {
        throw "Luban failed with exit code $LASTEXITCODE. See Generated/Luban/generation.log."
    }

    [xml]$definitions = Get-Content -LiteralPath 'Luban/Defines/tables.xml' -Raw -Encoding UTF8
    $tables = @($definitions.module.table)
    $results = foreach ($table in $tables) {
        $fileStem = $table.name.ToLowerInvariant()
        $jsonPath = Join-Path $generatedRoot "json/$fileStem.json"
        $binaryPath = Join-Path $generatedRoot "bin/$fileStem.bytes"
        if (!(Test-Path -LiteralPath $jsonPath) -or !(Test-Path -LiteralPath $binaryPath)) {
            throw "Missing generated data for $($table.name)."
        }
        $rows = @(Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json)
        [pscustomobject]@{
            table = $table.name
            input = $table.input
            rows = $rows.Count
            jsonSha256 = (Get-FileHash -LiteralPath $jsonPath -Algorithm SHA256).Hash.ToLowerInvariant()
            binarySha256 = (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $jsonFiles = @(Get-ChildItem -LiteralPath (Join-Path $generatedRoot 'json') -Filter '*.json' -File)
    $binaryFiles = @(Get-ChildItem -LiteralPath (Join-Path $generatedRoot 'bin') -Filter '*.bytes' -File)
    if ($jsonFiles.Count -ne $tables.Count -or $binaryFiles.Count -ne $tables.Count) {
        throw 'Generated file count differs from registered table count.'
    }
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $generatedRoot 'table-summary.json') -Encoding UTF8
    Write-Host "Luban validation passed: $($tables.Count) tables. Output: $generatedRoot"
}
finally {
    Pop-Location
}
