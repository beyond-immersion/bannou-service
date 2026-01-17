using System.Text.Json;
using System.Text.Json.Serialization;
using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Harmony;
using BeyondImmersion.Bannou.MusicTheory.Melody;
using BeyondImmersion.Bannou.MusicTheory.Pitch;
using BeyondImmersion.Bannou.MusicTheory.Time;

namespace BeyondImmersion.Bannou.MusicTheory.Output;

/// <summary>
/// MIDI event types.
/// </summary>
public enum MidiEventType
{
    /// <summary>Note on event</summary>
    NoteOn,

    /// <summary>Note off event</summary>
    NoteOff,

    /// <summary>Program (instrument) change</summary>
    ProgramChange,

    /// <summary>Control change</summary>
    ControlChange
}

/// <summary>
/// A single MIDI event.
/// </summary>
public sealed class MidiEvent
{
    /// <summary>
    /// Absolute tick position.
    /// </summary>
    [JsonPropertyName("tick")]
    public int Tick { get; set; }

    /// <summary>
    /// Event type.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MidiEventType Type { get; set; }

    /// <summary>
    /// MIDI note number (0-127).
    /// </summary>
    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Note { get; set; }

    /// <summary>
    /// Velocity (0-127).
    /// </summary>
    [JsonPropertyName("velocity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Velocity { get; set; }

    /// <summary>
    /// Duration in ticks (for noteOn with implicit noteOff).
    /// </summary>
    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Duration { get; set; }

    /// <summary>
    /// Program number for program change.
    /// </summary>
    [JsonPropertyName("program")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Program { get; set; }

    /// <summary>
    /// Controller number for control change.
    /// </summary>
    [JsonPropertyName("controller")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Controller { get; set; }

    /// <summary>
    /// Controller value.
    /// </summary>
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Value { get; set; }
}

/// <summary>
/// A MIDI track.
/// </summary>
public sealed class MidiTrack
{
    /// <summary>
    /// Track name.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// MIDI channel (0-15).
    /// </summary>
    [JsonPropertyName("channel")]
    public int Channel { get; set; }

    /// <summary>
    /// GM instrument number (0-127).
    /// </summary>
    [JsonPropertyName("instrument")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Instrument { get; set; }

    /// <summary>
    /// Track events.
    /// </summary>
    [JsonPropertyName("events")]
    public List<MidiEvent> Events { get; set; } = [];
}

/// <summary>
/// Tempo change event.
/// </summary>
public sealed class TempoEvent
{
    /// <summary>
    /// Tick position.
    /// </summary>
    [JsonPropertyName("tick")]
    public int Tick { get; set; }

    /// <summary>
    /// Tempo in BPM.
    /// </summary>
    [JsonPropertyName("bpm")]
    public double Bpm { get; set; }
}

/// <summary>
/// Time signature change event.
/// </summary>
public sealed class TimeSignatureEvent
{
    /// <summary>
    /// Tick position.
    /// </summary>
    [JsonPropertyName("tick")]
    public int Tick { get; set; }

    /// <summary>
    /// Beats per measure.
    /// </summary>
    [JsonPropertyName("numerator")]
    public int Numerator { get; set; }

    /// <summary>
    /// Beat unit (4 = quarter, 8 = eighth).
    /// </summary>
    [JsonPropertyName("denominator")]
    public int Denominator { get; set; }
}

/// <summary>
/// Key signature change event.
/// </summary>
public sealed class KeySignatureEvent
{
    /// <summary>
    /// Tick position.
    /// </summary>
    [JsonPropertyName("tick")]
    public int Tick { get; set; }

    /// <summary>
    /// Tonic pitch class.
    /// </summary>
    [JsonPropertyName("tonic")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PitchClass Tonic { get; set; }

    /// <summary>
    /// Mode.
    /// </summary>
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModeType Mode { get; set; }
}

/// <summary>
/// MIDI header information.
/// </summary>
public sealed class MidiHeader
{
    /// <summary>
    /// MIDI format (0, 1, or 2).
    /// </summary>
    [JsonPropertyName("format")]
    public int Format { get; set; } = 1;

    /// <summary>
    /// Composition name.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// Tempo changes.
    /// </summary>
    [JsonPropertyName("tempos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TempoEvent>? Tempos { get; set; }

    /// <summary>
    /// Time signature changes.
    /// </summary>
    [JsonPropertyName("timeSignatures")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TimeSignatureEvent>? TimeSignatures { get; set; }

    /// <summary>
    /// Key signature changes.
    /// </summary>
    [JsonPropertyName("keySignatures")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<KeySignatureEvent>? KeySignatures { get; set; }
}

/// <summary>
/// MIDI-JSON format representation.
/// </summary>
public sealed class MidiJson
{
    /// <summary>
    /// Header information.
    /// </summary>
    [JsonPropertyName("header")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MidiHeader? Header { get; set; }

    /// <summary>
    /// Ticks per beat (PPQN).
    /// </summary>
    [JsonPropertyName("ticksPerBeat")]
    public int TicksPerBeat { get; set; } = 480;

    /// <summary>
    /// MIDI tracks.
    /// </summary>
    [JsonPropertyName("tracks")]
    public List<MidiTrack> Tracks { get; set; } = [];

    /// <summary>
    /// Serializes to JSON string.
    /// </summary>
    public string ToJson(bool indented = false)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        return JsonSerializer.Serialize(this, options);
    }

    /// <summary>
    /// Deserializes from JSON string.
    /// </summary>
    public static MidiJson FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Deserialize<MidiJson>(json, options)
            ?? throw new ArgumentException("Failed to parse MIDI-JSON");
    }
}

/// <summary>
/// Renders musical elements to MIDI-JSON format.
/// </summary>
public static class MidiJsonRenderer
{
    /// <summary>
    /// Renders a melody to MIDI-JSON.
    /// </summary>
    /// <param name="notes">The melody notes.</param>
    /// <param name="ticksPerBeat">Ticks per beat.</param>
    /// <param name="trackName">Track name.</param>
    /// <param name="instrument">GM instrument number.</param>
    /// <returns>MIDI-JSON representation.</returns>
    public static MidiJson RenderMelody(
        IReadOnlyList<MelodyNote> notes,
        int ticksPerBeat = 480,
        string? trackName = "Melody",
        int instrument = 0)
    {
        var track = new MidiTrack
        {
            Name = trackName,
            Channel = 0,
            Instrument = instrument
        };

        foreach (var note in notes.OrderBy(n => n.StartTick))
        {
            track.Events.Add(new MidiEvent
            {
                Tick = note.StartTick,
                Type = MidiEventType.NoteOn,
                Note = note.Pitch.MidiNumber,
                Velocity = note.Velocity,
                Duration = note.DurationTicks
            });
        }

        return new MidiJson
        {
            TicksPerBeat = ticksPerBeat,
            Tracks = [track]
        };
    }

    /// <summary>
    /// Renders a chord progression to MIDI-JSON.
    /// </summary>
    /// <param name="voicings">The voiced chords.</param>
    /// <param name="ticksPerBeat">Ticks per beat.</param>
    /// <param name="beatsPerChord">Duration of each chord in beats.</param>
    /// <param name="trackName">Track name.</param>
    /// <param name="instrument">GM instrument number.</param>
    public static MidiJson RenderChords(
        IReadOnlyList<Voicing> voicings,
        int ticksPerBeat = 480,
        double beatsPerChord = 4.0,
        string? trackName = "Chords",
        int instrument = 0)
    {
        var track = new MidiTrack
        {
            Name = trackName,
            Channel = 0,
            Instrument = instrument
        };

        var currentTick = 0;
        var durationTicks = (int)(beatsPerChord * ticksPerBeat);

        foreach (var voicing in voicings)
        {
            foreach (var pitch in voicing.Pitches)
            {
                track.Events.Add(new MidiEvent
                {
                    Tick = currentTick,
                    Type = MidiEventType.NoteOn,
                    Note = pitch.MidiNumber,
                    Velocity = 70,
                    Duration = durationTicks
                });
            }

            currentTick += durationTicks;
        }

        return new MidiJson
        {
            TicksPerBeat = ticksPerBeat,
            Tracks = [track]
        };
    }

    /// <summary>
    /// Combines multiple MIDI-JSON representations.
    /// </summary>
    /// <param name="sources">Source MIDI-JSON objects.</param>
    /// <returns>Combined MIDI-JSON.</returns>
    public static MidiJson Combine(params MidiJson[] sources)
    {
        if (sources.Length == 0)
        {
            return new MidiJson();
        }

        var result = new MidiJson
        {
            TicksPerBeat = sources[0].TicksPerBeat,
            Header = sources[0].Header
        };

        var channel = 0;
        foreach (var source in sources)
        {
            foreach (var track in source.Tracks)
            {
                var newTrack = new MidiTrack
                {
                    Name = track.Name,
                    Channel = channel,
                    Instrument = track.Instrument
                };
                newTrack.Events.AddRange(track.Events);
                result.Tracks.Add(newTrack);
                channel = (channel + 1) % 16;
            }
        }

        return result;
    }

    /// <summary>
    /// Creates a complete composition MIDI-JSON with header.
    /// </summary>
    /// <param name="melody">Melody notes.</param>
    /// <param name="chords">Chord voicings.</param>
    /// <param name="tempo">Tempo in BPM.</param>
    /// <param name="meter">Time signature.</param>
    /// <param name="key">Key signature.</param>
    /// <param name="ticksPerBeat">Ticks per beat.</param>
    /// <param name="name">Composition name.</param>
    public static MidiJson RenderComposition(
        IReadOnlyList<MelodyNote> melody,
        IReadOnlyList<Voicing>? chords = null,
        double tempo = 120,
        Meter? meter = null,
        Scale? key = null,
        int ticksPerBeat = 480,
        string? name = null)
    {
        meter ??= Meter.Common.CommonTime;

        var result = new MidiJson
        {
            TicksPerBeat = ticksPerBeat,
            Header = new MidiHeader
            {
                Format = 1,
                Name = name,
                Tempos = [new TempoEvent { Tick = 0, Bpm = tempo }],
                TimeSignatures = [new TimeSignatureEvent
                {
                    Tick = 0,
                    Numerator = meter.Value.Numerator,
                    Denominator = meter.Value.Denominator
                }]
            }
        };

        if (key != null)
        {
            result.Header.KeySignatures = [new KeySignatureEvent
            {
                Tick = 0,
                Tonic = key.Root,
                Mode = key.Mode
            }];
        }

        // Add melody track
        var melodyTrack = new MidiTrack
        {
            Name = "Melody",
            Channel = 0,
            Instrument = 73 // Flute
        };

        foreach (var note in melody.OrderBy(n => n.StartTick))
        {
            melodyTrack.Events.Add(new MidiEvent
            {
                Tick = note.StartTick,
                Type = MidiEventType.NoteOn,
                Note = note.Pitch.MidiNumber,
                Velocity = note.Velocity,
                Duration = note.DurationTicks
            });
        }

        result.Tracks.Add(melodyTrack);

        // Add chord track if provided
        if (chords != null && chords.Count > 0)
        {
            var chordTrack = new MidiTrack
            {
                Name = "Chords",
                Channel = 1,
                Instrument = 0 // Piano
            };

            // Estimate chord duration from melody
            var totalTicks = melody.Max(n => n.EndTick);
            var durationPerChord = totalTicks / chords.Count;
            var currentTick = 0;

            foreach (var voicing in chords)
            {
                foreach (var pitch in voicing.Pitches)
                {
                    chordTrack.Events.Add(new MidiEvent
                    {
                        Tick = currentTick,
                        Type = MidiEventType.NoteOn,
                        Note = pitch.MidiNumber,
                        Velocity = 60,
                        Duration = durationPerChord
                    });
                }

                currentTick += durationPerChord;
            }

            result.Tracks.Add(chordTrack);
        }

        return result;
    }
}
