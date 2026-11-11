using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Language;
using Keypuncher.Storage;

namespace Keypuncher.Toy;

/// <summary>
/// The reflex loop. Every key or tap gets an answer in the same frame: a thing from the key's family,
/// a note whose pitch follows the key's column, and for letters and digits the glyph and its sound.
/// How much appears, and for how long play lasts, comes from the calm level in <see cref="Settings"/>.
/// No model runs here. Call everything from one thread (the UI thread); the mixer is the only thing
/// shared with another thread, and it is built for that.
/// </summary>
public sealed class ToyEngine
{
    /// <summary>Pitches run up a pentatonic scale from G3 at the left of the keyboard.</summary>
    private const int LowestKeyMidi = 55;
    /// <summary>A letter's word waits this long. A later letter with a word of its own takes its place, and a mash
    /// or a digit drops it; other keys and taps leave it be, so a child who keeps pressing still hears words.</summary>
    private const double WordDelay = 0.9;
    /// <summary>A word whose voice is still being made when it is due keeps trying this long, then stays silent.</summary>
    private const double WordVoiceWait = 1.5;
    private const double CountInterval = 0.55;
    /// <summary>A press past the calm level's rate wakes a thing this close to its key's place (0..1 of the screen);
    /// about two keys' width, so the answer stays where the child pressed.</summary>
    private const double WakeRadius = 0.14;
    /// <summary>How far a word's label sits from its picture, and the lowest the label may go.</summary>
    private const double WordLabelGap = 0.27, MaxWordLabelY = 0.88;
    /// <summary>Letters no word begins with; these borrow a word that contains them (Ð: fiðrildi).
    /// Any other letter without a word just says its sound.</summary>
    private static readonly HashSet<char> NeverStartsAWord = ['Ð', 'Ъ', 'Ь'];
    /// <summary>Mash bursts are at least this far apart, well under the three-flashes-a-second limit.</summary>
    private const double MinBurstGap = 0.7;
    private const double MaxBurstScale = 1.6;
    /// <summary>With no input for this long the scene settles faster, and the time stops counting as play.</summary>
    public const double QuietAfter = 20;
    /// <summary>The goodnight dims the sky and puts things to sleep over this many seconds.</summary>
    public const double GoodnightSeconds = 8;
    /// <summary>Once play time is up, the goodnight waits for a pause this long, so a busy child is not cut off.</summary>
    public const double PauseBeforeGoodnight = 6;
    /// <summary>The longest the goodnight waits for that pause.</summary>
    public const double MaxWindDown = 180;
    /// <summary>An animal that appears makes its noise about one time in three, so it stays a surprise. The gap
    /// between noises comes from the calm level.</summary>
    public const double CallChance = 1 / 3.0;
    /// <summary>The animal a word brings is likelier to call, right after its name: "kýr", then moo.</summary>
    public const double WordCallChance = 0.5;
    /// <summary>A noise starts this long after its animal appears, once the press's note has sounded.</summary>
    private const double CallDelay = 0.35;
    /// <summary>A noise never talks over speech: it waits this long for speech to end, then is dropped.</summary>
    private const double CallWait = 2.5;
    /// <summary>The pop a tap makes on something already there: a short sine that falls fast, like a bubble.</summary>
    private static readonly Voice PopVoice = new(Waveform.Sine, 0.002, 0.18, "pentatonic", "D3");
    /// <summary>Where the pop lands, in Hz, from the left of the screen to the right.</summary>
    private const double PopLow = 140, PopHigh = 200;
    private const double PopSweepSeconds = 0.12;

