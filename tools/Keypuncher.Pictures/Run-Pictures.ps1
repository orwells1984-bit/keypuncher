# Pictures of real things, outside play. model, dryrun, fetch and wanted need no Ollama and no graphics card, so they
# can run at any time; generate, check and nightly take the card and refuse to start while the toy runs.
#
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step model      # download the two cut-out models (about 400 MB) once
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step dryrun     # every real thing in world.db and the bench sets, into pictures-dryrun, with a report
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step fetch      # fetch what the toy wants into its own pictures folder
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step wanted     # list what the toy wants and why
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step generate   # draw the gaps with Z-Image-Turbo (sd-cli under -Sd DIR or KEYPUNCHER_SD)
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step check      # show unchecked pictures to gemma3; flags, never hides
#   pwsh -File tools/Keypuncher.Pictures/Run-Pictures.ps1 -Step nightly -Until 02:45   # fetch, generate, check; stops before the 03:00 encodes
param(
    [ValidateSet('model', 'dryrun', 'fetch', 'wanted', 'generate', 'check', 'nightly')] [string] $Step = 'wanted',
    [string] $Until = '',
    [string] $Data = '',
    [string] $Sd = ''
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
    if ($Sd) { $more += @('--sd', $Sd) }
    dotnet run --project tools/Keypuncher.Pictures -c Release --no-build -- $Step @more
    if ($LASTEXITCODE -ne 0) { throw "pictures exited with $LASTEXITCODE" }
}
finally { Pop-Location }
