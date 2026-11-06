# The experiment plan, as the proposal orders it, in the gap before the nightly cleanup (about 04:43): start it
# around noon and every run stops by itself at --until. Each step writes its own CSV and summary to bench-results.
#
#   pwsh -File tools/Keypuncher.Sandbox/Run-Bench.ps1 -Step baseline     # qwen3:4b and qwen3:1.7b, all sets, 3 runs (~2 h)
#   pwsh -File tools/Keypuncher.Sandbox/Run-Bench.ps1 -Step prompts      # A1, A2, A4, A8, best on qwen3:4b (~4 h)
#   pwsh -File tools/Keypuncher.Sandbox/Run-Bench.ps1 -Step models       # baseline and A2 on every pulled candidate, picks judged by qwen3:4b (overnight)
#   pwsh -File tools/Keypuncher.Sandbox/Run-Bench.ps1 -Step think        # checker with thinking on, two models (~1 h)
#
# Models that are not pulled are skipped with a note; pull them first:
#   ollama pull gemma3:4b; ollama pull qwen3:8b; ollama pull llama3.2:3b; ollama pull phi4-mini
param(
    [ValidateSet('baseline', 'prompts', 'models', 'think', 'smoke')] [string] $Step = 'smoke',
    [string] $Until = '04:15',
    [string] $Out = 'bench-results'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Push-Location $root
try {
    dotnet build tools/Keypuncher.Sandbox -c Release | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
    $bench = {
        param($more)
        dotnet run --project tools/Keypuncher.Sandbox -c Release --no-build -- bench --out $Out --until $Until @more
        if ($LASTEXITCODE -eq 2) { throw "bench did not start: another job has Ollama (see above)" }
        if ($LASTEXITCODE -ne 0) { throw "bench exited with $LASTEXITCODE" }
    }
    switch ($Step) {
        'smoke'    { & $bench @('--models', 'qwen3:4b', '--variants', 'baseline', '--sets', 'checker', '--runs', '1') }
        'baseline' { & $bench @('--models', 'qwen3:4b,qwen3:1.7b', '--variants', 'baseline', '--runs', '3') }
        'prompts'  { & $bench @('--models', 'qwen3:4b', '--variants', 'A1,A2,A4,A8,best', '--runs', '3') }
        'models'   { & $bench @('--models', 'gemma3:4b,llama3.1:8b,qwen3:8b,llama3.2:3b,phi4-mini,gemma3:1b', '--variants', 'baseline,A2', '--runs', '2', '--judge', 'qwen3:4b') }
        'think'    { & $bench @('--models', 'qwen3:4b,qwen3:8b', '--variants', 'A2', '--sets', 'checker,habitat', '--runs', '2', '--think') }
    }
}
finally { Pop-Location }
