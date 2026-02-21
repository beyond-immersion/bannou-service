# BeyondImmersion.Bannou.BehaviorExpressions

Register-based expression VM for ABML (Arcadia Behavior Markup Language). Compiles and evaluates expressions like `${player.health > 50}` with support for variables, functions, type coercion, and Liquid templates.

## Features

- **Register-based VM**: Efficient bytecode execution with 256 registers
- **Expression Compiler**: Compiles expression AST to compact bytecode
- **Variable Scopes**: Hierarchical variable scoping with parent chain lookup
- **Variable Providers**: Extensible variable sources for custom data
- **Built-in Functions**: Comprehensive function library (math, string, date, etc.)
- **Type Coercion**: Automatic type conversion with truthiness evaluation
- **LRU Cache**: Thread-safe compiled expression caching
- **Liquid Templates**: Fluid-based template rendering with variable integration

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.BehaviorExpressions
```

## Quick Start

```csharp
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.BehaviorExpressions.Runtime;

// Create a variable scope with some values
var scope = new VariableScope();
scope.SetValue("player", new Dictionary<string, object>
{
    ["health"] = 75,
    ["name"] = "Hero"
});
scope.SetValue("threshold", 50);

// Create an evaluator and evaluate expressions
var evaluator = new ExpressionEvaluator();

// Boolean expression
var canFight = evaluator.EvaluateBool("${player.health > threshold}", scope);
// Result: true

// Numeric expression
var damage = evaluator.EvaluateNumber("${player.health * 0.1}", scope);
// Result: 7.5

// String expression with built-in functions
var greeting = evaluator.EvaluateString("${upper(player.name)}", scope);
// Result: "HERO"
```

## Architecture

### Components

| Component | Description |
|-----------|-------------|
| `ExpressionVm` | Register-based bytecode VM (256 registers) |
| `ExpressionCompiler` | Compiles AST to bytecode |
| `ExpressionEvaluator` | Top-level API (parse → compile → cache → execute) |
| `ExpressionCache` | Thread-safe LRU cache for compiled expressions |
| `VariableScope` | Hierarchical variable storage with parent chaining |
| `FunctionRegistry` | Extensible function lookup |
| `AbmlTypeCoercion` | Type conversion and truthiness evaluation |
| `AbmlTemplateEngine` | Fluid-based Liquid template rendering |

### Bytecode Format

Instructions are 32-bit packed: `[opcode:8][A:8][B:8][C:8]`

```
OpCode Categories:
- Loads: LoadConst, LoadVar, LoadNull, LoadTrue, LoadFalse, Move
- Property Access: GetProp, GetPropSafe, GetIndex, GetIndexSafe
- Arithmetic: Add, Sub, Mul, Div, Mod, Neg
- Comparison: Eq, Ne, Lt, Le, Gt, Ge
- Logical: Not, And, Or, ToBool
- Control Flow: Jump, JumpIfTrue, JumpIfFalse, JumpIfNull, JumpIfNotNull
- Functions: CallArgs, Call
- Null Handling: Coalesce
- String: In, Concat
- Result: Return
```

## Built-in Functions

### Math
- `abs(x)`, `min(a, b)`, `max(a, b)`
- `round(x)`, `floor(x)`, `ceil(x)`
- `sqrt(x)`, `pow(base, exp)`
- `random()` - random 0.0-1.0

### String
- `len(s)`, `upper(s)`, `lower(s)`, `trim(s)`
- `contains(s, sub)`, `starts_with(s, prefix)`, `ends_with(s, suffix)`
- `substring(s, start, length?)`
- `split(s, delimiter)`, `join(arr, delimiter)`

### Date/Time
- `now()` - current UTC timestamp
- `format_date(date, format)`

### Logic
- `coalesce(a, b, ...)` - first non-null value
- `if_else(condition, then, else)` - conditional expression

### Collections
- `list(...)` - create list
- `dict(k1, v1, k2, v2, ...)` - create dictionary
- `range(start, end)` - generate number range

## Custom Functions

```csharp
var registry = new FunctionRegistry();
registry.RegisterWithBuiltins(); // Include built-in functions

// Add custom function
registry.Register("greet", args =>
{
    var name = args.Length > 0 ? args[0]?.ToString() : "World";
    return $"Hello, {name}!";
});

var evaluator = new ExpressionEvaluator(registry: registry);
var result = evaluator.EvaluateString("${greet(player.name)}", scope);
```

## Variable Providers

For extensible variable sources:

```csharp
public class PlayerProvider : IVariableProvider
{
    public string Namespace => "player";

    public object? GetVariable(string name)
    {
        return name switch
        {
            "health" => GetPlayerHealth(),
            "level" => GetPlayerLevel(),
            _ => null
        };
    }
}

// Register provider
scope.AddProvider(new PlayerProvider());

// Access via namespace
var health = evaluator.EvaluateNumber("${player.health}", scope);
```

## Template Rendering

```csharp
using BeyondImmersion.Bannou.BehaviorExpressions.Templates;

var engine = new AbmlTemplateEngine();
var scope = new VariableScope();
scope.SetValue("player", new { name = "Hero", level = 10 });

var result = engine.Render(
    "Welcome, {{ player.name }}! You are level {{ player.level }}.",
    scope
);
// Result: "Welcome, Hero! You are level 10."
```

## Dependencies

- `BeyondImmersion.Bannou.BehaviorCompiler` - Expression parser, AST types, VmConfig
- `Fluid.Core` - Liquid template engine

## License

MIT
