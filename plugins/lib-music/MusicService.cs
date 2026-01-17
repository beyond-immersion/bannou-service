using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

// Music Theory SDK
using MusicTheory = BeyondImmersion.Bannou.MusicTheory;
using SdkPitchClass = BeyondImmersion.Bannou.MusicTheory.Pitch.PitchClass;
using SdkPitch = BeyondImmersion.Bannou.MusicTheory.Pitch.Pitch;
using SdkScale = BeyondImmersion.Bannou.MusicTheory.Collections.Scale;
using SdkChord = BeyondImmersion.Bannou.MusicTheory.Collections.Chord;
using SdkVoicing = BeyondImmersion.Bannou.MusicTheory.Collections.Voicing;
using SdkModeType = BeyondImmersion.Bannou.MusicTheory.Collections.ModeType;
using SdkChordQuality = BeyondImmersion.Bannou.MusicTheory.Collections.ChordQuality;
using SdkMeter = BeyondImmersion.Bannou.MusicTheory.Time.Meter;
using SdkStyleDefinition = BeyondImmersion.Bannou.MusicTheory.Style.StyleDefinition;
using SdkBuiltInStyles = BeyondImmersion.Bannou.MusicTheory.Style.BuiltInStyles;
using SdkMidiJson = BeyondImmersion.Bannou.MusicTheory.Output.MidiJson;
using SdkMidiJsonRenderer = BeyondImmersion.Bannou.MusicTheory.Output.MidiJsonRenderer;
using SdkMelodyGenerator = BeyondImmersion.Bannou.MusicTheory.Melody.MelodyGenerator;
using SdkMelodyOptions = BeyondImmersion.Bannou.MusicTheory.Melody.MelodyGeneratorOptions;
using SdkContourShape = BeyondImmersion.Bannou.MusicTheory.Melody.ContourShape;
using SdkProgressionGenerator = BeyondImmersion.Bannou.MusicTheory.Harmony.ProgressionGenerator;
using SdkProgressionChord = BeyondImmersion.Bannou.MusicTheory.Harmony.ProgressionChord;
using SdkVoiceLeader = BeyondImmersion.Bannou.MusicTheory.Harmony.VoiceLeader;
using SdkVoiceLeadingRules = BeyondImmersion.Bannou.MusicTheory.Harmony.VoiceLeadingRules;
using SdkPitchRange = BeyondImmersion.Bannou.MusicTheory.Pitch.PitchRange;

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

            // Determine key signature
            var key = body.Key != null
                ? new SdkScale(ToSdkPitchClass(body.Key.Tonic), ToSdkModeType(body.Key.Mode))
                : new SdkScale(
                    (SdkPitchClass)random.Next(12),
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
            var progressionGenerator = new SdkProgressionGenerator(key, random);
            var chordsPerBar = 1; // One chord per bar for simplicity
            var progression = progressionGenerator.Generate(totalBars * chordsPerBar);

            // Voice the chords
            var voiceLeader = new SdkVoiceLeader();
            var voicings = voiceLeader.VoiceProgression(
                progression.Select(p => p.Chord).ToList(),
                SdkPitchRange.Instrument.Piano);

            // Generate melody over the harmony
            var melodyOptions = new SdkMelodyOptions
            {
                Range = SdkPitchRange.Vocal.Soprano,
                Contour = SdkContourShape.Arch,
                IntervalPreferences = style.IntervalPreferences,
                Density = 0.7,
                Syncopation = 0.2
            };

            var melodyGenerator = new SdkMelodyGenerator(key, random, melodyOptions);
            var melody = melodyGenerator.GenerateOverProgression(progression, ticksPerBeat);

            // Render to MIDI-JSON
            var midiJson = SdkMidiJsonRenderer.RenderComposition(
                melody,
                voicings,
                tempo,
                meter,
                key,
                ticksPerBeat,
                $"Generated {style.Name} Composition");

            stopwatch.Stop();

            // Convert SDK output to API response
            var response = new GenerateCompositionResponse
            {
                CompositionId = Guid.NewGuid().ToString(),
                MidiJson = ConvertToApiMidiJson(midiJson),
                GenerationTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Metadata = new CompositionMetadata
                {
                    StyleId = body.StyleId,
                    Key = new KeySignature
                    {
                        Tonic = ToApiPitchClass(key.Root),
                        Mode = ToApiMode(key.Mode)
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
            SdkStyleDefinition? style = null;

            if (!string.IsNullOrEmpty(body.StyleId))
            {
                style = SdkBuiltInStyles.GetById(body.StyleId);
            }
            else if (!string.IsNullOrEmpty(body.StyleName))
            {
                style = SdkBuiltInStyles.All.FirstOrDefault(s =>
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
            var styles = SdkBuiltInStyles.All.AsEnumerable();

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

            var scale = new SdkScale(ToSdkPitchClass(body.Key.Tonic), ToSdkModeType(body.Key.Mode));
            var generator = new SdkProgressionGenerator(scale, random);

            var length = body.Length > 0 ? body.Length : 8;
            var progression = generator.Generate(length);

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
                        Root = ToApiPitchClass(chord.Chord.Root),
                        Quality = ToApiChordQuality(chord.Chord.Quality),
                        Bass = chord.Chord.Bass.HasValue ? ToApiPitchClass(chord.Chord.Bass.Value) : null
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
                FunctionalAnalysis = progression.Select(p => ToApiFunctionalAnalysis(p.Function)).ToList()
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
            var root = firstChord != null ? ToSdkPitchClass(firstChord.Chord.Root) : SdkPitchClass.C;
            var mode = SdkModeType.Major;
            var scale = new SdkScale(root, mode);

            // Convert harmony to SDK progression
            var progression = body.Harmony?.Select((ce, i) => new SdkProgressionChord(
                new SdkChord(ToSdkPitchClass(ce.Chord.Root), ToSdkChordQuality(ce.Chord.Quality)),
                ce.RomanNumeral ?? GetRomanNumeral(i),
                i + 1,
                ce.DurationTicks / 480.0
            )).ToList() ?? [];

            // Set up melody options
            var range = body.Range != null
                ? new SdkPitchRange(
                    new SdkPitch(ToSdkPitchClass(body.Range.Low.PitchClass), body.Range.Low.Octave),
                    new SdkPitch(ToSdkPitchClass(body.Range.High.PitchClass), body.Range.High.Octave))
                : SdkPitchRange.Vocal.Soprano;

            var contour = body.Contour.HasValue
                ? ToSdkContourShape(body.Contour.Value)
                : SdkContourShape.Arch;

            var options = new SdkMelodyOptions
            {
                Range = range,
                Contour = contour,
                Density = body.RhythmDensity ?? 0.7,
                Syncopation = body.Syncopation ?? 0.2
            };

            var generator = new SdkMelodyGenerator(scale, random, options);
            var melody = generator.GenerateOverProgression(progression, 480);

            // Convert to API response
            var notes = melody.Select(n => new NoteEvent
            {
                Pitch = new Pitch
                {
                    PitchClass = ToApiPitchClass(n.Pitch.Class),
                    Octave = n.Pitch.Octave,
                    MidiNumber = n.Pitch.MidiNumber
                },
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
                    analysis.Range = new PitchRange
                    {
                        Low = minPitch.Pitch,
                        High = maxPitch.Pitch
                    };
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
            // Convert API chords to SDK chords
            var chords = body.Chords?.Select(cs =>
                new SdkChord(ToSdkPitchClass(cs.Root), ToSdkChordQuality(cs.Quality), cs.Bass.HasValue ? ToSdkPitchClass(cs.Bass.Value) : null)
            ).ToList() ?? [];

            // Set up voice leading rules
            var rules = new SdkVoiceLeadingRules
            {
                AvoidParallelFifths = body.Rules?.AvoidParallelFifths ?? true,
                AvoidParallelOctaves = body.Rules?.AvoidParallelOctaves ?? true,
                PreferStepwiseMotion = body.Rules?.PreferStepwiseMotion ?? true,
                AvoidVoiceCrossing = body.Rules?.AvoidVoiceCrossing ?? true,
                MaxLeapSemitones = body.Rules?.MaxLeap ?? 7
            };

            var voiceLeader = new SdkVoiceLeader(rules);

            // Determine voice range (default to piano range)
            var range = SdkPitchRange.Instrument.Piano;

            // Voice the progression
            var voicings = voiceLeader.VoiceProgression(chords, range);

            // Check for violations
            var violations = new List<VoiceLeadingViolation>();
            var sdkViolations = voiceLeader.CheckViolations(voicings);
            foreach (var (position, violation) in sdkViolations)
            {
                violations.Add(new VoiceLeadingViolation
                {
                    Rule = violation.Type.ToString(),
                    Position = position,
                    Voices = violation.VoiceIndices?.ToList(),
                    Severity = violation.IsCritical
                        ? VoiceLeadingViolationSeverity.Error
                        : VoiceLeadingViolationSeverity.Warning
                });
            }

            // Convert voicings to API response
            var voicedChords = new List<VoicedChord>();
            for (int i = 0; i < voicings.Count && i < (body.Chords?.Count ?? 0); i++)
            {
                var original = body.Chords?.ElementAt(i);
                if (original == null) continue;

                voicedChords.Add(new VoicedChord
                {
                    Symbol = original,
                    Pitches = voicings[i].Pitches.Select(p => new Pitch
                    {
                        PitchClass = ToApiPitchClass(p.Class),
                        Octave = p.Octave,
                        MidiNumber = p.MidiNumber
                    }).ToList()
                });
            }

            var response = new VoiceLeadResponse
            {
                Voicings = voicedChords,
                Violations = violations.Count > 0 ? violations : null
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

    private static SdkStyleDefinition? GetStyleDefinition(string styleId)
    {
        return SdkBuiltInStyles.GetById(styleId);
    }

    private static SdkPitchClass ToSdkPitchClass(PitchClass pc) => (SdkPitchClass)(int)pc;

    private static PitchClass ToApiPitchClass(SdkPitchClass pc) => (PitchClass)(int)pc;

    private static SdkModeType ToSdkModeType(KeySignatureMode mode) => mode switch
    {
        KeySignatureMode.Major => SdkModeType.Major,
        KeySignatureMode.Minor => SdkModeType.Minor,
        KeySignatureMode.Dorian => SdkModeType.Dorian,
        KeySignatureMode.Phrygian => SdkModeType.Phrygian,
        KeySignatureMode.Lydian => SdkModeType.Lydian,
        KeySignatureMode.Mixolydian => SdkModeType.Mixolydian,
        KeySignatureMode.Aeolian => SdkModeType.Minor,
        KeySignatureMode.Locrian => SdkModeType.Locrian,
        _ => SdkModeType.Major
    };

    private static KeySignatureMode ToApiMode(SdkModeType mode) => mode switch
    {
        SdkModeType.Major => KeySignatureMode.Major,
        SdkModeType.Minor => KeySignatureMode.Minor,
        SdkModeType.Dorian => KeySignatureMode.Dorian,
        SdkModeType.Phrygian => KeySignatureMode.Phrygian,
        SdkModeType.Lydian => KeySignatureMode.Lydian,
        SdkModeType.Mixolydian => KeySignatureMode.Mixolydian,
        SdkModeType.Locrian => KeySignatureMode.Locrian,
        _ => KeySignatureMode.Major
    };

    private static SdkChordQuality ToSdkChordQuality(ChordSymbolQuality quality) => quality switch
    {
        ChordSymbolQuality.Major => SdkChordQuality.Major,
        ChordSymbolQuality.Minor => SdkChordQuality.Minor,
        ChordSymbolQuality.Diminished => SdkChordQuality.Diminished,
        ChordSymbolQuality.Augmented => SdkChordQuality.Augmented,
        ChordSymbolQuality.Dominant7 => SdkChordQuality.Dominant7,
        ChordSymbolQuality.Major7 => SdkChordQuality.Major7,
        ChordSymbolQuality.Minor7 => SdkChordQuality.Minor7,
        ChordSymbolQuality.Diminished7 => SdkChordQuality.Diminished7,
        ChordSymbolQuality.HalfDiminished7 => SdkChordQuality.HalfDiminished7,
        ChordSymbolQuality.Augmented7 => SdkChordQuality.Augmented7,
        ChordSymbolQuality.Sus2 => SdkChordQuality.Sus2,
        ChordSymbolQuality.Sus4 => SdkChordQuality.Sus4,
        _ => SdkChordQuality.Major
    };

    private static ChordSymbolQuality ToApiChordQuality(SdkChordQuality quality) => quality switch
    {
        SdkChordQuality.Major => ChordSymbolQuality.Major,
        SdkChordQuality.Minor => ChordSymbolQuality.Minor,
        SdkChordQuality.Diminished => ChordSymbolQuality.Diminished,
        SdkChordQuality.Augmented => ChordSymbolQuality.Augmented,
        SdkChordQuality.Dominant7 => ChordSymbolQuality.Dominant7,
        SdkChordQuality.Major7 => ChordSymbolQuality.Major7,
        SdkChordQuality.Minor7 => ChordSymbolQuality.Minor7,
        SdkChordQuality.Diminished7 => ChordSymbolQuality.Diminished7,
        SdkChordQuality.HalfDiminished7 => ChordSymbolQuality.HalfDiminished7,
        SdkChordQuality.Augmented7 => ChordSymbolQuality.Augmented7,
        SdkChordQuality.Sus2 => ChordSymbolQuality.Sus2,
        SdkChordQuality.Sus4 => ChordSymbolQuality.Sus4,
        _ => ChordSymbolQuality.Major
    };

    private static SdkContourShape ToSdkContourShape(GenerateMelodyRequestContour contour) => contour switch
    {
        GenerateMelodyRequestContour.Arch => SdkContourShape.Arch,
        GenerateMelodyRequestContour.Wave => SdkContourShape.Wave,
        GenerateMelodyRequestContour.Ascending => SdkContourShape.Ascending,
        GenerateMelodyRequestContour.Descending => SdkContourShape.Descending,
        GenerateMelodyRequestContour.Static => SdkContourShape.Static,
        _ => SdkContourShape.Arch
    };

    private static FunctionalAnalysis ToApiFunctionalAnalysis(MusicTheory.Harmony.HarmonicFunctionType function) =>
        function switch
        {
            MusicTheory.Harmony.HarmonicFunctionType.Tonic => FunctionalAnalysis.Tonic,
            MusicTheory.Harmony.HarmonicFunctionType.Subdominant => FunctionalAnalysis.Subdominant,
            MusicTheory.Harmony.HarmonicFunctionType.Dominant => FunctionalAnalysis.Dominant,
            MusicTheory.Harmony.HarmonicFunctionType.Predominant => FunctionalAnalysis.Predominant,
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

    private static MidiJson ConvertToApiMidiJson(SdkMidiJson sdk)
    {
        var api = new MidiJson
        {
            TicksPerBeat = sdk.TicksPerBeat,
            Tracks = sdk.Tracks.Select(t => new MidiTrack
            {
                Name = t.Name,
                Channel = t.Channel,
                Instrument = t.Instrument,
                Events = t.Events.Select(e => new MidiEvent
                {
                    Tick = e.Tick,
                    Type = (MidiEventType)(int)e.Type,
                    Note = e.Note,
                    Velocity = e.Velocity,
                    Duration = e.Duration,
                    Program = e.Program,
                    Controller = e.Controller,
                    Value = e.Value
                }).ToList()
            }).ToList()
        };

        if (sdk.Header != null)
        {
            api.Header = new MidiHeader
            {
                Format = (MidiHeaderFormat)sdk.Header.Format,
                Name = sdk.Header.Name,
                Tempos = sdk.Header.Tempos?.Select(t => new TempoEvent
                {
                    Tick = t.Tick,
                    Bpm = (float)t.Bpm
                }).ToList(),
                TimeSignatures = sdk.Header.TimeSignatures?.Select(ts => new TimeSignatureEvent
                {
                    Tick = ts.Tick,
                    Numerator = ts.Numerator,
                    Denominator = (TimeSignatureEventDenominator)ts.Denominator
                }).ToList(),
                KeySignatures = sdk.Header.KeySignatures?.Select(ks => new KeySignatureEvent
                {
                    Tick = ks.Tick,
                    Key = new KeySignature
                    {
                        Tonic = ToApiPitchClass(ks.Tonic),
                        Mode = ToApiMode(ks.Mode)
                    }
                }).ToList()
            };
        }

        return api;
    }

    private static StyleDefinitionResponse ConvertToStyleResponse(SdkStyleDefinition style)
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
                    Denominator = (TimeSignatureEventDenominator)t.Meter.Denominator
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
