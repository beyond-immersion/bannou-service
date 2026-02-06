# BeyondImmersion.Bannou.BehaviorCompiler

ABML (Arcadia Behavior Markup Language) compiler and runtime SDK for NPC behavior systems.

## Overview

This SDK provides the complete ABML compilation and execution pipeline:

- **Parser**: YAML document parsing with error recovery
- **Documents**: AST types for ABML documents (flows, actions, triggers)
- **Compiler**: Multi-phase compilation pipeline (semantic analysis, variable registration, flow compilation, bytecode emission)
- **Runtime**: Stack-based bytecode interpreter with 50+ opcodes
- **GOAP**: Goal-Oriented Action Planning with A* search
- **Intent**: Behavior output merging with contribution traces
- **Archetypes**: Channel definitions for behavior output routing

## Features

- **Pure computation**: No infrastructure dependencies (no Redis, RabbitMQ, etc.)
- **Deterministic**: Same input always produces same output
- **Cross-platform**: Works on game clients and servers
- **Portable bytecode**: Compile once, interpret anywhere

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.BehaviorCompiler
```

## Usage

### Parsing ABML Documents

```csharp
using BeyondImmersion.Bannou.BehaviorCompiler.Parser;

var parser = new DocumentParser();
var result = parser.Parse(yamlContent);

if (result.HasErrors)
{
    foreach (var error in result.Errors)
        Console.WriteLine($"Error: {error.Message} at line {error.Line}");
}
else
{
    var document = result.Document;
    // Process document...
}
```

### Compiling to Bytecode

```csharp
using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;

var compiler = new BehaviorCompiler();
var model = compiler.Compile(document, options);

// Save compiled model
var bytes = model.ToBytes();
```

### Executing Behavior Models

```csharp
using BeyondImmersion.Bannou.BehaviorCompiler.Runtime;

var interpreter = new BehaviorModelInterpreter();
var result = interpreter.Evaluate(model, context);

// Process behavior output
foreach (var intent in result.Output.Intents)
{
    // Handle movement, attention, vocalization, etc.
}
```

### GOAP Planning

```csharp
using BeyondImmersion.Bannou.BehaviorCompiler.Goap;

var planner = new GoapPlanner();
var worldState = new WorldState()
    .Set("has_weapon", true)
    .Set("enemy_visible", true);

var goal = new GoapGoal("defeat_enemy", conditions);
var plan = planner.CreatePlan(worldState, goal, availableActions);

if (plan.Success)
{
    foreach (var action in plan.Actions)
        Console.WriteLine($"Action: {action.Name}");
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    ABML YAML Document                       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      DocumentParser                         │
│            (YAML parsing, error recovery)                   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                     AbmlDocument (AST)                      │
│          (Flows, Actions, Triggers, Metadata)               │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    BehaviorCompiler                         │
│    (Semantic analysis, bytecode emission, optimization)     │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                  BehaviorModel (Bytecode)                   │
│         (Portable binary format, constant pool)             │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│               BehaviorModelInterpreter                      │
│           (Stack VM, 50+ opcodes, 7 categories)             │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    BehaviorOutput                           │
│       (Intents: movement, attention, vocalization)          │
└─────────────────────────────────────────────────────────────┘
```

## License

MIT
