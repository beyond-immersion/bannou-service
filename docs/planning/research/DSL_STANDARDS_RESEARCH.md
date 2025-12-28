# DSL Standards Research for ABML (Arcadia Behavior Markup Language)

This document summarizes research on existing standards, libraries, and approaches for YAML-based scripting/DSL systems, with a focus on gaming and telephony contexts that can inform the design of ABML.

---

## Table of Contents

1. [Behavior Trees](#1-behavior-trees)
2. [Dialogue Systems](#2-dialogue-systems)
3. [Cutscene/Timeline Systems](#3-cutscenetimeline-systems)
4. [Telephony/Dialplans](#4-telephonydialplans)
5. [Statecharts/State Machines](#5-statechartsstate-machines)
6. [Agent/AI Behavior DSLs](#6-agentai-behavior-dsls)
7. [Relevant .NET Libraries](#7-relevant-net-libraries)
8. [Feature Comparison Table](#8-feature-comparison-table)
9. [Recommendations for ABML](#9-recommendations-for-abml)

---

## 1. Behavior Trees

### Overview

Behavior trees are a powerful tool for modeling AI behavior in games and robotics. They organize behaviors into a tree structure where leaf nodes execute actions or evaluate conditions, and inner nodes determine control flow.

### Industry Standards

**BehaviorTree.CPP XML Format** - The de facto standard for behavior tree definitions:
- Uses XML-based Domain Specific Language
- Current format version: `BTCPP_format="4"`
- Supports visual editing with Groot2
- Features: async actions, dataflow between nodes, logging/profiling

Example structure:
```xml
<root BTCPP_format="4">
  <BehaviorTree ID="MainTree">
    <Sequence name="root_sequence">
      <SaySomething name="action_hello" message="Hello"/>
      <ApproachObject name="approach_object"/>
    </Sequence>
  </BehaviorTree>
</root>
```

### .NET Libraries

| Library | Version | .NET Support | Key Features |
|---------|---------|--------------|--------------|
| [BehaviourTree](https://www.nuget.org/packages/BehaviourTree) | 1.0.73 | .NET Standard 1.0+ | Simple, fluent builder extension available |
| [BehaviourTree.FluentBuilder](https://www.nuget.org/packages/BehaviourTree.FluentBuilder) | 1.0.70 | .NET Standard | Fluent API for tree construction |
| [EugenyN/BehaviorTrees](https://github.com/EugenyN/BehaviorTrees) | - | .NET 9.0 | JSON serialization, visual editor, 20+ node types |
| [GladBehavior.Tree](https://github.com/HelloKitty/GladBehavior.Tree) | - | .NET | Generic behavior tree library |
| [FluentBehaviourTree](https://github.com/ashleydavis/Fluent-Behaviour-Tree) | 0.0.4 | .NET | Fluent API (older library) |

### Pros/Cons

**Pros:**
- Visual and intuitive to design
- Modular, scalable, reusable
- Industry-proven in AAA games (Halo 2, Spore, GTA)
- Clear separation of actions and decision logic

**Cons:**
- XML format is verbose
- No universal YAML standard
- Requires understanding of control flow nodes

---

## 2. Dialogue Systems

### Yarn Spinner

**Overview:** A dialogue system using a screenplay-like format for writing interactive conversations.

**Key Features:**
- Node-based structure for organizing dialogue
- Character name prefixes optional
- Variables with `$` prefix (bool, number, string, null)
- Commands in `<<>>` syntax for game events
- Options with `[[Option Text|NodeName]]` syntax
- Shortcut options with `->` for inline choices
- Expression interpolation with `{expression}`
- Built-in localization support

**Syntax Example:**
```yarn
title: Start
---
Player: Hey, Sally.
Sally: Oh! Hi. How are you?
-> I'm fine
    Sally: That's good to hear!
-> Not great
    Sally: I'm sorry to hear that.
<<move camera left>>
===
```

**Notable Games:** Night in the Woods, A Short Hike, DREDGE, Venba, Lost in Random

### Ink (inkle)

**Overview:** A scripting language for writing interactive narrative, designed as middleware.

**Key Features:**
- "Markup, not programming" - text comes first
- Knots (`=== knot_name ===`) for story sections
- Diverts (`->`) for flow control
- Weave system for complex branching
- Conditional content with `{conditions}`
- Compiles to JSON intermediate format

**Syntax Example:**
```ink
=== the_beginning ===
I looked at Monsieur Fogg.
* "What is the purpose of our journey?"
    "To win a wager."
* "Do you have a wife, Monsieur Fogg?"
    "No," he replied firmly.
- He glanced at his watch.
-> the_journey
```

**Notable Games:** 80 Days, Heaven's Vault, Sorcery!, Haven, Sea of Thieves

### Comparison: Yarn Spinner vs Ink

| Feature | Yarn Spinner | Ink |
|---------|-------------|-----|
| Learning Curve | Gentler | Steeper but more powerful |
| Format | Custom screenplay-like | Custom markup |
| Branching | Node-based | Weave/divert system |
| Logic | Basic variables | Full programming constructs |
| Output | Direct interpretation | JSON compilation |

---

## 3. Cutscene/Timeline Systems

### Overview

Cutscene systems generally favor visual editors (Unity Timeline, Unreal Sequencer) over text-based DSLs.

### Key Concepts

**Timeline Structure:**
- Parallel tracks for simultaneous actions
- Sync points for blocking until actions complete
- Event-based triggers
- Duration-based timing

**Example Conceptual YAML Format:**
```yaml
timeline:
  - at: 0.0
    parallel:
      - action: move_camera
        target: wide_shot
        duration: 2.0
      - action: fade_in
        duration: 1.0
  - at: 2.0
    action: play_dialogue
    node: intro_scene
  - sync: true  # Wait for all current actions
```

---

## 4. Telephony/Dialplans

### SignalWire Markup Language (SWML)

**Overview:** Modern YAML/JSON-based markup for call flows built on FreeSWITCH.

**Key Features:**
- YAML or JSON format
- Section-based organization (`main`, custom sections)
- Methods: `connect`, `play`, `prompt`, `hangup`, `transfer`, `ai`
- Dynamic values with `%{variable_name}`
- AI agent integration as a method

**Example:**
```yaml
version: 1.0.0
sections:
  main:
    - answer: {}
    - play:
        url: "say:Welcome to our service"
    - prompt:
        play: "say:Press 1 for sales, 2 for support"
        max_digits: 1
    - switch:
        variable: prompt_value
        case:
          "1":
            - transfer:
                dest: sales
          "2":
            - transfer:
                dest: support
  sales:
    - connect:
        to: "sip:sales@example.com"
```

### Patterns from Telephony for ABML

1. **Section-based organization** - Named blocks for different flows
2. **Method/Action paradigm** - Clear verbs for operations
3. **Condition/Pattern matching** - Expression-based routing
4. **Transfer/Divert mechanics** - Clean flow control between sections
5. **Dynamic variable interpolation** - `${var}` syntax
6. **Sync vs Async actions** - Clear blocking semantics

---

## 5. Statecharts/State Machines

### XState (JavaScript/TypeScript)

**Overview:** Actor-based state management using statecharts adhering to SCXML specification.

**Key Features:**
- Actor model for distributed state
- Entry/exit actions
- Guards and conditions
- Parallel states
- Hierarchical states

### SCXML (W3C Standard)

**Overview:** W3C standard for State Chart XML, version 1.0.

**Key Features:**
- Hierarchical states
- Parallel states (orthogonal regions)
- History states
- External communications (`<send>`, `<invoke>`)

### .NET State Machine Libraries

| Library | Features | YAML Support |
|---------|----------|--------------|
| [Stateless](https://github.com/dotnet-state-machine/stateless) | Fluent API, async, hierarchical, DOT visualization | No (code-based) |
| [Appccelerate](https://github.com/appccelerate/statemachine) | Hierarchical, async, history states | No (code-based) |

---

## 6. Agent/AI Behavior DSLs

### Modern AI Agent Frameworks

#### Microsoft Semantic Kernel

**Overview:** SDK for building AI agents in .NET and Python.

**Key Features:**
- Model-agnostic design
- Plugin/function calling
- Multi-agent orchestration patterns:
  - Concurrent (parallel)
  - Sequential
  - Handoff
  - GroupChat

#### LangChain / LangGraph

- Modular building blocks (chains)
- LangGraph for stateful multi-agent workflows
- Graph abstraction for agent state machines

#### CrewAI

- Role-based agent definition
- Crews for collaborative tasks
- Flows for event-driven control

### Common Agent Workflow Patterns

1. **Looping Pattern** - Observe, plan, act, re-evaluate cycle
2. **Tree of Thoughts** - Explore multiple reasoning paths
3. **Function-calling with Tool-use** - External API integration
4. **Multi-Agent Systems** - Specialized roles collaborating
5. **Handoff Pattern** - Dynamic control transfer

---

## 7. Relevant .NET Libraries

### Recommended Stack

| Purpose | Library | Rationale |
|---------|---------|-----------|
| YAML Parsing | **YamlDotNet** 16.3.0 | Industry standard, full-featured |
| Expressions | **NCalc** | Fast, cached, extensible |
| Templates | **Fluid** | Best performance, secure |
| State Machine | **Stateless** 5.20.0 | Mature, hierarchical, async |
| AI Integration | **Semantic Kernel** | Microsoft-backed, multi-agent |

### YamlDotNet

- Low-level parsing and emitting
- High-level object model
- Custom type converters
- .NET 6.0+, .NET Standard 2.0

### NCalc

```csharp
var expression = new Expression("[x] + [y]");
expression.Parameters["x"] = 10;
expression.Parameters["y"] = 20;
var result = expression.Evaluate(); // 30
```

### Fluid (Liquid Template Engine)

```liquid
Hello {{ name }}!
{% for item in items %}
  - {{ item.name }}: {{ item.price }}
{% endfor %}
```

### Stateless

```csharp
var machine = new StateMachine<State, Trigger>(State.Off);

machine.Configure(State.Off)
    .Permit(Trigger.PowerOn, State.On);

machine.Configure(State.On)
    .OnEntry(() => Console.WriteLine("Power on"))
    .Permit(Trigger.PowerOff, State.Off);
```

---

## 8. Feature Comparison Table

| Feature | Behavior Trees | Yarn/Ink | SWML | XState/SCXML | Agent Frameworks |
|---------|---------------|----------|------|--------------|------------------|
| **Format** | XML | Custom | YAML/JSON | JSON/XML | YAML/Code |
| **Hierarchy** | Yes | Nodes | Sections | States | Agents/Flows |
| **Parallel Execution** | Decorators | No | Some | Parallel States | Multi-agent |
| **Conditions** | Condition Nodes | Variables | Switch | Guards | Code/Prompts |
| **Variables** | Blackboard | Built-in | Dynamic | Context | Memory |
| **Async Support** | Native | N/A | Native | Native | Native |
| **.NET Libraries** | Several | None native | None | Stateless | Semantic Kernel |
| **Human Readability** | Low (XML) | High | High | Medium | Medium |

---

## 9. Recommendations for ABML

### Core Architecture Recommendations

#### 1. **YAML-First Format** (from SWML, Sismic)
- Human-readable and version-control friendly
- Use YamlDotNet for parsing

#### 2. **Section-Based Organization** (from SWML, Ink)
- Named sections for reusable behavior blocks
- Clear entry point (`main` or `start`)

#### 3. **Behavior Tree Semantics** (from BT.CPP)
- Sequence, Selector, Parallel composites
- Action and Condition nodes as leaves

#### 4. **State Machine Integration** (from XState, Stateless)
- States for persistent behavioral modes
- Transitions with guards

#### 5. **Expression Evaluation** (from NCalc)
- Guards and conditions using expression language
- Variable interpolation in actions

#### 6. **Template Support** (from Fluid)
- Dynamic text generation for dialogue
- Variable substitution in messages

### Key Design Principles

1. **Schema-First** - Define YAML schema before implementation
2. **Composability** - Behaviors can include other behaviors
3. **Expression-Based Guards** - Use NCalc for all conditions
4. **Event-Driven** - Support reactive behavior patterns
5. **Debuggable** - Include logging and state visualization
6. **Async-Native** - All actions support async execution
7. **Type-Safe** - Generate C# types from YAML schema

---

## References

### Specifications and Standards
- [W3C SCXML Specification](https://www.w3.org/TR/scxml/)
- [BehaviorTree.CPP Documentation](https://www.behaviortree.dev/)

### Libraries
- [YamlDotNet](https://github.com/aaubry/YamlDotNet)
- [NCalc](https://github.com/ncalc/ncalc)
- [Fluid](https://github.com/sebastienros/fluid)
- [Stateless](https://github.com/dotnet-state-machine/stateless)
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel)

### DSL Systems
- [SignalWire SWML](https://developer.signalwire.com/swml/)
- [Yarn Spinner](https://github.com/YarnSpinnerTool/YarnSpinner)
- [Ink](https://github.com/inkle/ink)

---

*Research compiled: December 2024*
*For: Bannou Service - ABML Development*