    private readonly World _world;
    private readonly Mixer _mixer;
    private readonly SpeechLibrary _speech;
    private readonly Random _random;
    private readonly Random _callRandom;
    private readonly Func<DateTime>? _clock;
    private readonly MashGrouper _mash = new();
    private readonly DeadKeyComposer _composer = new();
    private readonly List<(double At, Action Do)> _scheduled = [];
    private (double At, Action Do)? _pendingWord;
    private (ClipId Clip, string Text, double X, double Until)? _unsaidWord;
    private (Shape Shape, double X, double At, double Until)? _pendingCall;
    private double _lastCall = double.NegativeInfinity;
    private ToySettings _settings;
    private LanguageMix _mix;
    private SpawnLimiter _limiter;
    private double _lastBurst = double.NegativeInfinity;
    /// <summary>Seconds of play this session: time within <see cref="QuietAfter"/> of a press or tap. A toy left
    /// running on its own does not use up the child's play time.</summary>
    private double _played;
    private double _lastInput;
    private double? _windDownSince, _asleepSince;
    private static readonly IReadOnlyDictionary<string, string> NoWords = new Dictionary<string, string>();
    /// <summary>The things (by slug) whose word the grown-ups wrote has been shown and said this session; each is
    /// named once a session, the first time it appears, so a key pressed over and over does not keep saying it.</summary>
    private readonly HashSet<string> _named = [];

    /// <param name="callRandom">Decides when animals make their noise; kept apart from <paramref name="random"/> so
    /// noises never change what appears.</param>
    /// <param name="clock">The wall clock; when given, each session starts at its time of day (<see cref="Scene.ForHour"/>).
    /// Left out, the scene keeps its time, as tests and the preview tool want.</param>
    public ToyEngine(World world, Mixer mixer, SpeechLibrary speech, ToySettings settings, Random? random = null, Random? callRandom = null,
        Func<DateTime>? clock = null)
    {
        _clock = clock;
        _world = world;
        _mixer = mixer;
        _speech = speech;
        _random = random ?? new Random();
        _callRandom = callRandom ?? new Random();
        Gate = new GrownUpGate(_random);
        TouchGate = new TouchGate(_random);
        _settings = settings;
        _mix = new LanguageMix(settings.LanguageWeights);
        _mixer.Volume = settings.Volume;
        _limiter = LimiterFor(settings.Limits);
        _world.Limits = settings.Limits;
        _world.Style = settings.Style;
        _ = AnimalCalls.Warm(settings.Limits.GentleCalls);
        StartSession();
    }

    public World World => _world;
    public GrownUpGate Gate { get; }
    /// <summary>The grown-up exit for a touch screen; fed by <see cref="OnFingerDown"/> and its siblings.</summary>
    public TouchGate TouchGate { get; }
    /// <summary>True once the session timer has run out; presses only make soft notes until a new session.</summary>
    public bool IsAsleep => _asleepSince is not null;
    /// <summary>True once play time is up but the child has not paused yet; things settle faster meanwhile.</summary>
    public bool IsWindingDown => _windDownSince is not null && !IsAsleep;
    /// <summary>Every letter, number and word in a session uses this one language, so a key keeps one sound.
    /// It is drawn from the language weights when the session starts.</summary>
    public string SessionLanguage { get; private set; } = KeyMeanings.Icelandic;

    /// <summary>The words the grown-ups wrote for things in a language (the thing's slug to its word); none by
    /// default. Asked on every press that makes a thing, so it must answer from memory. A word for one of the
    /// starter things takes the place of the toy's own; a word for any other thing is shown and said when the
    /// thing first appears in a session and can follow its first letter, while <see cref="ToySettings.SayThingWords"/> is on.</summary>
    public Func<string, IReadOnlyDictionary<string, string>> ThingWords { get; set; } = _ => NoWords;

    /// <summary>Raised when the grown-up challenge is answered.</summary>
    public event Action? GrownUpRequested;

    /// <summary>Raised when an animal makes its noise.</summary>
    public event Action<Shape>? AnimalCalled;

    /// <summary>Raised with the tap's x when a tap lands on something already there and it pops.</summary>
    public event Action<double>? Popped;

    /// <summary>Raised when one of a director-restyled family's things is put on screen, by a key or tap or by
    /// <see cref="Show"/>: the slot and the recipe. For the log, so a run shows what the child saw of the model's
    /// choices.</summary>
    public event Action<int, Recipe>? RestyledShown;

