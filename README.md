# Keypuncher

A toy for an infant. Banging on the keyboard makes a persistent, slowly evolving landscape of
shapes and sounds; every letter key shows and says its letter (by sound) and a word, in Icelandic,
English or Bulgarian. A local LLM, running in Ollama on the family's Windows PCs, invents what the
creatures, plants and sounds look and sound like.

The full architecture and tech plan is in the project's
[plan document](https://claude.ai/artifact/HwtkxyHQZXKyvUUGjEkrNn). In short:

- **Reflex loop** (under 50 ms): a key press spawns something from the current palette and plays a
  note. No model runs here.
- **Director loop** (every 10 to 30 s): the LLM returns a JSON *recipe* (shape, colour, motion,
  synth patch) constrained to a schema. The app renders recipes with SkiaSharp and plays them with
  its own synth, so the model "draws" and "composes" without a diffusion model in the way.
- **Asset loop** (minutes, desktop only): SD-Turbo backgrounds and Stable Audio Open Small loops,
  generated ahead of time and cached.

## Layout

| Project | Purpose |
| --- | --- |
| `src/Keypuncher.Core` | World model, recipes, input events |
| `src/Keypuncher.Language` | Icelandic alphabet, Bulgarian phonetic key map, parent-approved concept list |
| `src/Keypuncher.Director` | Recipe JSON schema, validator, prompts |
| `src/Keypuncher.Inference` | `ILanguageModel`, `IImageGenerator`, `IAudioGenerator`, `ISpeech` |
| `src/Keypuncher.Inference.Ollama` | `ILanguageModel` over a local Ollama server with structured output |
| `src/Keypuncher.Rendering`, `.Audio`, `.Storage` | Empty until Phase 1 |
| `tools/Keypuncher.Bench` | Phase 0: tokens/s and valid-recipe rate per model on this PC |
| `tools/Keypuncher.Sandbox` | Ask the director for one recipe and print it |
| `tools/Keypuncher.KeySpike` | Phase 0: Windows low-level keyboard hook + Icelandic layout reading (Windows only) |
| `tests/` | xUnit tests for Core, Language and Director |

The MAUI app (`Keypuncher.App`) arrives in Phase 1 once the Phase 0 spikes pass.

## Phase 0 on the desktop

Needs the .NET 10 SDK, [Ollama](https://ollama.com) and the Icelandic keyboard layout installed.

```powershell
ollama pull gemma3:4b
ollama pull qwen3:4b
ollama pull llama3.1:8b          # tight on 6 GB; worth measuring

dotnet test
dotnet run --project tools/Keypuncher.Bench -- --models gemma3:4b,qwen3:4b,llama3.1:8b --runs 5
dotnet run --project tools/Keypuncher.Sandbox -- gemma3:4b "a round sleepy cat"
dotnet run --project tools/Keypuncher.KeySpike
```

The bench writes a Markdown table to `bench-results/`; commit it so the numbers live with the code.

Phase 0 gates, from the plan:

- [ ] The desktop returns a valid recipe in under 5 s median (`Keypuncher.Bench`).
- [ ] Every key on the Icelandic layout reports the right character, including Ð Þ Æ Ö and the
      accent dead key, and Win / Alt+Tab / Alt+F4 are swallowed (`Keypuncher.KeySpike`).
- [ ] SD-Turbo timing on the GTX 1060 (not yet scripted; see the plan).
- [ ] Piper's Icelandic voice (Salka) runs offline; check whether Windows offers a Bulgarian voice.
