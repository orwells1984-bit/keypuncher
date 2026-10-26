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
    /// <summary>True once the session timer has run out; presses only make soft notes until a new session.</summary>
    public bool IsAsleep => _asleepSince is not null;
    /// <summary>True once play time is up but the child has not paused yet; things settle faster meanwhile.</summary>
    public bool IsWindingDown => _windDownSince is not null && !IsAsleep;
    /// <summary>Every letter, number and word in a session uses this one language, so a key keeps one sound.
    /// It is drawn from the language weights when the session starts.</summary>
    public string SessionLanguage { get; private set; } = KeyMeanings.Icelandic;

    /// <summary>Raised when the grown-up challenge is answered.</summary>
    public event Action? GrownUpRequested;

    /// <summary>Raised when an animal makes its noise.</summary>
    public event Action<Shape>? AnimalCalled;

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
            if (value.LanguageWeights.GetValueOrDefault(SessionLanguage) <= 0) SessionLanguage = _mix.Pick(_random);
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
        _world.Touch(x, y, 0.08);
        if (fingers >= 3)
        {
            _pendingCall = null;
            Mash(new InputEvent(DateTimeOffset.Now, 0, null, x, y, fingers));
            return;
        }
        var recipe = _world.Families.For((uint)((int)(x * 6) + (6 * (int)(y * 4))));
        SpawnOrWake(recipe, x, y, 1, DateTimeOffset.Now);
        PlayNote(recipe.Voice, Notes.Degree("pentatonic", LowestKeyMidi, (int)(x * 13)), 0.7, x);
        MaybeCall(recipe, x, CallChance);
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

    /// <summary>Puts one of <paramref name="recipe"/> on screen, unprompted: for the director's test mode, so a
    /// grown-up sees each new thing as soon as it is chosen.</summary>
    public void Show(Recipe recipe) => SpawnThing(recipe, 0.2 + (_random.NextDouble() * 0.6), 0.4, 1.2);

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
            Count(meaning, recipe, ev.X, midi);
            return;
        }

        PlayNote(recipe.Voice, midi, speak ? 0.8 : 0.45, ev.X);
        SpawnOrWake(recipe, ev.X, ev.Y, speak ? 1 : 0.7, ev.Timestamp);
        if (speak) MaybeCall(recipe, ev.X, CallChance);
        if (meaning is null) return;

        ShowGlyph(meaning.Display, ev.X);
        if (!speak || meaning.Clip is null) return;

        Say(meaning.Clip, meaning.FallbackText, ev.X);
        if (_settings.SayWords && meaning.Letter is { } letter) ScheduleWord(language, letter, ev.X);
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
    /// counting aloud; bigger numbers say the number and make one thing.</summary>
    private void Count(KeyMeaning meaning, Recipe recipe, double x, int midi)
    {
        var n = meaning.Number!.Value;
        if (n > _settings.Limits.CountUpTo)
        {
            SpawnThing(recipe, x, 0.45, 1.2);
            foreach (var step in new[] { 0, 2, 4 })
                PlayNote(recipe.Voice, Notes.Degree("pentatonic", midi, step), 0.5, x);
            if (meaning.Clip is not null) Say(meaning.Clip, meaning.FallbackText, x);
            return;
        }
        if (n == 0)
        {
            SpawnEffect(BuiltInRecipes.Burst, x, 0.45, 0.5, 1.2);
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
                SpawnThing(recipe, spot, 0.45 + (_random.NextDouble() * 0.1), 1);
                PlayNote(recipe.Voice, Notes.Degree("pentatonic", LowestKeyMidi + 12, count - 1), 0.7, spot);
                var said = KeyMeanings.For(meaning.Language, count.ToString())!;
                Say(said.Clip!, said.FallbackText, spot);
            });
        }
    }

    private void ScheduleWord(string language, char letter, double x)
    {
        var candidates = StarterConcepts.StartingWith(language, letter).ToList();
        if (candidates.Count == 0 && NeverStartsAWord.Contains(letter))
            candidates = StarterConcepts.Containing(language, letter).ToList();
        if (candidates.Count == 0) return;

        var concept = candidates[_random.Next(candidates.Count)];
        var word = concept.WordIn(language)!;
        var clip = new ClipId(language, ClipKind.Word, concept.Id);
        _speech.TryGet(clip, word); // start loading now so it is ready when the pause comes

        // The word brings its own picture; a noise from the key's thing would not match it.
        _pendingCall = null;
        _unsaidWord = null;
        _pendingWord = (_world.Time + WordDelay, () =>
        {
            var spot = Math.Clamp(x, 0.15, 0.85);
            var recipe = BuiltInRecipes.ForConcept(concept.Id);
            SpawnThing(recipe, spot, 0.42, 1.8);
            _world.Add(new Entity
            {
                Id = _world.NextId(), Role = EntityRole.Word, Text = KeyMeanings.ApplyCase(word, upperCase: false),
                X = spot, Y = 0.62, Born = _world.Time, Lifetime = 3.5,
            });
            PlayNote(recipe.Voice, Notes.Parse(recipe.Voice.Root), 0.6, spot);
            // A voice made on the fly can take longer than the pause the first time; say it once it is ready.
            if (_speech.TryGet(clip, word) is { } samples) _mixer.Speak(samples, (spot * 2) - 1);
            else _unsaidWord = (clip, word, spot, _world.Time + WordVoiceWait);
            MaybeCall(recipe, spot, WordCallChance);
        });
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

    private void SleepyNote(double x) =>
        PlayNote(BuiltInRecipes.Palette[2].Voice, Notes.Degree("pentatonic", LowestKeyMidi, (int)(x * 8)), 0.3, x);

    /// <summary>A new thing while the calm level's rate allows; otherwise the newest of that family wakes up,
    /// so every press still gets a visible answer without adding to the crowd.</summary>
    private void SpawnOrWake(Recipe recipe, double x, double y, double scale, DateTimeOffset at)
    {
        if (_limiter.TryTake(at)) SpawnThing(recipe, x, y, scale);
        else if (_world.Newest(recipe) is { } existing) _world.Wake(existing);
    }

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

    private void SpawnThing(Recipe recipe, double x, double y, double scale)
    {
        // Plants grow from the ground; creatures stay where the key is, kept clear of the hills' top edge.
        var jitter = (_random.NextDouble() - 0.5) * 0.04;
        var placeY = recipe.Kind == RecipeKind.Plant
            ? 0.74 + (y * 0.2) + (jitter * 0.5)
            : Math.Clamp(0.1 + (y * 0.6) + jitter, 0.08, 0.7);
        _world.Add(new Entity
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
    private Recipe FamilyOf(int scanCode) => _world.Families.For((uint)scanCode);

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
