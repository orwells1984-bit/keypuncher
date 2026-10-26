# Keypuncher

A toy for an infant. Banging on the keyboard makes a persistent, slowly evolving landscape of
shapes and sounds; every letter key shows and says its letter (by sound) and a word, in Icelandic,
English or Bulgarian. A local LLM, running in Ollama on the family's Windows PCs, slowly turns the keys'
invented shapes into real animals, extinct animals, plants, vehicles and objects, and chooses how they
look and sound.

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
| `src/Keypuncher.Core` | World model, recipes, input events, keyboard geometry, mash grouping, built-in recipes, calm levels and art styles |
| `src/Keypuncher.Language` | Icelandic alphabet, Bulgarian phonetic key map, accent key, letter/number meanings, concept list, recording list |
| `src/Keypuncher.Rendering` | SkiaSharp scene and recipe shapes; embeds the Andika font |
| `src/Keypuncher.Audio` | Synth and mixer with volume cap and limiter, WAV files, recording clean-up |
| `src/Keypuncher.Storage` | The world in SQLite, recordings as WAV files, settings |
| `src/Keypuncher.Platform.Windows` | Keyboard hook (Icelandic layout forced), WASAPI output, microphone, Windows voices |
| `src/Keypuncher.Director` | The director: real-things prompts, schema, rules, fact checker, loop |
| `src/Keypuncher.Inference` | `ILanguageModel`, `IImageGenerator`, `IAudioGenerator`, `ISpeech` |
| `src/Keypuncher.Inference.Ollama` | `ILanguageModel` over a local Ollama server with structured output |
| `tools/Keypuncher.Preview` | Plays a scripted session with no window and writes PNG frames and a WAV |
| `tools/Keypuncher.Bench` | Phase 0: tokens/s and valid-recipe rate per model on this PC |
| `tools/Keypuncher.Sandbox` | Run the director against Ollama: a round of picks, or a score for the fact checker |
| `tools/Keypuncher.KeySpike` | Phase 0: Windows low-level keyboard hook + Icelandic layout reading (Windows only) |
| `tests/` | xUnit tests; everything except the app and the Windows pieces also runs on Linux |

## Phase 1: the toy, no model yet

Needs the .NET 10 SDK with the MAUI workload (`dotnet workload install maui-windows`).

```powershell
dotnet test
dotnet run --project src/Keypuncher.App
```

In VS Code, open the repo folder, install the recommended C# Dev Kit extension, and press F5
(*Keypuncher (Windows)*): it builds the app and starts it under the debugger. Ctrl+Shift+B builds,
and *Tasks: Run Test Task* runs the tests. The toy covers the screen and VS Code can't be reached
while it is in front; use the grown-up exit below to get back.

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
  3 minutes) and then puts things to sleep. Only play counts: time more than 20 s after the last press or
  tap does not, so a toy left running stays awake. After a goodnight, a rest as long as the play time
  makes the next press start a new session.
