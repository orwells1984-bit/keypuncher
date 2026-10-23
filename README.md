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
| `src/Keypuncher.App` | The .NET MAUI app (Windows): play page, grown-ups' page, recording page |
| `src/Keypuncher.Toy` | The reflex loop: what every key, mash and tap does; speech lookup; grown-up exit |
| `src/Keypuncher.Core` | World model, recipes, input events, keyboard geometry, mash grouping, built-in recipes |
| `src/Keypuncher.Language` | Icelandic alphabet, Bulgarian phonetic key map, accent key, letter/number meanings, concept list, recording list |
| `src/Keypuncher.Rendering` | SkiaSharp scene and recipe shapes; embeds the Andika font |
| `src/Keypuncher.Audio` | Synth and mixer with volume cap and limiter, WAV files, recording clean-up |
| `src/Keypuncher.Storage` | The world in SQLite, recordings as WAV files, settings |
| `src/Keypuncher.Platform.Windows` | Keyboard hook (Icelandic layout forced), WASAPI output, microphone, Windows voices |
| `src/Keypuncher.Director` | Recipe JSON schema, validator, prompts (Phase 2) |
| `src/Keypuncher.Inference` | `ILanguageModel`, `IImageGenerator`, `IAudioGenerator`, `ISpeech` |
| `src/Keypuncher.Inference.Ollama` | `ILanguageModel` over a local Ollama server with structured output |
| `tools/Keypuncher.Preview` | Plays a scripted session with no window and writes PNG frames and a WAV |
| `tools/Keypuncher.Bench` | Phase 0: tokens/s and valid-recipe rate per model on this PC |
| `tools/Keypuncher.Sandbox` | Ask the director for one recipe and print it |
| `tools/Keypuncher.KeySpike` | Phase 0: Windows low-level keyboard hook + Icelandic layout reading (Windows only) |
| `tests/` | xUnit tests; everything except the app and the Windows pieces also runs on Linux |

## Phase 1: the toy, no model yet

Needs the .NET 10 SDK with the MAUI workload (`dotnet workload install maui-windows`).

```powershell
dotnet test
dotnet run --project src/Keypuncher.App
```

- Every key answers at once: a creature or plant from that key's family (the same key always makes
  the same family), a note whose pitch rises from left to right, and for letters and digits the
  glyph, large. Three or more keys at once make one big burst instead.
- Letters are said by their sound, in one language per session: Icelandic most sessions, English or
  Bulgarian sometimes (Bulgarian through the phonetic key map). Letters show in small letters unless
  the grown-ups' page says capitals. If the child pauses, a word starting with that letter
  follows, with its picture. Digits make that many things and count them aloud.
- The accent key sparkles on its own and accents the next vowel (´ then a gives Á).
- Clicks and taps work too; three fingers make a burst.
- When the play time is up, the goodnight waits for the child to pause (6 s without a press, at most
  3 minutes) and then puts things to sleep.
- **Grown-up exit:** Ctrl+Shift+Q, or holding Esc for 3 seconds, shows a short word at the top left;
  type it within 20 seconds to reach the grown-ups' page (languages, capitals, volume, recordings, fresh world, quit).
- **Recording:** grown-ups' page, *Record voices*. Space starts and stops; each clip is trimmed,
  levelled, saved, played back, and the next one comes up. Left arrow redoes the last one.
  Until a clip is recorded the toy stays quiet for letters, and uses a Windows voice for numbers and
  words if one is installed for that language.
- Data lives in `%LOCALAPPDATA%\Keypuncher` (`world.db`, `recordings\`, `settings.json`). Set
  `KEYPUNCHER_DATA` to another folder to share recordings between the child's account and yours.

To look at the toy without Windows: `dotnet run --project tools/Keypuncher.Preview -- --out preview --keys "sól 3 köttur"`.

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
