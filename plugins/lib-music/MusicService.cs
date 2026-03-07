using BeyondImmersion.Bannou.MusicStoryteller;
using BeyondImmersion.Bannou.MusicStoryteller.Narratives;
using BeyondImmersion.Bannou.MusicStoryteller.Planning;
using BeyondImmersion.Bannou.MusicStoryteller.State;
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
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SdkChordQuality = BeyondImmersion.Bannou.MusicTheory.Collections.ChordQuality;
using SdkContourShape = BeyondImmersion.Bannou.MusicTheory.Melody.ContourShape;

namespace BeyondImmersion.BannouService.Music;

/// <summary>
/// Implementation of the Music service.
/// Provides music theory computation and MIDI-JSON generation using the music-theory SDK.
/// </summary>
[BannouService("music", typeof(IMusicService), lifetime: ServiceLifetime.Scoped)]
public partial class MusicService : IMusicService
{
    private readonly IMessageBus _messageBus;
    /// <summary>Redis-backed cache for deterministic compositions keyed by request parameters.</summary>
    private readonly IStateStore<GenerateCompositionResponse> _compositionCache;
    private readonly ILogger<MusicService> _logger;
    private readonly MusicServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new MusicService instance.
    /// </summary>
    public MusicService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<MusicService> logger,
        MusicServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _compositionCache = stateStoreFactory.GetStore<GenerateCompositionResponse>(
            StateStoreDefinitions.MusicCompositions);
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Generates a complete musical composition using the specified style and constraints.
    /// Uses the Storyteller SDK for narrative-driven composition when mood or narrative options are provided.
    /// Caches compositions when an explicit seed is provided (deterministic generation).
    /// </summary>
    public async Task<(StatusCodes, GenerateCompositionResponse?)> GenerateCompositionAsync(
        GenerateCompositionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating composition with style {StyleId}", body.StyleId);

        // Get the style definition
        var style = GetStyleDefinition(body.StyleId);
        if (style == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Check cache for deterministic requests (explicit seed provided)
        var cacheKey = body.Seed.HasValue ? BuildCompositionCacheKey(body) : null;
        if (cacheKey != null)
        {
            var cached = await TryGetCachedCompositionAsync(cacheKey, cancellationToken);
            if (cached != null)
            {
                _logger.LogDebug("Cache hit for composition {CacheKey}", cacheKey);
                return (StatusCodes.OK, cached);
            }
        }

        // Set up random generator
        var seed = body.Seed ?? Environment.TickCount;
        var random = new Random(seed);

        // Determine key signature (PitchClass has x-sdk-type, no conversion needed)
        var key = body.Key != null
            ? new Scale(body.Key.Tonic, TestableToModeType(body.Key.Mode))
            : new Scale(
                (PitchClass)random.Next(12),
                style.ModeDistribution.Select(random));

        // Determine tempo
        var tempo = body.Tempo ?? style.DefaultTempo;

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

        // Calculate ticks (use configuration for MIDI resolution)
        var ticksPerBeat = _configuration.DefaultTicksPerBeat;
        var totalBars = body.DurationBars;

        // Use Storyteller for narrative-driven composition
        var storyteller = new Storyteller();
        var compositionRequest = BuildStorytellerRequest(body, totalBars);
        var storyResult = storyteller.Compose(compositionRequest);

        _logger.LogDebug(
            "Storyteller generated {IntentCount} intents across {SectionCount} sections using template {TemplateId}",
            storyResult.TotalIntents,
            storyResult.Sections.Count,
            storyResult.Narrative.Id);

        // Generate chord progression guided by Storyteller intents
        var progressionGenerator = new ProgressionGenerator(seed);
        var chordsPerBar = _configuration.DefaultChordsPerBar;
        var progression = progressionGenerator.Generate(key, totalBars * chordsPerBar);

        // Voice the chords
        var voiceLeader = new VoiceLeader();
        var (voicings, _) = voiceLeader.Voice(
            progression.Select(p => p.Chord).ToList(),
            voiceCount: _configuration.DefaultVoiceCount);

        // Generate melody over the harmony with Storyteller-guided contour
        var melodyOptions = new MelodyOptions
        {
            Range = PitchRange.Vocal.Soprano,
            Contour = GetContourFromStoryResult(storyResult),
            IntervalPreferences = style.IntervalPreferences,
            Density = GetDensityFromStoryResult(storyResult),
            Syncopation = _configuration.DefaultMelodySyncopation,
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

        // Build response with narrative metadata
        var response = new GenerateCompositionResponse
        {
            CompositionId = Guid.NewGuid(),
            MidiJson = midiJson,
            Metadata = new CompositionMetadata
            {
                StyleId = body.StyleId,
                Key = new KeySignature
                {
                    Tonic = key.Root,
                    Mode = TestableToApiMode(key.Mode)
                },
                Tempo = tempo,
                Bars = totalBars,
                TuneType = body.TuneType,
                Seed = seed
            },
            NarrativeUsed = storyResult.Narrative.Id,
            EmotionalJourney = BuildEmotionalJourney(storyResult),
            TensionCurve = BuildTensionCurve(storyResult, totalBars)
        };

        // Cache deterministic compositions (explicit seed provided)
        if (cacheKey != null)
        {
            await CacheCompositionAsync(cacheKey, response, cancellationToken);
        }

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Validates a MIDI-JSON structure for correctness.
    /// </summary>
    public async Task<(StatusCodes, ValidateMidiJsonResponse?)> ValidateMidiJsonAsync(
        ValidateMidiJsonRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Validating MIDI-JSON structure");

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

    /// <summary>
    /// Gets a style definition by ID or name.
    /// </summary>
    public async Task<(StatusCodes, StyleDefinitionResponse?)> GetStyleAsync(
        GetStyleRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting style {StyleId} / {StyleName}", body.StyleId, body.StyleName);

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

    /// <summary>
    /// Lists all available style definitions.
    /// </summary>
    public async Task<(StatusCodes, ListStylesResponse?)> ListStylesAsync(
        ListStylesRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing styles with category filter {Category}", body.Category);

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

    /// <summary>
    /// Creates a new style definition.
    /// </summary>
    public async Task<(StatusCodes, StyleDefinitionResponse?)> CreateStyleAsync(
        CreateStyleRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating style {Name}", body.Name);

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

    /// <summary>
    /// Generates a chord progression using harmonic function theory.
    /// </summary>
    public async Task<(StatusCodes, GenerateProgressionResponse?)> GenerateProgressionAsync(
        GenerateProgressionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating progression in {Tonic} {Mode}", body.Key.Tonic, body.Key.Mode);

        var seed = body.Seed ?? Environment.TickCount;
        var scale = new Scale(body.Key.Tonic, TestableToModeType(body.Key.Mode));
        var generator = new ProgressionGenerator(seed);

        var length = body.Length ?? _configuration.DefaultProgressionLength;
        var progression = generator.Generate(scale, length);

        // Convert to API response
        var ticksPerBeat = _configuration.DefaultTicksPerBeat;
        var beatsPerChord = _configuration.DefaultBeatsPerChord;
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

    /// <summary>
    /// Generates a melody over a chord progression.
    /// </summary>
    public async Task<(StatusCodes, GenerateMelodyResponse?)> GenerateMelodyAsync(
        GenerateMelodyRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating melody over {ChordCount} chords", body.Harmony?.Count ?? 0);

        var seed = body.Seed ?? Environment.TickCount;

        // Infer key from first chord (or default to C major)
        var firstChord = body.Harmony?.FirstOrDefault();
        var root = firstChord?.Chord.Root ?? PitchClass.C;
        var mode = ModeType.Major;
        var scale = new Scale(root, mode);

        // Convert harmony to SDK progression (PitchClass has x-sdk-type, no conversion needed)
        var progression = body.Harmony?.Select((ce, i) => new ProgressionChord(
            new Chord(ce.Chord.Root, ToSdkChordQuality(ce.Chord.Quality)),
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
            ? ToSdkContourShape(body.Contour.Value)
            : SdkContourShape.Arch;

        var options = new MelodyOptions
        {
            Range = range,
            Contour = contour,
            Density = body.RhythmDensity ?? _configuration.DefaultMelodyDensity,
            Syncopation = body.Syncopation ?? _configuration.DefaultMelodySyncopation,
            TicksPerBeat = _configuration.DefaultTicksPerBeat
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
            Contour = ToApiContourShape(contour),
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

    /// <summary>
    /// Applies voice leading rules to a chord sequence.
    /// </summary>
    public async Task<(StatusCodes, VoiceLeadResponse?)> ApplyVoiceLeadingAsync(
        VoiceLeadRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying voice leading to {ChordCount} chords", body.Chords?.Count ?? 0);

        // Convert API chords to SDK chords (PitchClass has x-sdk-type, no conversion needed)
        var chords = body.Chords?.Select(cs =>
            new Chord(cs.Root, ToSdkChordQuality(cs.Quality), cs.Bass)
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
        var (voicings, sdkViolations) = voiceLeader.Voice(chords, voiceCount: _configuration.DefaultVoiceCount);

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

    #region Private Helpers

    private static StyleDefinition? GetStyleDefinition(string styleId)
    {
        return BuiltInStyles.GetById(styleId);
    }

    /// <summary>
    /// Builds a Storyteller CompositionRequest from the API request.
    /// Maps mood to narrative template and emotional preset per design spec.
    /// </summary>
    private CompositionRequest BuildStorytellerRequest(
        GenerateCompositionRequest body,
        int totalBars)
    {
        // Use explicit narrative options if provided
        if (body.Narrative != null)
        {
            var request = new CompositionRequest
            {
                TotalBars = totalBars,
                TemplateId = body.Narrative.TemplateId,
                AllowModulation = true
            };

            if (body.Narrative.InitialEmotion != null)
            {
                request.InitialEmotion = ToSdkEmotionalState(body.Narrative.InitialEmotion);
            }

            if (body.Narrative.TargetEmotion != null)
            {
                request.TargetEmotion = ToSdkEmotionalState(body.Narrative.TargetEmotion);
            }

            return request;
        }

        // Infer from mood using the design spec mapping:
        // bright → SimpleArc + Joyful
        // dark → TensionAndRelease + Tense
        // neutral → JourneyAndReturn + Neutral
        // melancholic → SimpleArc + Melancholic
        // triumphant → TensionAndRelease + Climax→Resolution
        var (templateId, initialEmotion, targetEmotion) = body.Mood switch
        {
            Mood.Bright => (
                "simple_arc",
                EmotionalState.Presets.Joyful,
                (EmotionalState?)null),
            Mood.Dark => (
                "tension_and_release",
                EmotionalState.Presets.Tense,
                (EmotionalState?)null),
            Mood.Neutral => (
                "journey_and_return",
                EmotionalState.Presets.Neutral,
                (EmotionalState?)null),
            Mood.Melancholic => (
                "simple_arc",
                EmotionalState.Presets.Melancholic,
                (EmotionalState?)null),
            Mood.Triumphant => (
                "tension_and_release",
                EmotionalState.Presets.Climax,
                EmotionalState.Presets.Resolution),
            _ => (
                "simple_arc",
                EmotionalState.Presets.Neutral,
                (EmotionalState?)null)
        };

        return new CompositionRequest
        {
            TotalBars = totalBars,
            TemplateId = templateId,
            InitialEmotion = initialEmotion,
            TargetEmotion = targetEmotion,
            AllowModulation = true
        };
    }

    /// <summary>
    /// Converts API EmotionalStateInput to SDK EmotionalState using configuration defaults.
    /// </summary>
    private EmotionalState ToSdkEmotionalState(EmotionalStateInput input)
    {
        return new EmotionalState(
            input.Tension ?? _configuration.DefaultEmotionalTension,
            input.Brightness ?? _configuration.DefaultEmotionalBrightness,
            input.Energy ?? _configuration.DefaultEmotionalEnergy,
            input.Warmth ?? _configuration.DefaultEmotionalWarmth,
            input.Stability ?? _configuration.DefaultEmotionalStability,
            input.Valence ?? _configuration.DefaultEmotionalValence);
    }

    /// <summary>
    /// Determines melody contour from Storyteller result.
    /// Maps narrative arc to contour shape.
    /// Uses configuration for thresholds per IMPLEMENTATION TENETS (no hardcoded magic numbers).
    /// </summary>
    private SdkContourShape GetContourFromStoryResult(CompositionResult result)
    {
        // Use the emotional trajectory to determine contour
        var finalTension = result.FinalState.Emotional.Tension;
        var initialTension = result.Sections.FirstOrDefault()?.Plan.Goal.TargetState
            .Get<double>(WorldState.Keys.Tension);

        // Use configuration values for thresholds (per IMPLEMENTATION TENETS)
        var defaultTension = _configuration.ContourDefaultTension;
        var tensionThreshold = _configuration.ContourTensionThreshold;

        // If tension generally increases, use ascending contour
        if (finalTension > (initialTension ?? defaultTension) + tensionThreshold)
        {
            return SdkContourShape.Ascending;
        }

        // If tension generally decreases, use descending contour
        if (finalTension < (initialTension ?? defaultTension) - tensionThreshold)
        {
            return SdkContourShape.Descending;
        }

        // For dramatic arcs (tension_and_release), use arch
        if (result.Narrative.Id == "tension_and_release")
        {
            return SdkContourShape.Arch;
        }

        // For journey narratives, use wave
        if (result.Narrative.Id == "journey_and_return")
        {
            return SdkContourShape.Wave;
        }

        // Default to arch for most pleasing melodic shape
        return SdkContourShape.Arch;
    }

    /// <summary>
    /// Determines melody density from Storyteller result.
    /// Higher energy phases get denser melodies.
    /// Uses configuration for scaling per IMPLEMENTATION TENETS (no hardcoded magic numbers).
    /// </summary>
    private double GetDensityFromStoryResult(CompositionResult result)
    {
        // Average energy across all phases
        var avgEnergy = result.Sections.Count > 0
            ? result.Sections.Average(s =>
                s.Plan.Goal.TargetState.Get<double>(WorldState.Keys.Energy))
            : _configuration.DefaultEmotionalEnergy;

        // Map energy to density using configuration (per IMPLEMENTATION TENETS)
        return _configuration.DensityMinimum + avgEnergy * _configuration.DensityEnergyMultiplier;
    }

    /// <summary>
    /// Builds the emotional journey snapshots for the API response.
    /// </summary>
    private static List<EmotionalStateSnapshot> BuildEmotionalJourney(
        CompositionResult result)
    {
        var snapshots = new List<EmotionalStateSnapshot>();

        foreach (var section in result.Sections)
        {
            // Get target emotional state for this phase
            var target = section.Plan.Goal.TargetState;

            snapshots.Add(new EmotionalStateSnapshot
            {
                Bar = section.StartBar,
                PhaseName = section.PhaseName,
                Tension = target.Get<double>(WorldState.Keys.Tension),
                Brightness = target.Get<double>(WorldState.Keys.Brightness),
                Energy = target.Get<double>(WorldState.Keys.Energy),
                Warmth = target.Get<double>(WorldState.Keys.Warmth),
                Stability = target.Get<double>(WorldState.Keys.Stability),
                Valence = target.Get<double>(WorldState.Keys.Valence)
            });
        }

        // Add final state at the end
        var finalEmotional = result.FinalState.Emotional;
        snapshots.Add(new EmotionalStateSnapshot
        {
            Bar = result.TotalBars,
            PhaseName = "End",
            Tension = finalEmotional.Tension,
            Brightness = finalEmotional.Brightness,
            Energy = finalEmotional.Energy,
            Warmth = finalEmotional.Warmth,
            Stability = finalEmotional.Stability,
            Valence = finalEmotional.Valence
        });

        return snapshots;
    }

    /// <summary>
    /// Builds the tension curve for the API response.
    /// Interpolates tension values at each bar.
    /// </summary>
    private static List<double> BuildTensionCurve(
        CompositionResult result,
        int totalBars)
    {
        var curve = new List<double>();

        // Get section boundaries with their tension targets
        var sectionTensions = result.Sections
            .Select(s => (
                StartBar: s.StartBar,
                EndBar: s.EndBar,
                Tension: s.Plan.Goal.TargetState.Get<double>(WorldState.Keys.Tension)))
            .ToList();

        for (int bar = 1; bar <= totalBars; bar++)
        {
            // Find which section this bar belongs to
            var section = sectionTensions.FirstOrDefault(s => bar >= s.StartBar && bar <= s.EndBar);

            if (section != default)
            {
                // Interpolate within the section
                var progress = section.EndBar > section.StartBar
                    ? (double)(bar - section.StartBar) / (section.EndBar - section.StartBar)
                    : 1.0;

                // Get previous section's tension for interpolation
                var sectionIndex = sectionTensions.IndexOf(section);
                var prevTension = sectionIndex > 0
                    ? sectionTensions[sectionIndex - 1].Tension
                    : result.Request.InitialEmotion?.Tension ?? 0.2;

                // Linear interpolation from previous to current
                var tension = prevTension + (section.Tension - prevTension) * progress;
                curve.Add(Math.Clamp(tension, 0, 1));
            }
            else
            {
                // Fallback to final tension
                curve.Add(result.FinalState.Emotional.Tension);
            }
        }

        return curve;
    }

    // ToPitchClass and ToApiPitchClass removed - PitchClass has x-sdk-type (identity conversion)

    // internal for switch coverage unit testing via EnumMappingValidator
    internal static ModeType TestableToModeType(KeyMode mode) => mode switch
    {
        KeyMode.Major => ModeType.Major,
        KeyMode.Minor => ModeType.Minor,
        KeyMode.Dorian => ModeType.Dorian,
        KeyMode.Phrygian => ModeType.Phrygian,
        KeyMode.Lydian => ModeType.Lydian,
        KeyMode.Mixolydian => ModeType.Mixolydian,
        KeyMode.Aeolian => ModeType.Minor,
        KeyMode.Locrian => ModeType.Locrian,
        _ => ModeType.Major
    };

    // internal for switch coverage unit testing via EnumMappingValidator
    internal static KeyMode TestableToApiMode(ModeType mode) => mode switch
    {
        ModeType.Major => KeyMode.Major,
        ModeType.Minor => KeyMode.Minor,
        ModeType.Dorian => KeyMode.Dorian,
        ModeType.Phrygian => KeyMode.Phrygian,
        ModeType.Lydian => KeyMode.Lydian,
        ModeType.Mixolydian => KeyMode.Mixolydian,
        ModeType.Locrian => KeyMode.Locrian,
        _ => KeyMode.Major
    };

    // Schema is subset of SDK — every schema value has a matching name in SDK
    private static SdkChordQuality ToSdkChordQuality(ChordQuality quality) =>
        quality.MapByName<ChordQuality, SdkChordQuality>();

    // SDK is superset (has MinorMajor7, Power) — extras fall back to Major
    private static ChordQuality ToApiChordQuality(SdkChordQuality quality) =>
        quality.MapByNameOrDefault(ChordQuality.Major);

    // Schema is subset of SDK — every schema value has a matching name in SDK
    private static SdkContourShape ToSdkContourShape(ContourShape contour) =>
        contour.MapByName<ContourShape, SdkContourShape>();

    // SDK is superset (has InvertedArch, Free) — extras fall back to Arch
    private static ContourShape ToApiContourShape(SdkContourShape contour) =>
        contour.MapByNameOrDefault(ContourShape.Arch);

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
                PrimaryCadence = CadenceType.Authentic,
                DominantPrepProbability = (float)style.HarmonyStyle.DominantPrepProbability,
                SecondaryDominantProbability = (float)style.HarmonyStyle.SecondaryDominantProbability,
                ModalInterchangeProbability = (float)style.HarmonyStyle.ModalInterchangeProbability,
                CommonProgressions = style.HarmonyStyle.CommonProgressions
            } : null
        };
    }

    /// <summary>
    /// Builds a deterministic cache key from composition request parameters.
    /// Only called when seed is explicitly provided (deterministic generation).
    /// </summary>
    private static string BuildCompositionCacheKey(GenerateCompositionRequest request)
    {
        // Include all parameters that affect output
        var keyParts = new List<string>
        {
            request.StyleId,
            request.Seed?.ToString() ?? "0",
            request.DurationBars.ToString()
        };

        if (request.Key != null)
        {
            keyParts.Add($"key:{request.Key.Tonic}:{request.Key.Mode}");
        }

        if (request.Tempo.HasValue)
        {
            keyParts.Add($"tempo:{request.Tempo.Value}");
        }

        if (!string.IsNullOrEmpty(request.TuneType))
        {
            keyParts.Add($"tune:{request.TuneType}");
        }

        keyParts.Add($"mood:{request.Mood}");

        if (request.Narrative != null)
        {
            keyParts.Add($"narrative:{request.Narrative.TemplateId}");
        }

        return string.Join(":", keyParts);
    }

    /// <summary>
    /// Attempts to retrieve a cached composition from Redis.
    /// </summary>
    private async Task<GenerateCompositionResponse?> TryGetCachedCompositionAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.music", "MusicService.TryGetCachedCompositionAsync");
        try
        {
            return await _compositionCache.GetAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex)
        {
            // Cache miss or error - proceed with generation
            _logger.LogWarning(ex, "Cache lookup failed for {CacheKey}", cacheKey);
            return null;
        }
    }

    /// <summary>
    /// Caches a generated composition for future requests with the same parameters.
    /// </summary>
    private async Task CacheCompositionAsync(
        string cacheKey,
        GenerateCompositionResponse response,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.music", "MusicService.CacheCompositionAsync");
        try
        {
            // Compositions with explicit seed are deterministic - cache with configured TTL
            await _compositionCache.SaveAsync(cacheKey, response,
                new StateOptions { Ttl = _configuration.CompositionCacheTtlSeconds }, cancellationToken);
            _logger.LogDebug("Cached composition {CacheKey}", cacheKey);
        }
        catch (Exception ex)
        {
            // Cache write failure is non-fatal
            _logger.LogWarning(ex, "Failed to cache composition {CacheKey}", cacheKey);
        }
    }

    #endregion
}