- **Calm level** (grown-ups' page): Baby, Toddler or Big toddler sets how many things stay on screen
  and move, and how they are drawn. Younger levels draw things bigger, with thicker dark outlines and
  fewer, fully saturated colours, because an infant's acuity and contrast sensitivity are a fraction
  of an adult's; see `CalmLimits` and `ArtStyle` in `Keypuncher.Core`.
- **Grown-up exit:** Ctrl+Shift+Q, or holding Esc for 3 seconds (a small ring at the top left fills as you hold), shows a short word at the top left;
  type it within 20 seconds to reach the grown-ups' page (languages, capitals, volume, calm level, recordings, fresh world, quit).
  On a touch screen, hold one finger in each of two opposite corners for 3 seconds, with nothing else touching (the same ring
  fills); six shapes appear in a row and the top left names four of them. Tap those four in that order within 20 seconds.
  A palm or a child lying on the screen is too many touches, a slap is too short, a tap on any other shape ends the
  challenge, and the shapes move every time, so copying where a grown-up tapped does not work. Taps keep playing meanwhile.
- **Recording:** grown-ups' page, *Record voices*. Space starts and stops; each clip is trimmed,
  levelled, saved, played back, and the next one comes up. Left arrow redoes the last one.
  Until a clip is recorded the toy stays quiet for letters, and uses a Windows voice for numbers and
  words if one is installed for that language.
- The screen stays on during play, so a pause or the goodnight never ends at a dark screen or the sign-in;
  the grown-ups' page and quitting hand it back to the power plan.
- Data lives in `%LOCALAPPDATA%\Keypuncher` (`world.db`, `recordings\`, `settings.json`). Set
  `KEYPUNCHER_DATA` to another folder to share recordings between the child's account and yours.

To look at the toy without Windows: `dotnet run --project tools/Keypuncher.Preview -- --out preview --keys "sól 3 köttur"`. Add `--calls` to write every animal noise as a WAV instead.

## Phase 2: the director

With [Ollama](https://ollama.com) running and `ollama pull qwen3:4b` done, the toy's director keeps the
model busy with two jobs, one request at a time. Every 2 / 1 / 0.5 minutes of play (Baby / Toddler / Big
toddler), starting 30 s in, it turns one key family into a real thing. Every 3 / 2 / 1.5 minutes it moves
the scene a step (see below), the first a minute into play. Every request and what came of it is written
to `director.log` in the data folder, as well as listed on the grown-ups' page. **Test mode** (a switch on the grown-ups' page) runs both jobs back to back as fast as the model answers, shows each new thing at once, writes the latest choice at the top of the screen, fades scenes in over 3 s and skips the goodnight. The twelve invented families at the front of the palette
(puffbird, petalpop and so on) go first, one by one; a family keeps its kind, so a plant key stays a plant
key, and the real animals after them never change. A thing that really grows into something (tadpole,
caterpillar, acorn) later becomes it. Without Ollama nothing changes and the toy plays as before.

Only real things are allowed, and that is held in three layers:

1. **Prompt and schema.** Each request names one category (animal, extinct animal, plant, vehicle,
   object) and a place drawn at random (a pond, the Ice Age, a building site). The schema allows only that
   category and the shapes it could be. The model gives an ordinary English name and one fact before it draws.
2. **Rules** (`RealThingRules`): story words (dragon, unicorn, fairy, monster and so on, with real
   exceptions such as komodo dragon and gila monster), group words (dinosaur, flower), names that are not plain English, a shape that cannot
   be the thing (a dinosaur-shaped bus, a T. rex-shaped crocodile), a plant in a creature family, and
   repeats are refused. Names with their own drawing (cow, woolly mammoth) always get it. Faces only go on
   animals.
3. **Fact check** (`FactChecker`): a second, cold call that sees only the name and life-stage claim, says
   whether it is real ("unsure" is a no) and names its kind itself (alive today, extinct, plant, vehicle,
   object), which must match the pick, so a puffin offered as an extinct animal is refused. It is the same
   small model, so the check is not independent; the grown-ups' page lists every pick with its fact so a
   person can spot what gets through.

### The scene

The backdrop is a `Scene`: a real place (named for grown-ups, such as "Reynisfjara, Iceland"), one of eleven
landscapes (meadow, farm, forest, jungle, mountains, beach, lake, desert, savanna, snowy hills, lava field),
a time of day (dawn to evening; the goodnight is the only night, and each play session starts at the hour the clock shows) and the weather (clear, cloudy, rain, snow,
fog, a rainbow). The loop chooses the kind of step (a journey from the starting meadow and after three steps at one place, otherwise mostly a weather change or the time moving on; a small model left to choose only moves the clock) and the model chooses the weather or where to go. Each step is held to the real world:

1. **Prompt and schema.** The time of day may only stay or move on; a rainbow is offered only after rain.
   A weather step must change the weather, a journey must go somewhere new. Half of the later requests for an animal or plant ask for one really found at the scene's place.
2. **Rules** (`SceneRules`): no snow in deserts, jungles or the savanna, a new landscape needs a new place,
   not all three things at once, no going back to one of the last eight places.
   A journey offers five well-known real places (Icelandic and Bulgarian ones among them, `KnownPlace.All`); if all three tries miss, it goes to one of them. A kind of country with a country after it ("Savannah, Botswana") is not a place.
3. **Place check** (`PlaceChecker`): a new place goes to a second, cold call that sees only its name, must
   say it is real, lists the landscapes found there itself (the claimed one must be among them) and, for
   snow, must say snow falls there.

Every scene keeps the brightness of the calm level's own sky and hills and only takes on their hue
(`SceneGround`), so the Art Rules' contrast holds in all of them; the weather is drawn still, and a new
scene fades in over 30 / 20 / 15 s. `dotnet run --project tools/Keypuncher.Preview -- --scenes` writes a
contact sheet of every landscape per calm level.

The names and facts are never shown or said to the child; the child's words still come only from the concept
list. Settings: the grown-ups' page has a switch, `KEYPUNCHER_MODEL` picks another model and `OLLAMA_HOST`
another server.

```powershell
dotnet run --project tools/Keypuncher.Sandbox -- picks qwen3:4b 12   # restyle all twelve families, print each try
dotnet run --project tools/Keypuncher.Sandbox -- check qwen3:4b      # score the checker on real and made-up names
dotnet run --project tools/Keypuncher.Sandbox -- scenes qwen3:4b 12  # walk the scene through twelve steps
dotnet run --project tools/Keypuncher.Sandbox -- places qwen3:4b     # score the place checker on real and made-up places
```

## Phase 0 on the desktop

Needs the .NET 10 SDK, [Ollama](https://ollama.com) and the Icelandic keyboard layout installed.

```powershell
ollama pull gemma3:4b
ollama pull qwen3:4b
ollama pull llama3.1:8b          # tight on 6 GB; worth measuring

dotnet test
dotnet run --project tools/Keypuncher.Bench -- --models gemma3:4b,qwen3:4b,llama3.1:8b --runs 5
dotnet run --project tools/Keypuncher.Sandbox -- recipe gemma3:4b "a round sleepy cat"
dotnet run --project tools/Keypuncher.KeySpike
```

The bench writes a Markdown table to `bench-results/`; commit it so the numbers live with the code.

Phase 0 gates, from the plan:

- [ ] The desktop returns a valid recipe in under 5 s median (`Keypuncher.Bench`).
- [ ] Every key on the Icelandic layout reports the right character, including Ð Þ Æ Ö and the
      accent dead key, and Win / Alt+Tab / Alt+F4 are swallowed (`Keypuncher.KeySpike`).
- [ ] SD-Turbo timing on the GTX 1060 (not yet scripted; see the plan).
- [ ] Piper's Icelandic voice (Salka) runs offline; check whether Windows offers a Bulgarian voice.
