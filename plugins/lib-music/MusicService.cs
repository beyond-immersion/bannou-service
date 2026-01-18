using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Harmony;
using BeyondImmersion.Bannou.MusicTheory.Melody;
using BeyondImmersion.Bannou.MusicTheory.Output;
using BeyondImmersion.Bannou.MusicTheory.Pitch;
using BeyondImmersion.Bannou.MusicTheory.Style;
using BeyondImmersion.Bannou.MusicTheory.Time;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-music.tests")]

namespace BeyondImmersion.BannouService.Music;

/// <summary>
/// Implementation of the Music service.
/// Provides music theory computation and MIDI-JSON generation using the music-theory SDK.
/// </summary>
[BannouService("music", typeof(IMusicService), lifetime: ServiceLifetime.Scoped)]
public partial class MusicService : IMusicService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<MusicService> _logger;
    private readonly MusicServiceConfiguration _configuration;

    /// <summary>
    /// Creates a new MusicService instance.
    /// </summary>
    public MusicService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<MusicService> logger,
        MusicServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Generates a complete musical composition using the specified style and constraints.
    /// </summary>
    public async Task<(StatusCodes, GenerateCompositionResponse?)> GenerateCompositionAsync(
        GenerateCompositionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating composition with style {StyleId}", body.StyleId);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Get the style definition
            var style = GetStyleDefinition(body.StyleId);
            if (style == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Set up random generator
            var seed = body.Seed ?? Environment.TickCount;
            var random = new Random(seed);

            // Determine key signature (PitchClass has x-sdk-type, no conversion needed)
            var key = body.Key != null
                ? new Scale(body.Key.Tonic, ToModeType(body.Key.Mode))
                : new Scale(
                    (PitchClass)random.Next(12),
                    style.ModeDistribution.Select(random));

            // Determine tempo
            var tempo = body.Tempo > 0 ? body.Tempo : style.DefaultTempo;

            // Get meter from tune type or style default
            var meter = style.DefaultMeter;
            if (!string.IsNullOrEmpty(body.TuneType))
            {
                var tuneType = style.GetTuneType(body.TuneType);
                if (tuneType != null)
                {
                    meter = tuneType.Meter;
                }
            }

            // Calculate ticks
            var ticksPerBeat = 480;
            var beatsPerBar = meter.Numerator;
            var totalBars = body.DurationBars;

            // Generate chord progression
            var progressionGenerator = new ProgressionGenerator(seed);
            var chordsPerBar = 1; // One chord per bar for simplicity
            var progression = progressionGenerator.Generate(key, totalBars * chordsPerBar);

            // Voice the chords
            var voiceLeader = new VoiceLeader();
            var (voicings, voiceViolations) = voiceLeader.Voice(
                progression.Select(p => p.Chord).ToList(),
                voiceCount: 4);

            // Generate melody over the harmony
            var melodyOptions = new MelodyOptions
            {
                Range = PitchRange.Vocal.Soprano,
                Contour = ContourShape.Arch,
                IntervalPreferences = style.IntervalPreferences,
                Density = 0.7,
                Syncopation = 0.2,
                TicksPerBeat = ticksPerBeat
            };

            var melodyGenerator = new MelodyGenerator(seed);
            var melody = melodyGenerator.Generate(progression, key, melodyOptions);

            // Render to MIDI-JSON
            var midiJson = MidiJsonRenderer.RenderComposition(
                melody,
                voicings,
                tempo,
                meter,
                key,
                ticksPerBeat,
                $"Generated {style.Name} Composition");

            stopwatch.Stop();

            // SDK types used directly via x-sdk-type (no conversion needed)
            var response = new GenerateCompositionResponse
            {
                CompositionId = Guid.NewGuid().ToString(),
                MidiJson = midiJson,
                GenerationTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Metadata = new CompositionMetadata
                {
                    StyleId = body.StyleId,
                    Key = new KeySignature
                    {
                        Tonic = key.Root,
                        Mode = ToApiMode(key.Mode)  // KeySignature uses API enum, not SDK ModeType
                    },
                    Tempo = tempo,
                    Bars = totalBars,
                    TuneType = body.TuneType,
                    Seed = seed
                }
            };

            await Task.CompletedTask; // Satisfy async requirement
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating composition");
            await _messageBus.TryPublishErrorAsync(
                "music",
                "GenerateComposition",
                "generation_failed",
                ex.Message,
                dependency: null,
                endpoint: "post:/music/generate",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Validates a MIDI-JSON structure for correctness.
    /// </summary>
    public async Task<(StatusCodes, ValidateMidiJsonResponse?)> ValidateMidiJsonAsync(
        ValidateMidiJsonRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Validating MIDI-JSON structure");

        try
        {
            var errors = new List<ValidationError>();
            var warnings = new List<string>();

            // Validate tracks exist
            if (body.MidiJson.Tracks == null || body.MidiJson.Tracks.Count == 0)
            {
                errors.Add(new ValidationError
                {
                    Type = ValidationErrorType.Structure,
                    Message = "MIDI-JSON must contain at least one track",
                    Path = "tracks"
                });
            }

            // Validate ticks per beat
            if (body.MidiJson.TicksPerBeat < 24 || body.MidiJson.TicksPerBeat > 960)
            {
                errors.Add(new ValidationError
                {
                    Type = ValidationErrorType.Range,
                    Message = "ticksPerBeat must be between 24 and 960",
                    Path = "ticksPerBeat"
                });
            }

            // Validate each track
            var trackIndex = 0;
            foreach (var track in body.MidiJson.Tracks ?? [])
            {
                // Validate channel
                if (track.Channel < 0 || track.Channel > 15)
                {
                    errors.Add(new ValidationError
                    {
                        Type = ValidationErrorType.Range,
                        Message = $"Track {trackIndex} channel must be 0-15",
                        Path = $"tracks[{trackIndex}].channel"
                    });
                }

                // Validate events
                var eventIndex = 0;
                foreach (var evt in track.Events ?? [])
                {
                    // Validate tick is non-negative
                    if (evt.Tick < 0)
                    {
                        errors.Add(new ValidationError
                        {
                            Type = ValidationErrorType.Timing,
                            Message = "Event tick cannot be negative",
                            Path = $"tracks[{trackIndex}].events[{eventIndex}].tick"
                        });
                    }

                    // Validate note range
                    if (evt.Note.HasValue && (evt.Note < 0 || evt.Note > 127))
                    {
                        errors.Add(new ValidationError
                        {
                            Type = ValidationErrorType.Range,
                            Message = "MIDI note must be 0-127",
                            Path = $"tracks[{trackIndex}].events[{eventIndex}].note"
                        });
                    }

                    // Validate velocity range
                    if (evt.Velocity.HasValue && (evt.Velocity < 0 || evt.Velocity > 127))
                    {
                        errors.Add(new ValidationError
                        {
                            Type = ValidationErrorType.Range,
                            Message = "Velocity must be 0-127",
                            Path = $"tracks[{trackIndex}].events[{eventIndex}].velocity"
                        });
                    }

                    // Strict mode: warn about missing duration for noteOn
                    if (body.StrictMode && evt.Type == MidiEventType.NoteOn && !evt.Duration.HasValue)
                    {
                        warnings.Add($"Track {trackIndex}, event {eventIndex}: noteOn without duration (implicit noteOff required)");
                    }

                    eventIndex++;
                }

                trackIndex++;
            }

            var response = new ValidateMidiJsonResponse
            {
                IsValid = errors.Count == 0,
                Errors = errors.Count > 0 ? errors : null,
                Warnings = warnings.Count > 0 ? warnings : null
            };

            await Task.CompletedTask;
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating MIDI-JSON");
            await _messageBus.TryPublishErrorAsync(
                "music",
                "ValidateMidiJson",
                "validation_failed",
                ex.Message,
                dependency: null,
                endpoint: "post:/music/validate",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets a style definition by ID or name.
    /// </summary>
    public async Task<(StatusCodes, StyleDefinitionResponse?)> GetStyleAsync(
        GetStyleRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting style {StyleId} / {StyleName}", body.StyleId, body.StyleName);

        try
        {
            StyleDefinition? style = null;

            if (!string.IsNullOrEmpty(body.StyleId))
            {
                style = BuiltInStyles.GetById(body.StyleId);
            }
            else if (!string.IsNullOrEmpty(body.StyleName))
            {
                style = BuiltInStyles.All.FirstOrDefault(s =>
                    s.Name.Equals(body.StyleName, StringComparison.OrdinalIgnoreCase));
            }

            if (style == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var response = ConvertToStyleResponse(style);
            await Task.CompletedTask;
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting style");
            await _messageBus.TryPublishErrorAsync(
                "music",
                "GetStyle",
                "style_lookup_failed",
                ex.Message,
                dependency: null,
                endpoint: "post:/music/style/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists all available style definitions.
    /// </summary>
    public async Task<(StatusCodes, ListStylesResponse?)> ListStylesAsync(
        ListStylesRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Listing styles with category filter {Category}", body.Category);

        try
        {
            var styles = BuiltInStyles.All.AsEnumerable();

            // Filter by category if specified
            if (!string.IsNullOrEmpty(body.Category))
            {
                styles = styles.Where(s =>
                    s.Category.Equals(body.Category, StringComparison.OrdinalIgnoreCase));
            }

            var styleList = styles.ToList();
            var total = styleList.Count;

            // Apply pagination
            var pagedStyles = styleList
                .Skip(body.Offset)
                .Take(body.Limit)
                .Select(s => new StyleSummary
                {
                    StyleId = s.Id,
                    Name = s.Name,
                    Category = s.Category,
                    Description = s.Description
                })
                .ToList();

            var response = new ListStylesResponse
            {
                Styles = pagedStyles,
                Total = total
            };

            await Task.CompletedTask;
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing styles");
            await _messageBus.TryPublishErrorAsync(
                "music",
                "ListStyles",
                "list_styles_failed",
                ex.Message,
                dependency: null,
                endpoint: "post:/music/style/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Creates a new style definition.
    /// </summary>
    public async Task<(StatusCodes, StyleDefinitionResponse?)> CreateStyleAsync(
        CreateStyleRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating style {Name}", body.Name);

        try
        {
            // For now, return the style definition as if it was created
            // In production, this would persist to state store
            var response = new StyleDefinitionResponse
            {
                StyleId = body.Name.ToLowerInvariant().Replace(" ", "-"),
                Name = body.Name,
                Category = body.Category,
                Description = body.Description,
                ModeDistribution = body.ModeDistribution,
                IntervalPreferences = body.IntervalPreferences,
                FormTemplates = body.FormTemplates?.ToList(),
                TuneTypes = body.TuneTypes?.ToList(),
                DefaultTempo = body.DefaultTempo,
                HarmonyStyle = body.HarmonyStyle
            };

            await Task.CompletedTask;
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating style");
            await _messageBus.TryPublishErrorAsync(
                "music",
                "CreateStyle",
                "create_style_failed",
                ex.Message,
                dependency: null,
                endpoint: "post:/music/style/create",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Generates a chord progression using harmonic function theory.
    /// </summary>
    public async Task<(StatusCodes, GenerateProgressionResponse?)> GenerateProgressionAsync(
        GenerateProgressionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating progression in {Tonic} {Mode}", body.Key.Tonic, body.Key.Mode);

        try
        {
            var seed = body.Seed ?? Environment.TickCount;
            var random = new Random(seed);

            var scale = new Scale(body.Key.Tonic, ToModeType(body.Key.Mode));
            var generator = new ProgressionGenerator(seed);

            var length = body.Length > 0 ? body.Length : 8;
            var progression = generator.Generate(scale, length);

            // Convert to API response
            var ticksPerBeat = 480;
            var beatsPerChord = 4.0;
            var currentTick = 0;

            var chordEvents = new List<ChordEvent>();
            foreach (var chord in progression)
            {
                var durationTicks = (int)(beatsPerChord * ticksPerBeat);
                chordEvents.Add(new ChordEvent
                {
                    Chord = new ChordSymbol
                    {
                        Root = chord.Chord.Root,
                        Quality = ToApiChordQuality(chord.Chord.Quality),
                        Bass = chord.Chord.Bass
                    },
                    StartTick = currentTick,
                    DurationTicks = durationTicks,
                    RomanNumeral = chord.RomanNumeral
                });
                currentTick += durationTicks;
            }

            // Build analysis
            var analysis = new ProgressionAnalysis
            {
                RomanNumerals = progression.Select(p => p.RomanNumeral).ToList(),
                FunctionalAnalysis = progression.Select(p => GetFunctionalAnalysis(p.Degree)).ToList()
            };

            var response = new GenerateProgressionResponse
            {
                Chords = chordEvents,
                Analysis = analysis
            };

            await Task.CompletedTask;
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating progression");
            await _messageBus.TryPublishErrorAsync(
                "music",
                "GenerateProgression",
                "progression_generation_failed",
                ex.Message,
                dependency: null,
                endpoint: "post:/music/theory/progression",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Generates a melody over a chord progression.
    /// </summary>
    public async Task<(StatusCodes, GenerateMelodyResponse?)> GenerateMelodyAsync(
        GenerateMelodyRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating melody over {ChordCount} chords", body.Harmony?.Count ?? 0);

        try
        {
            var seed = body.Seed ?? Environment.TickCount;
            var random = new Random(seed);

            // Infer key from first chord (or default to C major)
            var firstChord = body.Harmony?.FirstOrDefault();
            var root = firstChord?.Chord.Root ?? PitchClass.C;
            var mode = ModeType.Major;
            var scale = new Scale(root, mode);

            // Convert harmony to SDK progression (PitchClass has x-sdk-type, no conversion needed)
            var progression = body.Harmony?.Select((ce, i) => new ProgressionChord(
                new Chord(ce.Chord.Root, ToChordQuality(ce.Chord.Quality)),
                ce.RomanNumeral ?? GetRomanNumeral(i),
                i + 1,
                ce.DurationTicks / 480.0
            )).ToList() ?? [];

            // Set up melody options (Pitch and PitchRange have x-sdk-type, no conversion needed)
            var range = body.Range != null
                ? new PitchRange(
                    new Pitch(body.Range.Value.Low.PitchClass, body.Range.Value.Low.Octave),
                    new Pitch(body.Range.Value.High.PitchClass, body.Range.Value.High.Octave))
                : PitchRange.Vocal.Soprano;

            var contour = body.Contour.HasValue
                ? ToContourShape(body.Contour.Value)
                : ContourShape.Arch;

            var options = new MelodyOptions
            {
                Range = range,
                Contour = contour,
                Density = body.RhythmDensity ?? 0.7,
                Syncopation = body.Syncopation ?? 0.2,
                TicksPerBeat = 480
            };

            var generator = new MelodyGenerator(seed);
            var melody = generator.Generate(progression, scale, options);

            // Convert to API response (Pitch is SDK type via x-sdk-type)
            var notes = melody.Select(n => new NoteEvent
            {
                Pitch = n.Pitch,
                StartTick = n.StartTick,
                DurationTicks = n.DurationTicks,
                Velocity = n.Velocity
            }).ToList();

            // Build analysis
            var analysis = new MelodyAnalysis
            {
                NoteCount = notes.Count,
                Contour = contour.ToString().ToLowerInvariant(),
                AverageNoteDuration = notes.Count > 0
                    ? (float)notes.Average(n => n.DurationTicks)
                    : 0
            };

            if (notes.Count > 0)
            {
                var minPitch = notes.MinBy(n => n.Pitch.MidiNumber);
                var maxPitch = notes.MaxBy(n => n.Pitch.MidiNumber);
                if (minPitch != null && maxPitch != null)
                {
                    analysis.Range = new PitchRange(minPitch.Pitch, maxPitch.Pitch);
                }
            }

            var response = new GenerateMelodyResponse
            {
                Notes = notes,
                Analysis = analysis
            };

            await Task.CompletedTask;
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating melody");
            await _messageBus.TryPublishErrorAsync(
                "music",
                "GenerateMelody",
                "melody_generation_failed",
                ex.Message,
                dependency: null,
                endpoint: "post:/music/theory/melody",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Applies voice leading rules to a chord sequence.
    /// </summary>
    public async Task<(StatusCodes, VoiceLeadResponse?)> ApplyVoiceLeadingAsync(
        VoiceLeadRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Applying voice leading to {ChordCount} chords", body.Chords?.Count ?? 0);

        try
        {
            // Convert API chords to SDK chords (PitchClass has x-sdk-type, no conversion needed)
            var chords = body.Chords?.Select(cs =>
                new Chord(cs.Root, ToChordQuality(cs.Quality), cs.Bass)
            ).ToList() ?? [];

            // Set up voice leading rules
            var rules = new VoiceLeadingRules
            {
                AvoidParallelFifths = body.Rules?.AvoidParallelFifths ?? true,
                AvoidParallelOctaves = body.Rules?.AvoidParallelOctaves ?? true,
                PreferStepwiseMotion = body.Rules?.PreferStepwiseMotion ?? true,
                AvoidVoiceCrossing = body.Rules?.AvoidVoiceCrossing ?? true,
                MaxLeap = body.Rules?.MaxLeap ?? 7
            };

            var voiceLeader = new VoiceLeader(rules);

            // Voice the chords (returns voicings and violations together)
            var (voicings, sdkViolations) = voiceLeader.Voice(chords, voiceCount: 4);

            // VoiceLeadingViolation and Pitch have x-sdk-type, so SDK types are used directly

            // Convert voicings to API response
            var voicedChords = new List<VoicedChord>();
            for (int i = 0; i < voicings.Count && i < (body.Chords?.Count ?? 0); i++)
            {
                var original = body.Chords?.ElementAt(i);
                if (original == null) continue;

                voicedChords.Add(new VoicedChord
                {
                    Symbol = original,
                    Pitches = voicings[i].Pitches.ToList()
                });
            }

            var response = new VoiceLeadResponse
            {
                Voicings = voicedChords,
                Violations = sdkViolations.Count > 0 ? sdkViolations.ToList() : null
            };

            await Task.CompletedTask;
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying voice leading");
            await _messageBus.TryPublishErrorAsync(
                "music",
                "ApplyVoiceLeading",
                "voice_leading_failed",
                ex.Message,
                dependency: null,
                endpoint: "post:/music/theory/voice-lead",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Private Helpers

    private static StyleDefinition? GetStyleDefinition(string styleId)
    {
        return BuiltInStyles.GetById(styleId);
    }

    // ToPitchClass and ToApiPitchClass removed - PitchClass has x-sdk-type (identity conversion)

    private static ModeType ToModeType(KeySignatureMode mode) => mode switch
    {
        KeySignatureMode.Major => ModeType.Major,
        KeySignatureMode.Minor => ModeType.Minor,
        KeySignatureMode.Dorian => ModeType.Dorian,
        KeySignatureMode.Phrygian => ModeType.Phrygian,
        KeySignatureMode.Lydian => ModeType.Lydian,
        KeySignatureMode.Mixolydian => ModeType.Mixolydian,
        KeySignatureMode.Aeolian => ModeType.Minor,
        KeySignatureMode.Locrian => ModeType.Locrian,
        _ => ModeType.Major
    };

    private static KeySignatureMode ToApiMode(ModeType mode) => mode switch
    {
        ModeType.Major => KeySignatureMode.Major,
        ModeType.Minor => KeySignatureMode.Minor,
        ModeType.Dorian => KeySignatureMode.Dorian,
        ModeType.Phrygian => KeySignatureMode.Phrygian,
        ModeType.Lydian => KeySignatureMode.Lydian,
        ModeType.Mixolydian => KeySignatureMode.Mixolydian,
        ModeType.Locrian => KeySignatureMode.Locrian,
        _ => KeySignatureMode.Major
    };

    private static ChordQuality ToChordQuality(ChordSymbolQuality quality) => quality switch
    {
        ChordSymbolQuality.Major => ChordQuality.Major,
        ChordSymbolQuality.Minor => ChordQuality.Minor,
        ChordSymbolQuality.Diminished => ChordQuality.Diminished,
        ChordSymbolQuality.Augmented => ChordQuality.Augmented,
        ChordSymbolQuality.Dominant7 => ChordQuality.Dominant7,
        ChordSymbolQuality.Major7 => ChordQuality.Major7,
        ChordSymbolQuality.Minor7 => ChordQuality.Minor7,
        ChordSymbolQuality.Diminished7 => ChordQuality.Diminished7,
        ChordSymbolQuality.HalfDiminished7 => ChordQuality.HalfDiminished7,
        ChordSymbolQuality.Augmented7 => ChordQuality.Augmented7,
        ChordSymbolQuality.Sus2 => ChordQuality.Sus2,
        ChordSymbolQuality.Sus4 => ChordQuality.Sus4,
        _ => ChordQuality.Major
    };

    private static ChordSymbolQuality ToApiChordQuality(ChordQuality quality) => quality switch
    {
        ChordQuality.Major => ChordSymbolQuality.Major,
        ChordQuality.Minor => ChordSymbolQuality.Minor,
        ChordQuality.Diminished => ChordSymbolQuality.Diminished,
        ChordQuality.Augmented => ChordSymbolQuality.Augmented,
        ChordQuality.Dominant7 => ChordSymbolQuality.Dominant7,
        ChordQuality.Major7 => ChordSymbolQuality.Major7,
        ChordQuality.Minor7 => ChordSymbolQuality.Minor7,
        ChordQuality.Diminished7 => ChordSymbolQuality.Diminished7,
        ChordQuality.HalfDiminished7 => ChordSymbolQuality.HalfDiminished7,
        ChordQuality.Augmented7 => ChordSymbolQuality.Augmented7,
        ChordQuality.Sus2 => ChordSymbolQuality.Sus2,
        ChordQuality.Sus4 => ChordSymbolQuality.Sus4,
        _ => ChordSymbolQuality.Major
    };

    private static ContourShape ToContourShape(GenerateMelodyRequestContour contour) => contour switch
    {
        GenerateMelodyRequestContour.Arch => ContourShape.Arch,
        GenerateMelodyRequestContour.Wave => ContourShape.Wave,
        GenerateMelodyRequestContour.Ascending => ContourShape.Ascending,
        GenerateMelodyRequestContour.Descending => ContourShape.Descending,
        GenerateMelodyRequestContour.Static => ContourShape.Static,
        _ => ContourShape.Arch
    };

    private static FunctionalAnalysis ToApiFunctionalAnalysis(HarmonicFunctionType function) =>
        function switch
        {
            HarmonicFunctionType.Tonic => FunctionalAnalysis.Tonic,
            HarmonicFunctionType.Subdominant => FunctionalAnalysis.Subdominant,
            HarmonicFunctionType.Dominant => FunctionalAnalysis.Dominant,
            _ => FunctionalAnalysis.Tonic
        };

    /// <summary>
    /// Derives functional analysis from scale degree.
    /// </summary>
    private static FunctionalAnalysis GetFunctionalAnalysis(int degree) =>
        degree switch
        {
            1 => FunctionalAnalysis.Tonic,       // I
            3 => FunctionalAnalysis.Tonic,       // iii (tonic function)
            6 => FunctionalAnalysis.Tonic,       // vi (tonic function)
            2 => FunctionalAnalysis.Subdominant, // ii (predominant/subdominant)
            4 => FunctionalAnalysis.Subdominant, // IV
            5 => FunctionalAnalysis.Dominant,    // V
            7 => FunctionalAnalysis.Dominant,    // vii° (leading tone, dominant function)
            _ => FunctionalAnalysis.Tonic
        };

    private static string GetRomanNumeral(int index) => (index % 7) switch
    {
        0 => "I",
        1 => "ii",
        2 => "iii",
        3 => "IV",
        4 => "V",
        5 => "vi",
        6 => "vii°",
        _ => "I"
    };

    // ConvertToApiMidiJson removed - SDK types used directly via x-sdk-type

    private static StyleDefinitionResponse ConvertToStyleResponse(StyleDefinition style)
    {
        return new StyleDefinitionResponse
        {
            StyleId = style.Id,
            Name = style.Name,
            Category = style.Category,
            Description = style.Description,
            DefaultTempo = style.DefaultTempo,
            ModeDistribution = new ModeDistribution
            {
                Major = (float)style.ModeDistribution.Major,
                Minor = (float)style.ModeDistribution.Minor,
                Dorian = (float)style.ModeDistribution.Dorian,
                Phrygian = (float)style.ModeDistribution.Phrygian,
                Lydian = (float)style.ModeDistribution.Lydian,
                Mixolydian = (float)style.ModeDistribution.Mixolydian
            },
            IntervalPreferences = new IntervalPreferences
            {
                StepWeight = (float)style.IntervalPreferences.StepWeight,
                ThirdWeight = (float)style.IntervalPreferences.ThirdWeight,
                LeapWeight = (float)style.IntervalPreferences.LeapWeight,
                LargeLeapWeight = (float)style.IntervalPreferences.LargeLeapWeight
            },
            FormTemplates = style.FormTemplates.Select(f => new FormTemplate
            {
                Name = f.Name,
                Sections = f.Sections.Select(s => s.Label).ToList(),
                BarsPerSection = f.DefaultBarsPerSection
            }).ToList(),
            TuneTypes = style.TuneTypes.Select(t => new TuneType
            {
                Name = t.Name,
                Meter = new TimeSignatureEvent
                {
                    Tick = 0,
                    Numerator = t.Meter.Numerator,
                    Denominator = t.Meter.Denominator
                },
                TempoRange = new TempoRange
                {
                    Min = t.TempoRange.min,
                    Max = t.TempoRange.max
                },
                DefaultForm = t.DefaultForm,
                RhythmPatterns = t.RhythmPatterns
            }).ToList(),
            HarmonyStyle = style.HarmonyStyle != null ? new HarmonyStyle
            {
                PrimaryCadence = HarmonyStylePrimaryCadence.Authentic,
                DominantPrepProbability = (float)style.HarmonyStyle.DominantPrepProbability,
                SecondaryDominantProbability = (float)style.HarmonyStyle.SecondaryDominantProbability,
                ModalInterchangeProbability = (float)style.HarmonyStyle.ModalInterchangeProbability,
                CommonProgressions = style.HarmonyStyle.CommonProgressions
            } : null
        };
    }

    #endregion
}
