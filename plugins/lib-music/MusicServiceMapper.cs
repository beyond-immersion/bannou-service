using BeyondImmersion.BannouService;

// SDK type aliases — required to disambiguate from generated types with same names
using SdkChordQuality = BeyondImmersion.Bannou.MusicTheory.Collections.ChordQuality;
using SdkContourShape = BeyondImmersion.Bannou.MusicTheory.Melody.ContourShape;
using SdkKeySignatureEvent = BeyondImmersion.Bannou.MusicTheory.Output.KeySignatureEvent;
using SdkMidiEvent = BeyondImmersion.Bannou.MusicTheory.Output.MidiEvent;
using SdkMidiEventType = BeyondImmersion.Bannou.MusicTheory.Output.MidiEventType;
using SdkMidiHeader = BeyondImmersion.Bannou.MusicTheory.Output.MidiHeader;
using SdkMidiJson = BeyondImmersion.Bannou.MusicTheory.Output.MidiJson;
using SdkMidiTrack = BeyondImmersion.Bannou.MusicTheory.Output.MidiTrack;
using SdkModeType = BeyondImmersion.Bannou.MusicTheory.Collections.ModeType;
using SdkPitch = BeyondImmersion.Bannou.MusicTheory.Pitch.Pitch;
using SdkPitchClass = BeyondImmersion.Bannou.MusicTheory.Pitch.PitchClass;
using SdkPitchRange = BeyondImmersion.Bannou.MusicTheory.Pitch.PitchRange;
using SdkTempoEvent = BeyondImmersion.Bannou.MusicTheory.Output.TempoEvent;
using SdkTimeSignatureEvent = BeyondImmersion.Bannou.MusicTheory.Output.TimeSignatureEvent;
using SdkVoiceLeadingRules = BeyondImmersion.Bannou.MusicTheory.Harmony.VoiceLeadingRules;
using SdkVoiceLeadingViolation = BeyondImmersion.Bannou.MusicTheory.Harmony.VoiceLeadingViolation;
using SdkVoiceLeadingViolationType = BeyondImmersion.Bannou.MusicTheory.Harmony.VoiceLeadingViolationType;

namespace BeyondImmersion.BannouService.Music;

/// <summary>
/// Boundary mapper between NSwag-generated API types (BeyondImmersion.BannouService namespace)
/// and MusicTheory SDK types. Per IMPLEMENTATION TENETS T25 Case 5 (A2 SDK Boundary):
/// the schema defines types, NSwag generates them, and the plugin maps at the boundary.
/// </summary>
internal static class MusicServiceMapper
{
    #region Enum Mappings — identical value sets, use EnumMapping.MapByName

    // PitchClass: 12 values (C, Cs, D, Ds, E, F, Fs, G, Gs, A, As, B)
    internal static SdkPitchClass ToSdkPitchClass(PitchClass pc) =>
        pc.MapByName<PitchClass, SdkPitchClass>();

    internal static PitchClass ToApiPitchClass(SdkPitchClass pc) =>
        pc.MapByName<SdkPitchClass, PitchClass>();

    // ModeType: 15 values (Major through Chromatic)
    internal static SdkModeType ToSdkModeType(ModeType mt) =>
        mt.MapByName<ModeType, SdkModeType>();

    internal static ModeType ToApiModeType(SdkModeType mt) =>
        mt.MapByName<SdkModeType, ModeType>();

    // MidiEventType: 4 values (NoteOn, NoteOff, ProgramChange, ControlChange)
    internal static SdkMidiEventType ToSdkMidiEventType(MidiEventType t) =>
        t.MapByName<MidiEventType, SdkMidiEventType>();

    internal static MidiEventType ToApiMidiEventType(SdkMidiEventType t) =>
        t.MapByName<SdkMidiEventType, MidiEventType>();

    // VoiceLeadingViolationType: 7 values
    internal static SdkVoiceLeadingViolationType ToSdkVlvt(VoiceLeadingViolationType t) =>
        t.MapByName<VoiceLeadingViolationType, SdkVoiceLeadingViolationType>();

    internal static VoiceLeadingViolationType ToApiVlvt(SdkVoiceLeadingViolationType t) =>
        t.MapByName<SdkVoiceLeadingViolationType, VoiceLeadingViolationType>();

    #endregion

    #region Superset/Lossy Enum Mappings — moved from MusicService.cs

    // ChordQuality: schema is SUBSET of SDK (SDK has MinorMajor7, Power extras)
    internal static SdkChordQuality ToSdkChordQuality(ChordQuality quality) =>
        quality.MapByName<ChordQuality, SdkChordQuality>();

    // SDK extras fall back to Major
    internal static ChordQuality ToApiChordQuality(SdkChordQuality quality) =>
        quality.MapByNameOrDefault(ChordQuality.Major);

    // ContourShape: schema is SUBSET of SDK (SDK has InvertedArch, Free extras)
    internal static SdkContourShape ToSdkContourShape(ContourShape contour) =>
        contour.MapByName<ContourShape, SdkContourShape>();

    // SDK extras fall back to Arch
    internal static ContourShape ToApiContourShape(SdkContourShape contour) =>
        contour.MapByNameOrDefault(ContourShape.Arch);

