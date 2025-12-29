// ═══════════════════════════════════════════════════════════════════════════
// ABML Channel Scheduler Tests
// Tests for multi-channel execution, signaling, and synchronization.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Execution.Channel;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Parser;
using BeyondImmersion.BannouService.Abml.Runtime;
using Xunit;

namespace BeyondImmersion.BannouService.UnitTests.Abml;

/// <summary>
/// Tests for multi-channel execution, signaling, and synchronization.
/// </summary>
public class ChannelSchedulerTests
{
    private readonly DocumentParser _parser = new();

    private ChannelScheduler CreateScheduler(ChannelSchedulerConfig? config = null)
    {
        var evaluator = new ExpressionEvaluator();
        var handlers = ActionHandlerRegistry.CreateWithBuiltins();
        return new ChannelScheduler(evaluator, handlers, config);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BASIC MULTI-CHANNEL EXECUTION
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_TwoChannels_RunsConcurrently()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: two_channels
            flows:
              channel_a:
                actions:
                  - log: { message: "A1" }
                  - log: { message: "A2" }
              channel_b:
                actions:
                  - log: { message: "B1" }
                  - log: { message: "B2" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["A"] = "channel_a",
            ["B"] = "channel_b"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Logs.Count);
        // Messages should interleave due to round-robin scheduling
    }

    [Fact]
    public async Task Execute_SingleChannel_CompletesNormally()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: single_channel
            flows:
              main:
                actions:
                  - log: { message: "Step 1" }
                  - log: { message: "Step 2" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["main"] = "main"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Step 1", result.Logs[0].Message);
        Assert.Equal("Step 2", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_NoChannels_ReturnsError()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: no_channels
            flows:
              start:
                actions:
                  - log: "test"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var result = await scheduler.ExecuteAsync(
            parseResult.Value!,
            new Dictionary<string, string>());

        Assert.False(result.IsSuccess);
        Assert.Contains("No channels", result.ErrorMessage);
    }

    [Fact]
    public async Task Execute_InvalidFlowName_ReturnsError()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: invalid_flow
            flows:
              start:
                actions:
                  - log: "test"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["main"] = "nonexistent_flow"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EMIT / WAIT_FOR SIGNALING TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_EmitWaitFor_SignalDelivered()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: signal_test
            flows:
              sender:
                actions:
                  - log: { message: "Sending" }
                  - emit: { signal: "data_ready" }
                  - log: { message: "Sent" }
              receiver:
                actions:
                  - log: { message: "Waiting" }
                  - wait_for: { signal: "data_ready" }
                  - log: { message: "Received" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["sender"] = "sender",
            ["receiver"] = "receiver"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.True(result.IsSuccess);
        // Both channels should complete
        Assert.Contains(result.Logs, l => l.Message == "Received");
        Assert.Contains(result.Logs, l => l.Message == "Sent");
    }

    [Fact]
    public async Task Execute_EmitWithPayload_PayloadAccessible()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: payload_test
            flows:
              sender:
                actions:
                  - emit:
                      signal: "data"
                      payload: "${'hello world'}"
              receiver:
                actions:
                  - wait_for: { signal: "data" }
                  - log: { message: "Got: ${_signal.payload}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["sender"] = "sender",
            ["receiver"] = "receiver"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Logs, l => l.Message == "Got: hello world");
    }

    [Fact]
    public async Task Execute_MultipleReceivers_AllReceiveSignal()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: multi_receiver
            flows:
              sender:
                actions:
                  - emit: { signal: "broadcast" }
              receiver1:
                actions:
                  - wait_for: { signal: "broadcast" }
                  - log: { message: "R1 got it" }
              receiver2:
                actions:
                  - wait_for: { signal: "broadcast" }
                  - log: { message: "R2 got it" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["sender"] = "sender",
            ["R1"] = "receiver1",
            ["R2"] = "receiver2"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Logs, l => l.Message == "R1 got it");
        Assert.Contains(result.Logs, l => l.Message == "R2 got it");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SYNC BARRIER TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Sync_AllChannelsWaitAtBarrier()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: sync_test
            flows:
              fast:
                actions:
                  - log: { message: "Fast before" }
                  - sync: { point: "barrier1" }
                  - log: { message: "Fast after" }
              slow:
                actions:
                  - log: { message: "Slow before" }
                  - log: { message: "Slow extra work" }
                  - sync: { point: "barrier1" }
                  - log: { message: "Slow after" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["fast"] = "fast",
            ["slow"] = "slow"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.True(result.IsSuccess);
        // Both channels should complete past the barrier
        Assert.Contains(result.Logs, l => l.Message == "Fast after");
        Assert.Contains(result.Logs, l => l.Message == "Slow after");
    }