    public ToySettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _mix = new LanguageMix(value.LanguageWeights);
            _mixer.Volume = value.Volume;
            _limiter = LimiterFor(value.Limits);
            _world.Limits = value.Limits;
            _world.Style = value.Style;
            _ = AnimalCalls.Warm(value.Limits.GentleCalls);
            if (value.LanguageWeights.GetValueOrDefault(SessionLanguage) <= 0)
            {
                SessionLanguage = _mix.Pick(_random);
                // Things were named in the language that has just gone; in the new one they have not been.
                _named.Clear();
            }
        }
    }

    /// <summary>Starts the session timer again, picks the session's language and wakes the scene from a
    /// goodnight, and with a clock sets the sky to the hour; call when play starts.</summary>
    public void StartSession()
    {
        _played = 0;
        _lastInput = _world.Time;
        SessionLanguage = _mix.Pick(_random);
        _windDownSince = _asleepSince = null;
        _named.Clear();
        _world.Night = 0;
        _world.Quiet = false;
        // The sky agrees with the window when play starts; the director moves it on from there. SetScene fades it,
        // so a new session never jumps, and SceneGround keeps the calm level's brightness whatever the hour.
        if (_clock is { } clock) _world.SetScene(_world.Scene with { TimeOfDay = Scene.ForHour(clock().Hour) });
    }

    public void OnKey(KeyPress press)
    {
        if (Gate.Feed(press, _world.Time))
        {
            GrownUpRequested?.Invoke();
            return;
        }
        if (!press.IsDown)
        {
            _mash.Feed(press);
            return;
        }
        WakeAfterRest();
        _lastInput = _world.Time;
        if (IsAsleep)
        {
            if (_mash.Feed(press).Kind is PressKind.Single or PressKind.Mash) SleepyNote(KeyboardGeometry.Position(press.ScanCode).X);
            return;
        }

        if (press.IsDeadKey)
        {
            // The accent key on its own: something visible now, the accent lands on the next vowel.
            _mash.Feed(press with { Character = null });
            _composer.FeedDeadKey(press.Character ?? "");
            var (x, y) = KeyboardGeometry.Position(press.ScanCode);
            SpawnEffect(BuiltInRecipes.Sparkle, x, y, 0.8, 1.0);
            PlayNote(BuiltInRecipes.Sparkle.Voice, Notes.Parse(BuiltInRecipes.Sparkle.Voice.Root), 0.5, x);
            return;
        }

        var character = press.Character;
        var (kind, ev) = _mash.Feed(press);
        if (kind is PressKind.Single) character = _composer.Feed(character);
        if (ev is null) return;

        switch (kind)
        {
            case PressKind.Single:
                CancelCounting();
                Respond(ev with { Character = character }, speak: true);
                break;
            case PressKind.Repeat:
                Respond(ev with { Character = null }, speak: false);
                break;
            case PressKind.Mash:
                CancelScheduled();
                Mash(ev);
                break;
        }
    }

    /// <summary>A finger landed at 0..1 coordinates on a screen this wide over high, with this many fingers now down:
    /// the touch exit sees it first, then it plays like any tap.</summary>
    public void OnFingerDown(long id, double x, double y, double aspect, int fingers)
    {
        if (TouchGate.FingerDown(id, x, y, aspect, _world.Time))
        {
            GrownUpRequested?.Invoke();
            return;
        }
        OnTouch(x, y, fingers);
    }

    public void OnFingerMove(long id, double x, double y, double aspect) => TouchGate.FingerMove(id, x, y, aspect, _world.Time);

    public void OnFingerUp(long id) => TouchGate.FingerUp(id, _world.Time);

    /// <summary>A tap or press on the screen at 0..1 coordinates.</summary>
    public void OnTouch(double x, double y, int fingers)
    {
        WakeAfterRest();
        _lastInput = _world.Time;
        if (IsAsleep)
        {
            SleepyNote(x);
            return;
        }
        CancelCounting();
        var woken = _world.Touch(x, y, 0.08);
        if (fingers >= 3)
        {
            _pendingCall = null;
            Mash(new InputEvent(DateTimeOffset.Now, 0, null, x, y, fingers));
            return;
        }
        if (woken > 0) Pop(x);
        var spot = (uint)((int)(x * 6) + (6 * (int)(y * 4)));
        _world.Families.Use(spot);
        var recipe = _world.Families.For(spot);
        var made = SpawnOrWake(recipe, x, y, 1, DateTimeOffset.Now);
        PlayNote(recipe.Voice, Notes.Degree("pentatonic", LowestKeyMidi, (int)(x * 13)), 0.7, x);
        MaybeCall(recipe, x, CallChance);
        if (made is not null) ScheduleThingWord(made);
    }

    /// <summary>The longest step <see cref="Update"/> is given, so nothing jumps.</summary>
    public const double MaxStep = 0.1;

    /// <summary>The most a single frame may move the clock on: after a long stall (the PC asleep) play picks up
    /// where it was rather than racing through what was missed.</summary>
    public const double MaxCatchUp = 1;

    /// <summary>Moves the toy on by the real time since the last frame, in steps of at most <see cref="MaxStep"/>,
    /// so its clock keeps to real time however slowly the screen draws: the model's pace and the goodnight are in
    /// real minutes.</summary>
    public void Advance(double seconds)
    {
        for (var left = Math.Min(seconds, MaxCatchUp); left > 1e-9; left -= MaxStep) Update(Math.Min(left, MaxStep));
    }

    /// <summary>Advances the world and runs scheduled counting and words.</summary>
    public void Update(double dt)
    {
        if (_world.Time - _lastInput < QuietAfter) _played += dt;
        if (_windDownSince is null && _settings.SessionLength is { } length && _played >= length.TotalSeconds)
            _windDownSince = _world.Time;
        if (IsWindingDown && (_world.Time - _lastInput >= PauseBeforeGoodnight || _world.Time - _windDownSince >= MaxWindDown))
        {
            CancelScheduled();
            _asleepSince = _world.Time;
        }
        _world.Quiet = IsAsleep || IsWindingDown || _world.Time - _lastInput >= QuietAfter;
        _world.Night = _asleepSince is { } since ? Math.Min(1, (_world.Time - since) / GoodnightSeconds) : 0;
        _world.Tick(dt);
        TouchGate.Tick(_world.Time);
        for (var i = 0; i < _scheduled.Count;)
        {
            if (_scheduled[i].At > _world.Time) { i++; continue; }
            var action = _scheduled[i].Do;
            _scheduled.RemoveAt(i);
            action();
        }
        if (_pendingWord is { } word && word.At <= _world.Time)
        {
            _pendingWord = null;
            word.Do();
        }
        if (_pendingCall is { } call && call.At <= _world.Time)
        {
            if (_mixer.SpeechPlaying || _pendingWord is not null || _unsaidWord is not null)
            {
                if (_world.Time >= call.Until) _pendingCall = null;
            }
            else
            {
                _pendingCall = null;
                Call(call.Shape, call.X);
            }
        }
        if (_unsaidWord is { } unsaid)
        {
            if (_speech.TryGet(unsaid.Clip, unsaid.Text) is { } samples) _mixer.Speak(samples, (unsaid.X * 2) - 1);
            else if (_world.Time < unsaid.Until) return;
            _unsaidWord = null;
        }
    }

    /// <summary>After a goodnight, a rest as long as the play time makes the next press a new session, so a toy
    /// left running is awake again when the child comes back. Pressing on through the night keeps it asleep.</summary>
    private void WakeAfterRest()
    {
        if (IsAsleep && _settings.SessionLength is { } length && _world.Time - _lastInput >= length.TotalSeconds) StartSession();
    }

    /// <summary>Clears key state after the window loses focus (key-ups went elsewhere).</summary>
    public void ResetInput() => _mash.Reset();

    /// <summary>Puts one of <paramref name="recipe"/> on screen, unprompted, with a little burst: for a thing the
    /// director has just chosen, so the child sees it as soon as it is chosen and not only when its key is next
    /// pressed, which on a slow day could be never. Counts as shown for the log.</summary>
    public void Show(Recipe recipe)
    {
        var thing = SpawnThing(recipe, 0.2 + (_random.NextDouble() * 0.6), 0.4, 1.2);
        SpawnEffect(BuiltInRecipes.Burst, thing.X, thing.Y, 0.5, 1.2);
        if (_world.Families.RestyledSlot(recipe) is { } slot) RestyledShown?.Invoke(slot, recipe);
        // Nobody pressed anything, so its word, if the grown-ups wrote one, comes with it and not after a pause:
        // once a session, like a key's thing, however often the director comes back to it.
        if (WordOfThing(recipe) is { } word && !_named.Contains(Slugs.For(recipe.Name)))
        {
            CancelWord();
            _pendingCall = null;
            NameThing(thing, word, SessionLanguage);
        }
    }

    /// <summary>The word the grown-ups wrote, in the session's language, for a thing that is not one of the toy's
    /// starter things; null when there is none or such words are switched off.</summary>
    private string? WordOfThing(Recipe recipe)
    {
        if (!_settings.SayThingWords) return null;
        var words = ThingWords(SessionLanguage);
        if (words.Count == 0) return null;
        var slug = Slugs.For(recipe.Name);
        return !BuiltInRecipes.Concepts.ContainsKey(slug) && words.TryGetValue(slug, out var word) && !string.IsNullOrWhiteSpace(word) ? word : null;
    }

    /// <summary>After the pause a letter's word waits, names a thing that has just appeared, if the grown-ups
    /// wrote a word for it and it was not named this session yet. Returns whether its word is now waiting, in
    /// the place a letter's word would take.</summary>
    private bool ScheduleThingWord(Entity thing)
    {
        if (thing.Recipe is not { } recipe || WordOfThing(recipe) is not { } word) return false;
        var slug = Slugs.For(recipe.Name);
        if (_named.Contains(slug)) return false;
        var language = SessionLanguage;
        _speech.TryGet(ClipCatalog.ThingClip(language, slug), word); // start loading now so it is ready when the pause comes
        _pendingCall = null;
        _unsaidWord = null;
        _pendingWord = (_world.Time + WordDelay, () =>
        {
            NameThing(thing, word, language);
            MaybeCall(recipe, thing.X, WordCallChance);
        });
        return true;
    }

    /// <summary>Shows a thing's word by it and says it, and counts the thing as named this session.</summary>
    private void NameThing(Entity thing, string word, string language)
    {
        var slug = Slugs.For(thing.Recipe!.Name);
        _named.Add(slug);
        var spot = Math.Clamp(thing.X, 0.15, 0.85);
        ShowWord(word, spot, thing.Y);
        SayWord(ClipCatalog.ThingClip(language, slug), word, spot);
    }

    /// <summary>A word's label by its picture: under it, or over it when the picture is low (plants grow from the ground).</summary>
    private void ShowWord(string word, double x, double pictureY) =>
        _world.Add(new Entity
        {
            Id = _world.NextId(), Role = EntityRole.Word, Text = KeyMeanings.ApplyCase(word, upperCase: false),
            X = x, Y = pictureY + WordLabelGap <= MaxWordLabelY ? pictureY + WordLabelGap : pictureY - WordLabelGap,
            Born = _world.Time, Lifetime = 3.5,
        });

    /// <summary>Says a word, or, when its voice is still being made, keeps trying for a little while.</summary>
    private void SayWord(ClipId clip, string word, double x)
    {
        if (_speech.TryGet(clip, word) is { } samples) _mixer.Speak(samples, (x * 2) - 1);
        else _unsaidWord = (clip, word, x, _world.Time + WordVoiceWait);
    }

    private void Respond(InputEvent ev, bool speak)
    {
        var recipe = FamilyOf(ev.ScanCode);
        var column = KeyboardGeometry.Column(ev.ScanCode);
        var midi = Notes.Degree("pentatonic", LowestKeyMidi, (int)Math.Round(column));

        var language = SessionLanguage;
        var meaning = KeyMeanings.For(language, ev.Character);

        if (meaning?.Number is { } n)
        {
            CancelWord();
            ShowGlyph(meaning.Display, ev.X);
            Count(meaning, recipe, ev.X, ev.Y, midi);
            return;
        }

        PlayNote(recipe.Voice, midi, speak ? 0.8 : 0.45, ev.X);
        var made = SpawnOrWake(recipe, ev.X, ev.Y, speak ? 1 : 0.7, ev.Timestamp);
        if (speak) MaybeCall(recipe, ev.X, CallChance);
        // A real thing with a word of the grown-ups' is named the first time it appears; the letter's own word
        // gives way to it that once.
        var named = speak && made is not null && ScheduleThingWord(made);
        if (meaning is null) return;

        ShowGlyph(meaning.Display, ev.X);
        if (!speak || meaning.Clip is null) return;

        Say(meaning.Clip, meaning.FallbackText, ev.X);
        if (!named && _settings.SayWords && meaning.Letter is { } letter) ScheduleWord(language, letter, ev.X, ev.Y);
    }

    /// <summary>Now and then, an animal that has just appeared makes its noise, once speech is done. Only one
    /// noise waits at a time, and none comes sooner than the calm level's gap after the last.</summary>
    private void MaybeCall(Recipe recipe, double x, double chance)
    {
        var shape = recipe.Body.Shape;
        if (!_settings.AnimalNoises || IsWindingDown || IsAsleep || !AnimalCalls.HasCall(shape)) return;
        if (_pendingCall is not null || _world.Time - _lastCall < _settings.Limits.CallGapSeconds) return;
        if (_callRandom.NextDouble() >= chance) return;
        _pendingCall = (shape, x, _world.Time + CallDelay, _world.Time + CallDelay + CallWait);
    }

    private void Call(Shape shape, double x)
    {
        if (AnimalCalls.For(shape, _settings.Limits.GentleCalls) is not { } samples) return;
        _lastCall = _world.Time;
        _mixer.Call(samples, (x * 2) - 1);
        AnimalCalled?.Invoke(shape);
    }

    /// <summary>Digits: 0 says zero; 1 up to the calm level's limit make that many things, one at a time,
    /// counting aloud; bigger numbers say the number and make one thing. All of it at the key's place.</summary>
    private void Count(KeyMeaning meaning, Recipe recipe, double x, double y, int midi)
    {
        var n = meaning.Number!.Value;
        if (n > _settings.Limits.CountUpTo)
        {
            SpawnThing(recipe, x, y, 1.2);
            foreach (var step in new[] { 0, 2, 4 })
                PlayNote(recipe.Voice, Notes.Degree("pentatonic", midi, step), 0.5, x);
            if (meaning.Clip is not null) Say(meaning.Clip, meaning.FallbackText, x);
            return;
        }
        if (n == 0)
        {
            SpawnEffect(BuiltInRecipes.Burst, x, PlaceY(recipe, y), 0.5, 1.2);
            PlayNote(recipe.Voice, midi, 0.6, x);
            if (meaning.Clip is not null) Say(meaning.Clip, meaning.FallbackText, x);
            return;
        }

        for (var i = 1; i <= n; i++)
        {
            var count = i;
            var spot = Math.Clamp(x + ((count - ((n + 1) / 2.0)) * 0.06), 0.05, 0.95);
            Schedule((count - 1) * CountInterval, () =>
            {
                SpawnThing(recipe, spot, y + (_random.NextDouble() * 0.1), 1);
                PlayNote(recipe.Voice, Notes.Degree("pentatonic", LowestKeyMidi + 12, count - 1), 0.7, spot);
                var said = KeyMeanings.For(meaning.Language, count.ToString())!;
                Say(said.Clip!, said.FallbackText, spot);
            });
        }
    }

    private void ScheduleWord(string language, char letter, double x, double y)
    {
        var pool = WordPool(language);
        var candidates = pool.Where(c => char.ToUpperInvariant(c.Word[0]) == char.ToUpperInvariant(letter)).ToList();
        if (candidates.Count == 0 && NeverStartsAWord.Contains(letter))
            candidates = [.. pool.Where(c => c.Word.Contains(letter, StringComparison.InvariantCultureIgnoreCase))];
        if (candidates.Count == 0) return;

        var (id, word, recipe) = candidates[_random.Next(candidates.Count)];
        var clip = ClipCatalog.ThingClip(language, id);
        _speech.TryGet(clip, word); // start loading now so it is ready when the pause comes

        // The word brings its own picture; a noise from the key's thing would not match it.
        _pendingCall = null;
        _unsaidWord = null;
        _pendingWord = (_world.Time + WordDelay, () =>
        {
            var spot = Math.Clamp(x, 0.15, 0.85);
            // The word's picture comes up where the letter's key is, with its label by it.
            var picture = SpawnThing(recipe, spot, y, 1.8);
            ShowWord(word, spot, picture.Y);
            _named.Add(id);
            PlayNote(recipe.Voice, Notes.Parse(recipe.Voice.Root), 0.6, spot);
            // A voice made on the fly can take longer than the pause the first time; say it once it is ready.
            SayWord(clip, word, spot);
            MaybeCall(recipe, spot, WordCallChance);
        });
    }

    /// <summary>Every word a letter can bring in a language, each with the thing it names and the recipe that
    /// draws it: the starter words in their order, a grown-up's word for a starter thing in place of the toy's,
    /// then (while such words are on) the grown-ups' words for the real things a key makes now, by slug.</summary>
    private List<(string Id, string Word, Recipe Recipe)> WordPool(string language)
    {
        var words = ThingWords(language);
        var pool = new List<(string Id, string Word, Recipe Recipe)>();
        foreach (var concept in StarterConcepts.All)
        {
            var word = words.TryGetValue(concept.Id, out var byHand) && !string.IsNullOrWhiteSpace(byHand) ? byHand : concept.WordIn(language);
            if (!string.IsNullOrEmpty(word)) pool.Add((concept.Id, word, BuiltInRecipes.ForConcept(concept.Id)));
        }
        if (words.Count == 0 || !_settings.SayThingWords) return pool;
        foreach (var recipe in _world.Families.All)
        {
            var slug = Slugs.For(recipe.Name);
            if (BuiltInRecipes.Concepts.ContainsKey(slug) || pool.Any(c => c.Id == slug)) continue;
            if (words.TryGetValue(slug, out var word) && !string.IsNullOrWhiteSpace(word)) pool.Add((slug, word, recipe));
        }
        return pool;
    }

    /// <summary>One soft burst and one chord; a burst too soon after the last gets a single note instead.</summary>
    private void Mash(InputEvent ev)
    {
        var root = Notes.Degree("pentatonic", LowestKeyMidi, (int)(ev.X * 10));
        if (_world.Time - _lastBurst < MinBurstGap)
        {
            PlayNote(BuiltInRecipes.Burst.Voice, root, 0.35, ev.X);
            return;
        }
        _lastBurst = _world.Time;
        var size = Math.Min(1.0 + (ev.Intensity * 0.1), MaxBurstScale);
        SpawnEffect(BuiltInRecipes.Burst, ev.X, ev.Y, size, 1.4);
        foreach (var step in new[] { 0, 2, 4 })
            PlayNote(BuiltInRecipes.Burst.Voice, Notes.Degree("pentatonic", root, step), 0.5, ev.X);
        SpawnOrWake(FamilyOf(ev.ScanCode), ev.X, ev.Y, 1.2, ev.Timestamp);
    }

    /// <summary>A tap on something already on screen pops as well as waking it: a sine falling six-fold in 120 ms
    /// (four-fold and softer for babies, whose calls are gentle too), pitched a little higher to the right.</summary>
    private void Pop(double x)
    {
        var gentle = _settings.Limits.GentleCalls;
        var frequency = PopLow + ((PopHigh - PopLow) * Math.Clamp(x, 0, 1));
        _mixer.Note(frequency, PopVoice, gentle ? 0.35 : 0.5, (x * 2) - 1, sweepFrom: gentle ? 4 : 6, sweepSeconds: PopSweepSeconds);
        Popped?.Invoke(x);
    }

    private void SleepyNote(double x) =>

        PlayNote(BuiltInRecipes.Palette[2].Voice, Notes.Degree("pentatonic", LowestKeyMidi, (int)(x * 8)), 0.3, x);

    /// <summary>A new thing while the calm level's rate allows; otherwise the thing nearest the key's place wakes
    /// up (one of the same family if there is one close by), or, with nothing close, a small sparkle shows there.
    /// Either way every press gets a visible answer where the key is, without adding to the crowd.</summary>
    /// <returns>The new thing, or null when none was made.</returns>
    private Entity? SpawnOrWake(Recipe recipe, double x, double y, double scale, DateTimeOffset at)
    {
        if (_limiter.TryTake(at))
        {
            var thing = SpawnThing(recipe, x, y, scale);
            if (_world.Families.RestyledSlot(recipe) is { } slot) RestyledShown?.Invoke(slot, recipe);
            return thing;
        }
        var placeY = PlaceY(recipe, y);
        if (_world.Nearest(x, placeY, WakeRadius, recipe) is { } existing) _world.Wake(existing);
        else SpawnEffect(BuiltInRecipes.Sparkle, x, placeY, 0.6, 1.0);
        return null;
    }

    /// <summary>The height a thing of <paramref name="recipe"/> made at key height <paramref name="y"/> is placed at,
    /// before its jitter: plants grow from the ground, creatures stay at the key's height, kept clear of the hills'
    /// top edge.</summary>
    private static double PlaceY(Recipe recipe, double y) =>
        recipe.Kind == RecipeKind.Plant ? 0.74 + (y * 0.2) : Math.Clamp(0.1 + (y * 0.6), 0.08, 0.7);

    private static SpawnLimiter LimiterFor(CalmLimits limits) =>
        new(limits.NewThingsPerSecond, burst: limits.NewThingsPerSecond < 1 ? 1 : 2 * limits.NewThingsPerSecond);

    private void ShowGlyph(string display, double x) =>
        _world.Add(new Entity
        {
            Id = _world.NextId(), Role = EntityRole.Letter, Text = KeyMeanings.ApplyCase(display, _settings.UpperCase),
            X = Math.Clamp(x, 0.12, 0.88), Y = 0.3, Born = _world.Time, Lifetime = 2.2,
        });

    private void Say(ClipId clip, string? fallbackText, double x)
    {
        if (_speech.TryGet(clip, fallbackText) is { } samples)
            _mixer.Speak(samples, (x * 2) - 1);
    }

    private Entity SpawnThing(Recipe recipe, double x, double y, double scale)
    {
        // Plants grow from the ground; creatures stay where the key is, kept clear of the hills' top edge.
        var jitter = (_random.NextDouble() - 0.5) * 0.04;
        var placeY = recipe.Kind == RecipeKind.Plant
            ? PlaceY(recipe, y) + (jitter * 0.5)
            : Math.Clamp(PlaceY(recipe, y) + jitter, 0.08, 0.7);
        return _world.Add(new Entity
        {
            Id = _world.NextId(), Role = recipe.Kind == RecipeKind.Effect ? EntityRole.Effect : EntityRole.Thing,
            Recipe = recipe, X = Math.Clamp(x + jitter, 0.03, 0.97), Y = placeY,
            Born = _world.Time, Scale = scale, Phase = _random.NextDouble(),
            Lifetime = recipe.Kind == RecipeKind.Effect ? 6 : double.PositiveInfinity,
        });
    }

    private void SpawnEffect(Recipe recipe, double x, double y, double scale, double lifetime) =>
        _world.Add(new Entity
        {
            Id = _world.NextId(), Role = EntityRole.Effect, Recipe = recipe, X = x, Y = y,
            Born = _world.Time, Scale = scale, Phase = _random.NextDouble(), Lifetime = lifetime,
        });

    private void PlayNote(Voice voice, int midi, double velocity, double x) =>
        _mixer.Note(Notes.Frequency(midi), voice, velocity, (x * 2) - 1);

    /// <summary>The same key always makes the same family of thing, though the director may restyle the family.</summary>
    private Recipe FamilyOf(int scanCode)
    {
        _world.Families.Use((uint)scanCode);
        return _world.Families.For((uint)scanCode);
    }

    private void Schedule(double delay, Action action)
    {
        if (delay <= 0) action();
        else _scheduled.Add((_world.Time + delay, action));
    }

    private void CancelScheduled()
    {
        CancelCounting();
        CancelWord();
        _pendingCall = null;
    }

    private void CancelCounting() => _scheduled.Clear();

    private void CancelWord() => (_pendingWord, _unsaidWord) = (null, null);
}
