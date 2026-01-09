// =============================================================================
// ABML Document Parser Tests
// Tests for YAML document parsing.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Parser;
using Xunit;

namespace BeyondImmersion.BannouService.Tests.Abml;

/// <summary>
/// Tests for DocumentParser.
/// </summary>
public class DocumentParserTests
{
    private readonly DocumentParser _parser = new();

    // =========================================================================
    // VERSION AND METADATA TESTS
    // =========================================================================

    [Fact]
    public void Parse_ValidDocument_ReturnsSuccess()
    {
        var yaml = TestFixtures.Load("parser_valid_doc");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("2.0", result.Value.Version);
        Assert.Equal("test_doc", result.Value.Metadata.Id);
        Assert.Equal("behavior", result.Value.Metadata.Type);
    }

    [Fact]
    public void Parse_MissingVersion_ReturnsError()
    {
        var yaml = TestFixtures.Load("parser_missing_version");

        var result = _parser.Parse(yaml);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Message.Contains("version"));
    }

    [Fact]
    public void Parse_UnsupportedVersion_ReturnsError()
    {
        var yaml = TestFixtures.Load("parser_unsupported_version");

        var result = _parser.Parse(yaml);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Message.Contains("Unsupported version"));
    }

    [Fact]
    public void Parse_MissingMetadata_ReturnsError()
    {
        var yaml = TestFixtures.Load("parser_missing_metadata");

        var result = _parser.Parse(yaml);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Message.Contains("metadata"));
    }

    [Fact]
    public void Parse_MissingMetadataId_ReturnsError()
    {
        var yaml = TestFixtures.Load("parser_missing_metadata_id");

        var result = _parser.Parse(yaml);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Message.Contains("id"));
    }

    [Fact]
    public void Parse_FullMetadata_ParsesAllFields()
    {
        var yaml = TestFixtures.Load("parser_full_metadata");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var meta = result.Value!.Metadata;
        Assert.Equal("full_doc", meta.Id);
        Assert.Equal("cutscene", meta.Type);
        Assert.Equal("A test document", meta.Description);
        Assert.Equal(2, meta.Tags.Count);
        Assert.Contains("combat", meta.Tags);
        Assert.Contains("boss", meta.Tags);
        Assert.True(meta.Deterministic);
    }

    // =========================================================================
    // FLOW PARSING TESTS
    // =========================================================================

    [Fact]
    public void Parse_MultipleFlows_ParsesAll()
    {
        var yaml = TestFixtures.Load("parser_multi_flow");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Flows.Count);
        Assert.True(result.Value.Flows.ContainsKey("start"));
        Assert.True(result.Value.Flows.ContainsKey("process"));
        Assert.True(result.Value.Flows.ContainsKey("end"));
    }

    [Fact]
    public void Parse_FlowWithTriggers_ParsesTriggers()
    {
        var yaml = TestFixtures.Load("parser_triggers");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var flow = result.Value!.Flows["morning"];
        Assert.Equal(2, flow.Triggers.Count);
        Assert.Equal("wake_up", flow.Triggers[0].Event);
        Assert.Equal("${energy > 0}", flow.Triggers[0].Condition);
        Assert.Equal("06:00-09:00", flow.Triggers[1].TimeRange);
    }

    // =========================================================================
    // CONTROL FLOW ACTION TESTS
    // =========================================================================

    [Fact]
    public void Parse_CondAction_ParsesBranches()
    {
        var yaml = TestFixtures.Load("parser_cond");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as CondAction;
        Assert.NotNull(action);
        Assert.Equal(2, action.Branches.Count);
        Assert.Equal("${x > 10}", action.Branches[0].When);
        Assert.Equal("${x > 5}", action.Branches[1].When);
        Assert.NotNull(action.ElseBranch);
        Assert.Single(action.ElseBranch);
    }

    [Fact]
    public void Parse_ForEachAction_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("parser_foreach");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as ForEachAction;
        Assert.NotNull(action);
        Assert.Equal("item", action.Variable);
        Assert.Equal("${items}", action.Collection);
        Assert.Single(action.Do);
    }

    [Fact]
    public void Parse_RepeatAction_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("parser_repeat");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as RepeatAction;
        Assert.NotNull(action);
        Assert.Equal(3, action.Times);
        Assert.Single(action.Do);
    }

    [Fact]
    public void Parse_GotoAction_ParsesSimpleForm()
    {
        var yaml = TestFixtures.Load("parser_goto_simple");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as GotoAction;
        Assert.NotNull(action);
        Assert.Equal("other_flow", action.Flow);
        Assert.Null(action.Args);
    }

    [Fact]
    public void Parse_GotoAction_ParsesWithArgs()
    {
        var yaml = TestFixtures.Load("parser_goto_args");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as GotoAction;
        Assert.NotNull(action);
        Assert.Equal("process", action.Flow);
        Assert.NotNull(action.Args);
        Assert.Equal("${x}", action.Args["value"]);
        Assert.Equal("test", action.Args["name"]);
    }

    [Fact]
    public void Parse_CallAction_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("parser_call");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as CallAction;
        Assert.NotNull(action);
        Assert.Equal("subroutine", action.Flow);
    }

    [Fact]
    public void Parse_ReturnAction_ParsesWithValue()
    {
        var yaml = TestFixtures.Load("parser_return");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as ReturnAction;
        Assert.NotNull(action);
        Assert.Equal("${result}", action.Value);
    }

    // =========================================================================
    // VARIABLE ACTION TESTS
    // =========================================================================

    [Fact]
    public void Parse_SetAction_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("parser_set");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as SetAction;
        Assert.NotNull(action);
        Assert.Equal("counter", action.Variable);
        Assert.Equal("${x + 1}", action.Value);
    }

    [Fact]
    public void Parse_IncrementAction_ParsesWithDefaultBy()
    {
        var yaml = TestFixtures.Load("parser_increment_default");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as IncrementAction;
        Assert.NotNull(action);
        Assert.Equal("counter", action.Variable);
        Assert.Equal(1, action.By);
    }

    [Fact]
    public void Parse_IncrementAction_ParsesWithCustomBy()
    {
        var yaml = TestFixtures.Load("parser_increment_custom");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as IncrementAction;
        Assert.NotNull(action);
        Assert.Equal("counter", action.Variable);
        Assert.Equal(5, action.By);
    }

    [Fact]
    public void Parse_DecrementAction_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("parser_decrement");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as DecrementAction;
        Assert.NotNull(action);
        Assert.Equal("lives", action.Variable);
        Assert.Equal(1, action.By);
    }

    [Fact]
    public void Parse_ClearAction_ParsesSimpleForm()
    {
        var yaml = TestFixtures.Load("parser_clear");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as ClearAction;
        Assert.NotNull(action);
        Assert.Equal("temp_data", action.Variable);
    }

    // =========================================================================
    // LOG AND DOMAIN ACTION TESTS
    // =========================================================================

    [Fact]
    public void Parse_LogAction_ParsesSimpleForm()
    {
        var yaml = TestFixtures.Load("parser_log_simple");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as LogAction;
        Assert.NotNull(action);
        Assert.Equal("Hello world", action.Message);
        Assert.Equal("info", action.Level);
    }

    [Fact]
    public void Parse_LogAction_ParsesWithLevel()
    {
        var yaml = TestFixtures.Load("parser_log_level");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as LogAction;
        Assert.NotNull(action);
        Assert.Equal("Error occurred", action.Message);
        Assert.Equal("error", action.Level);
    }

    [Fact]
    public void Parse_DomainAction_ParsesParameters()
    {
        var yaml = TestFixtures.Load("parser_domain");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as DomainAction;
        Assert.NotNull(action);
        Assert.Equal("animate", action.Name);
        Assert.Equal("hero", action.Parameters["target"]);
        Assert.Equal("wave", action.Parameters["animation"]);
    }

    // =========================================================================
    // CONTEXT TESTS
    // =========================================================================

    [Fact]
    public void Parse_Context_ParsesVariables()
    {
        var yaml = TestFixtures.Load("parser_context_vars");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Context);
        Assert.Equal(2, result.Value.Context.Variables.Count);

        var counter = result.Value.Context.Variables["counter"];
        Assert.Equal("int", counter.Type);
        Assert.Equal(0, Convert.ToInt32(counter.Default));

        var name = result.Value.Context.Variables["name"];
        Assert.Equal("string", name.Type);
        Assert.Equal("${entity.name}", name.Source);
    }

    [Fact]
    public void Parse_Context_ParsesServices()
    {
        var yaml = TestFixtures.Load("parser_context_services");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Context);
        Assert.Equal(2, result.Value.Context.Services.Count);
        Assert.Equal("economy_service", result.Value.Context.Services[0].Name);
        Assert.True(result.Value.Context.Services[0].Required);
        Assert.Equal("relationship_service", result.Value.Context.Services[1].Name);
        Assert.False(result.Value.Context.Services[1].Required);
    }

    // =========================================================================
    // OPTIONS BLOCK TESTS
    // =========================================================================

    [Fact]
    public void Parse_OptionsBlock_ParsesAllTypes()
    {
        var yaml = TestFixtures.Load("parser_options");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Options);
        Assert.Equal(2, result.Value.Options.OptionsByType.Count);
        Assert.True(result.Value.Options.OptionsByType.ContainsKey("combat"));
        Assert.True(result.Value.Options.OptionsByType.ContainsKey("dialogue"));
    }

    [Fact]
    public void Parse_OptionsBlock_ParsesCombatOptions()
    {
        var yaml = TestFixtures.Load("parser_options");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var combat = result.Value!.Options!.OptionsByType["combat"];
        Assert.Equal(3, combat.Count);

        // Verify sword_slash option
        var swordSlash = combat.First(o => o.ActionId == "sword_slash");
        Assert.Equal("${combat.style == 'aggressive' ? 0.9 : 0.6}", swordSlash.Preference);
        Assert.Equal("0.3", swordSlash.Risk);
        Assert.Equal("${equipment.has_sword}", swordSlash.Available);
        Assert.Contains("has_sword", swordSlash.Requirements);
        Assert.Contains("melee", swordSlash.Tags);
        Assert.Contains("offensive", swordSlash.Tags);
        Assert.Null(swordSlash.CooldownMs);

        // Verify shield_bash option with cooldown
        var shieldBash = combat.First(o => o.ActionId == "shield_bash");
        Assert.Equal("0.7", shieldBash.Preference);
        Assert.Equal("true", shieldBash.Available);
        Assert.Equal("3000", shieldBash.CooldownMs);
        Assert.Contains("stun", shieldBash.Tags);

        // Verify retreat option
        var retreat = combat.First(o => o.ActionId == "retreat");
        Assert.Equal("${1.0 - combat.riskTolerance}", retreat.Preference);
        Assert.Null(retreat.Risk);
    }

    [Fact]
    public void Parse_OptionsBlock_ParsesDialogueOptions()
    {
        var yaml = TestFixtures.Load("parser_options");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var dialogue = result.Value!.Options!.OptionsByType["dialogue"];
        Assert.Equal(2, dialogue.Count);

        var greet = dialogue.First(o => o.ActionId == "greet_friendly");
        Assert.Equal("${personality.extraversion * personality.agreeableness}", greet.Preference);
        Assert.Equal("true", greet.Available);

        var intimidate = dialogue.First(o => o.ActionId == "intimidate");
        Assert.Equal("0.5", intimidate.Preference);
        Assert.Equal("${personality.aggression > 0.5}", intimidate.Available);
    }

    [Fact]
    public void Parse_NoOptionsBlock_ReturnsNull()
    {
        var yaml = TestFixtures.Load("parser_valid_doc");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Options);
    }

    // =========================================================================
    // ERROR HANDLING TESTS
    // =========================================================================

    [Fact]
    public void Parse_InvalidYaml_ReturnsError()
    {
        var yaml = TestFixtures.Load("parser_invalid");

        var result = _parser.Parse(yaml);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_EmptyDocument_ReturnsError()
    {
        var yaml = "";

        var result = _parser.Parse(yaml);

        Assert.False(result.IsSuccess);
    }
}
