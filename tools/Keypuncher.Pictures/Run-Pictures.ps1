# Pictures of real things, outside play. Needs no Ollama and no graphics card, so it can run at any time.
#
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step model      # download the two cut-out models (about 400 MB) once
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step dryrun     # every real thing in world.db and the bench sets, into pictures-dryrun, with a report
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step fetch      # fetch what the toy wants into its own pictures folder
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step wanted     # list what the toy wants and why
param(
    [ValidateSet('model', 'dryrun', 'fetch', 'wanted')] [string] $Step = 'wanted',
    [string] $Until = '',
    [string] $Data = ''
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Push-Location $root
try {
    dotnet build tools/Keypuncher.Pictures -c Release | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
    $more = @()
    if ($Until) { $more += @('--until', $Until) }
    if ($Data) { $more += @('--data', $Data) }
    dotnet run --project tools/Keypuncher.Pictures -c Release --no-build -- $Step @more
    if ($LASTEXITCODE -ne 0) { throw "pictures exited with $LASTEXITCODE" }
}
finally { Pop-Location }
