// =============================================================================
// Behavior Model State Schema
// Defines input/output variables for behavior model evaluation.
// =============================================================================

namespace BeyondImmersion.Bannou.Client.Behavior.Runtime;

/// <summary>
/// Defines the input and output variables for a behavior model.
/// Used by the interpreter to map game state to model inputs and model outputs to intents.
/// </summary>
public sealed class StateSchema
{
    /// <summary>
    /// Input variable definitions (provided by game client each evaluation).
    /// </summary>
    public IReadOnlyList<VariableDefinition> Inputs { get; }

    /// <summary>
    /// Output variable definitions (produced by model evaluation).
    /// </summary>
    public IReadOnlyList<VariableDefinition> Outputs { get; }

    /// <summary>
    /// Number of input variables.
    /// </summary>
    public int InputCount => Inputs.Count;

    /// <summary>
    /// Number of output variables.
    /// </summary>
    public int OutputCount => Outputs.Count;

    // Lookup tables for fast index resolution
    private readonly Dictionary<uint, int> _inputHashToIndex;
    private readonly Dictionary<uint, int> _outputHashToIndex;

    /// <summary>
    /// Creates a new state schema.
    /// </summary>
    /// <param name="inputs">Input variable definitions.</param>
    /// <param name="outputs">Output variable definitions.</param>
    public StateSchema(IReadOnlyList<VariableDefinition> inputs, IReadOnlyList<VariableDefinition> outputs)
    {
        Inputs = inputs;
        Outputs = outputs;

        _inputHashToIndex = new Dictionary<uint, int>(inputs.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            _inputHashToIndex[inputs[i].NameHash] = i;
        }

        _outputHashToIndex = new Dictionary<uint, int>(outputs.Count);
        for (var i = 0; i < outputs.Count; i++)
        {
            _outputHashToIndex[outputs[i].NameHash] = i;
        }
    }

    /// <summary>
    /// Gets the index of an input variable by name hash.
    /// </summary>
    /// <param name="nameHash">FNV-1a hash of the variable name.</param>
    /// <param name="index">The index if found.</param>
    /// <returns>True if the variable exists.</returns>
    public bool TryGetInputIndex(uint nameHash, out int index)
    {
        return _inputHashToIndex.TryGetValue(nameHash, out index);
    }

    /// <summary>
    /// Gets the index of an output variable by name hash.
    /// </summary>
    /// <param name="nameHash">FNV-1a hash of the variable name.</param>
    /// <param name="index">The index if found.</param>
    /// <returns>True if the variable exists.</returns>
    public bool TryGetOutputIndex(uint nameHash, out int index)
    {
        return _outputHashToIndex.TryGetValue(nameHash, out index);
    }

    /// <summary>
    /// Gets the default value for an input variable.
    /// </summary>
    /// <param name="index">Variable index.</param>
    /// <returns>Default value as double.</returns>
    public double GetInputDefault(int index)
    {
        if (index < 0 || index >= Inputs.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Inputs[index].DefaultValue;
    }

    /// <summary>
    /// Gets the input variable name (for debugging).
    /// </summary>
    /// <param name="index">Variable index.</param>
    /// <param name="stringTable">String table for name lookup.</param>
    /// <returns>Variable name.</returns>
    public string GetInputName(int index, string[] stringTable)
    {
        if (index < 0 || index >= Inputs.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var nameIndex = Inputs[index].NameStringIndex;
        return nameIndex >= 0 && nameIndex < stringTable.Length
            ? stringTable[nameIndex]
            : $"input_{index}";
    }

    /// <summary>
    /// Empty schema with no inputs or outputs.
    /// </summary>
    public static StateSchema Empty { get; } = new(
        Array.Empty<VariableDefinition>(),
        Array.Empty<VariableDefinition>());

    /// <summary>
    /// Deserializes a state schema from binary data.
    /// </summary>
    /// <param name="reader">Binary reader positioned at schema data.</param>
    /// <returns>Deserialized schema.</returns>
    public static StateSchema Deserialize(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var inputCount = reader.ReadUInt16();
        var inputs = new VariableDefinition[inputCount];
        for (var i = 0; i < inputCount; i++)
        {
            inputs[i] = VariableDefinition.Deserialize(reader);
        }

        var outputCount = reader.ReadUInt16();
        var outputs = new VariableDefinition[outputCount];
        for (var i = 0; i < outputCount; i++)
        {
            outputs[i] = VariableDefinition.Deserialize(reader);
        }

        return new StateSchema(inputs, outputs);
    }

    /// <summary>
    /// Serializes the state schema to binary data.
    /// </summary>
    /// <param name="writer">Binary writer.</param>
    public void Serialize(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write((ushort)Inputs.Count);
        foreach (var input in Inputs)
        {
            input.Serialize(writer);
        }

        writer.Write((ushort)Outputs.Count);
        foreach (var output in Outputs)
        {
            output.Serialize(writer);
        }
    }
}

/// <summary>
/// Definition of a single input or output variable.
/// </summary>
public readonly struct VariableDefinition
{
    /// <summary>
    /// FNV-1a hash of the variable name for fast lookup.
    /// </summary>
    public uint NameHash { get; init; }

    /// <summary>
    /// Index into string table for variable name (for debugging).
    /// </summary>
    public int NameStringIndex { get; init; }

    /// <summary>
    /// Variable type.
    /// </summary>
    public BehaviorVariableType Type { get; init; }

    /// <summary>
    /// Default value (as double for all types).
    /// For strings, this is the string table index.
    /// For Vector3, this is the first component (use Vector3Default for all).
    /// </summary>
    public double DefaultValue { get; init; }

    /// <summary>
    /// Creates a new variable definition.
    /// </summary>
    public VariableDefinition(uint nameHash, int nameStringIndex, BehaviorVariableType type, double defaultValue = 0.0)
    {
        NameHash = nameHash;
        NameStringIndex = nameStringIndex;
        Type = type;
        DefaultValue = defaultValue;
    }

    /// <summary>
    /// Deserializes a variable definition from binary data.
    /// </summary>
    public static VariableDefinition Deserialize(BinaryReader reader)
    {
        var nameHash = reader.ReadUInt32();
        var nameStringIndex = reader.ReadInt32();
        var type = (BehaviorVariableType)reader.ReadByte();
        var defaultValue = reader.ReadDouble();

        return new VariableDefinition(nameHash, nameStringIndex, type, defaultValue);
    }

    /// <summary>
    /// Serializes the variable definition to binary data.
    /// </summary>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(NameHash);
        writer.Write(NameStringIndex);
        writer.Write((byte)Type);
        writer.Write(DefaultValue);
    }

    /// <summary>
    /// Creates a variable definition from a name.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="nameStringIndex">Index in string table.</param>
    /// <param name="type">Variable type.</param>
    /// <param name="defaultValue">Default value.</param>
    public static VariableDefinition Create(string name, int nameStringIndex, BehaviorVariableType type, double defaultValue = 0.0)
    {
        return new VariableDefinition(HashName(name), nameStringIndex, type, defaultValue);
    }

    /// <summary>
    /// FNV-1a hash for variable name lookup.
    /// </summary>
    public static uint HashName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return 0;
        }

        const uint fnvPrime = 16777619;
        const uint fnvOffsetBasis = 2166136261;

        var hash = fnvOffsetBasis;
        foreach (var c in name)
        {
            hash ^= c;
            hash *= fnvPrime;
        }

        return hash;
    }
}