    [Fact]
    public async Task Execute_Sync_ThreeChannels()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: sync_three
            flows:
              ch1:
                actions:
                  - sync: { point: "all_ready" }
                  - log: { message: "CH1 done" }
              ch2:
                actions:
                  - sync: { point: "all_ready" }
                  - log: { message: "CH2 done" }
              ch3:
                actions:
                  - sync: { point: "all_ready" }
                  - log: { message: "CH3 done" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["ch1"] = "ch1",
            ["ch2"] = "ch2",
            ["ch3"] = "ch3"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Logs, l => l.Message == "CH1 done");
        Assert.Contains(result.Logs, l => l.Message == "CH2 done");
        Assert.Contains(result.Logs, l => l.Message == "CH3 done");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DEADLOCK DETECTION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Deadlock_Detected()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: deadlock
            flows:
              ch1:
                actions:
                  - wait_for: { signal: "from_ch2" }
                  - emit: { signal: "from_ch1" }
              ch2:
                actions:
                  - wait_for: { signal: "from_ch1" }
                  - emit: { signal: "from_ch2" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["ch1"] = "ch1",
            ["ch2"] = "ch2"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.False(result.IsSuccess);
        Assert.Contains("Deadlock", result.ErrorMessage);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TIMEOUT TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WaitTimeout_Detected()
    {
        // When one channel waits and another keeps emitting unrelated signals,
        // the waiting channel should eventually timeout
        var yaml = """
            version: "2.0"
            metadata:
              id: timeout
            flows:
              waiter:
                actions:
                  - wait_for: { signal: "never_sent" }
              sender:
                actions:
                  - emit: { signal: "wrong_signal" }
                  - emit: { signal: "still_wrong" }
                  - emit: { signal: "nope" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        // Use short timeout for test
        var config = new ChannelSchedulerConfig
        {
            WaitTimeout = TimeSpan.FromMilliseconds(50)
        };
        var scheduler = CreateScheduler(config);
        var channelFlows = new Dictionary<string, string>
        {
            ["waiter"] = "waiter",
            ["sender"] = "sender"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        // Should fail - waiter times out waiting for signal that never comes
        // (sender sends different signals, keeping things active but not satisfying waiter)
        Assert.False(result.IsSuccess);
        // Could be timeout or deadlock after sender completes
        Assert.True(
            result.ErrorMessage!.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            result.ErrorMessage.Contains("Deadlock", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ERROR HANDLING TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ChannelError_PropagatesWithChannel()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: error_channel
            flows:
              good:
                actions:
                  - log: { message: "Good" }
              bad:
                actions:
                  - goto: nonexistent
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["good"] = "good",
            ["bad"] = "bad"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.False(result.IsSuccess);
        Assert.Equal("bad", result.FailedChannel);
    }

    [Fact]
    public async Task Execute_ChannelReturnsValue_InResults()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: return_value
            flows:
              returner:
                actions:
                  - return: { value: "${'result_data'}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["main"] = "returner"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ChannelResults);
        Assert.True(result.ChannelResults.ContainsKey("main"));
        Assert.Equal("result_data", result.ChannelResults["main"]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SCOPE ISOLATION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Channels_HaveSeparateScopes()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: scope_isolation
            flows:
              ch1:
                actions:
                  - set:
                      variable: x
                      value: "from_ch1"
                  - log: { message: "CH1: ${x}" }
              ch2:
                actions:
                  - set:
                      variable: x
                      value: "from_ch2"
                  - log: { message: "CH2: ${x}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["ch1"] = "ch1",
            ["ch2"] = "ch2"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows);

        Assert.True(result.IsSuccess);
        // Each channel should see its own value
        Assert.Contains(result.Logs, l => l.Message == "CH1: from_ch1");
        Assert.Contains(result.Logs, l => l.Message == "CH2: from_ch2");
    }

    [Fact]
    public async Task Execute_Channels_ShareRootScope()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: shared_root
            flows:
              reader:
                actions:
                  - log: { message: "Shared: ${shared_data}" }
              writer:
                actions:
                  - log: { message: "Writer sees: ${shared_data}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var rootScope = new VariableScope();
        rootScope.SetValue("shared_data", "initial");

        var scheduler = CreateScheduler();
        var channelFlows = new Dictionary<string, string>
        {
            ["reader"] = "reader",
            ["writer"] = "writer"
        };

        var result = await scheduler.ExecuteAsync(parseResult.Value!, channelFlows, rootScope);

        Assert.True(result.IsSuccess);
        // Both channels should see the shared data
        Assert.Contains(result.Logs, l => l.Message == "Shared: initial");
        Assert.Contains(result.Logs, l => l.Message == "Writer sees: initial");
    }
}