    // KeyMode→ModeType: LOSSY mapping (Aeolian→Minor) — explicit switch, NOT MapByName
    // internal for switch coverage unit testing via EnumMappingValidator
    internal static SdkModeType TestableToModeType(KeyMode mode) => mode switch
    {
        KeyMode.Major => SdkModeType.Major,
        KeyMode.Minor => SdkModeType.Minor,
        KeyMode.Dorian => SdkModeType.Dorian,
        KeyMode.Phrygian => SdkModeType.Phrygian,
        KeyMode.Lydian => SdkModeType.Lydian,
        KeyMode.Mixolydian => SdkModeType.Mixolydian,
        KeyMode.Aeolian => SdkModeType.Minor,
        KeyMode.Locrian => SdkModeType.Locrian,
        _ => SdkModeType.Major
    };

    // ModeType→KeyMode: lossy reverse mapping
    // internal for switch coverage unit testing via EnumMappingValidator
    internal static KeyMode TestableToApiMode(SdkModeType mode) => mode switch
    {
        SdkModeType.Major => KeyMode.Major,
        SdkModeType.Minor => KeyMode.Minor,
        SdkModeType.Dorian => KeyMode.Dorian,
        SdkModeType.Phrygian => KeyMode.Phrygian,
        SdkModeType.Lydian => KeyMode.Lydian,
        SdkModeType.Mixolydian => KeyMode.Mixolydian,
        SdkModeType.Locrian => KeyMode.Locrian,
        _ => KeyMode.Major
    };

    #endregion

    #region Object Conversions — Pitch

    internal static SdkPitch ToSdkPitch(Pitch p) =>
        new(ToSdkPitchClass(p.PitchClass), p.Octave);

    internal static Pitch ToApiPitch(SdkPitch p) => new()
    {
        PitchClass = ToApiPitchClass(p.PitchClass),
        Octave = p.Octave,
        MidiNumber = p.MidiNumber
    };

    internal static SdkPitchRange ToSdkPitchRange(PitchRange r) =>
        new(ToSdkPitch(r.Low), ToSdkPitch(r.High));

    internal static PitchRange ToApiPitchRange(SdkPitchRange r) => new()
    {
        Low = ToApiPitch(r.Low),
        High = ToApiPitch(r.High)
    };

    #endregion

    #region Object Conversions — Voice Leading

    internal static SdkVoiceLeadingRules ToSdkVoiceLeadingRules(VoiceLeadingRules? r) => new()
    {
        AvoidParallelFifths = r?.AvoidParallelFifths ?? true,
        AvoidParallelOctaves = r?.AvoidParallelOctaves ?? true,
        PreferStepwiseMotion = r?.PreferStepwiseMotion ?? true,
        AvoidVoiceCrossing = r?.AvoidVoiceCrossing ?? true,
        MaxLeap = r?.MaxLeap ?? 7
    };

    internal static VoiceLeadingViolation ToApiVoiceLeadingViolation(SdkVoiceLeadingViolation v) => new()
    {
        Type = ToApiVlvt(v.Type),
        Position = v.Position,
        Voices = v.Voices.ToList(),
        IsError = v.IsError,
        Message = v.Message
    };

    #endregion

    #region Object Conversions — MIDI (SDK→API only)

    internal static MidiJson ToApiMidiJson(SdkMidiJson mj) => new()
    {
        Header = mj.Header != null ? ToApiMidiHeader(mj.Header) : null,
        TicksPerBeat = mj.TicksPerBeat,
        Tracks = mj.Tracks.Select(ToApiMidiTrack).ToList()
    };

    internal static MidiHeader ToApiMidiHeader(SdkMidiHeader h) => new()
    {
        Format = h.Format,
        Name = h.Name,
        Tempos = h.Tempos?.Select(ToApiTempoEvent).ToList(),
        TimeSignatures = h.TimeSignatures?.Select(ToApiTimeSignatureEvent).ToList(),
        KeySignatures = h.KeySignatures?.Select(ToApiKeySignatureEvent).ToList()
    };

    internal static MidiTrack ToApiMidiTrack(SdkMidiTrack t) => new()
    {
        Name = t.Name,
        Channel = t.Channel,
        Instrument = t.Instrument,
        Events = t.Events.Select(ToApiMidiEvent).ToList()
    };

    internal static MidiEvent ToApiMidiEvent(SdkMidiEvent e) => new()
    {
        Tick = e.Tick,
        Type = ToApiMidiEventType(e.Type),
        Note = e.Note,
        Velocity = e.Velocity,
        Duration = e.Duration,
        Program = e.Program,
        Controller = e.Controller,
        Value = e.Value
    };

    internal static TempoEvent ToApiTempoEvent(SdkTempoEvent e) => new()
    {
        Tick = e.Tick,
        Bpm = (float)e.Bpm
    };

    internal static TimeSignatureEvent ToApiTimeSignatureEvent(SdkTimeSignatureEvent e) => new()
    {
        Tick = e.Tick,
        Numerator = e.Numerator,
        Denominator = e.Denominator
    };

    internal static KeySignatureEvent ToApiKeySignatureEvent(SdkKeySignatureEvent e) => new()
    {
        Tick = e.Tick,
        Tonic = ToApiPitchClass(e.Tonic),
        Mode = ToApiModeType(e.Mode)
    };

    #endregion
}
