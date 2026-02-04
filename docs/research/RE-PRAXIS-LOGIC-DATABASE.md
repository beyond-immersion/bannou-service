# Re:Praxis Logic Database - Technical Summary

> **Source**: Re:Praxis (reconstruction of Versu's Praxis language)
> **Version**: 1.4.0
> **Repository**: ~/repos/re-praxis
> **Origin**: Versu social simulation engine (Richard Evans, Emily Short)
> **Implementation Relevance**: HIGH - directly applicable to Actor/Behavior service knowledge bases

## Core Concept

Re:Praxis is an **in-memory logic database** using exclusion logic for storing, querying, and reasoning about narrative data. It uses a hierarchical tree structure with cardinality constraints and pattern-matching queries with variable unification.

## Tree Structure with Cardinality

Data is stored as paths through a tree, with two cardinality operators:

| Operator | Cardinality | Semantics | Max Children |
|----------|-------------|-----------|--------------|
| `.` (dot) | MANY | One-to-many relationship | Unlimited |
| `!` (exclusion) | ONE | One-to-one relationship | 1 (replaces previous) |

### Examples

```
ashley.age!32
// ashley → (MANY) → age → (ONE) → 32
// Subsequent inserts to ashley.age! replace the value

ashley.likes.mike
// ashley → (MANY) → likes → (MANY) → mike
// Can have multiple likes

astrid.relationships.jordan.reputation!30
// Deep nesting with final exclusive value
```

## Node Types

| Type | Syntax | Detection | Example |
|------|--------|-----------|---------|
| **Variable** | `?name` | Starts with `?` | `?speaker`, `?other` |
| **Symbol** | identifier | Default case | `ashley`, `likes` |
| **Integer** | number | `int.TryParse` | `32`, `-5` |
| **Float** | decimal | `float.TryParse` | `3.14`, `0.5` |
| **String** | `"quoted"` | Starts/ends with `"` | `"Hello world"` |

### Type Detection Order

```csharp
// Priority (first match wins):
1. Is int type? → IntNode
2. Is long type? → IntNode (cast)
3. Is float type? → FloatNode
4. Is double type? → FloatNode (cast)
5. Is string?
   a. Starts with `?` → VariableNode
   b. Starts/ends with `"` → StringNode (strip quotes)
   c. int.TryParse → IntNode
   d. float.TryParse → FloatNode
   e. Default → SymbolNode
```

## Database Operations

### Insert

```csharp
db.Insert("ashley.age!32");
// Creates path, respecting cardinality
// ONE cardinality replaces existing children
// MANY cardinality adds to children
```

### Assert (Check Existence)

```csharp
db.Assert("ashley.likes.mike");  // true if path exists
db.Assert("ashley.likes");        // true if any child exists (prefix check)
```

### Delete

```csharp
db.Delete("ashley.likes.mike");
// Removes entire subtree at path
// Returns true if deleted, false if not found
```

## Query System

### Query Builder (Immutable, Fluent)

```csharp
var query = new DBQuery()
    .Where("astrid.relationships.?other.reputation!?r")
    .Where("gt ?r 10")
    .Where("player.relationships.?other.reputation!?r1")
    .Where("lt ?r1 0")
    .Where("neq ?speaker player");

var result = query.Run(db);
```

### Expression Types

| Expression | Syntax | Purpose |
|------------|--------|---------|
| **Assertion** | `path.with.?vars` | Bind variables to matching paths |
| **Negation** | `not path.with.?vars` | Exclude paths that match |
| **Equality** | `eq ?a ?b` | Variables must be equal |
| **Inequality** | `neq ?a ?b` | Variables must differ |
| **Greater Than** | `gt ?a ?b` | Numeric comparison |
| **Less Than** | `lt ?a ?b` | Numeric comparison |
| **GTE/LTE** | `gte ?a ?b`, `lte ?a ?b` | Inclusive comparisons |

### Unification Algorithm

```python
# Pseudocode for variable binding
possibleBindings = []

for each sentence in query.sentences:
    newBindings = Unify(sentence, database)

    if possibleBindings is empty:
        possibleBindings = newBindings
    else:
        # Cross-product with compatibility check
        iterativeBindings = []
        for oldBinding in possibleBindings:
            for newBinding in newBindings:
                if no_conflicting_keys(oldBinding, newBinding):
                    merged = merge(oldBinding, newBinding)
                    iterativeBindings.append(merged)
        possibleBindings = iterativeBindings

return filter_non_empty(possibleBindings)
```

**Time Complexity**: O(n^m) where n = bindings per sentence, m = sentences (Cartesian product)

### Query Result Format

```csharp
public class QueryResult
{
    public bool Success { get; }
    public Dictionary<string, object>[] Bindings { get; }
}

// Example result:
result.Success = true
result.Bindings = [
    { "?speaker": "astrid", "?other": "jordan", "?r": 30 },
    { "?speaker": "astrid", "?other": "britt", "?r": 15 }
]
```

## Before-Access Listeners (Computed Properties)

Real-time data updates before database access:

```csharp
db.AddBeforeAccessListener("player.reputation", (database) =>
{
    // Called before any access to player.reputation*
    database.Insert($"player.reputation!{externalObject.GetReputation()}");
});

// Any query accessing player.reputation triggers the callback
db.Assert("player.reputation!25");  // Callback runs first
```

### Listener Matching

- Uses **prefix matching** via `Sentence.StartsWith()`
- Executed **before** database access
- Supports lazy/computed properties

## Implementation Patterns

### Node Interface

```csharp
public interface INode
{
    NodeType NodeType { get; }
    string Symbol { get; }
    NodeCardinality Cardinality { get; }
    IEnumerable<INode> Children { get; }
    INode? Parent { get; set; }
    object GetValue();

    // Comparison operators
    bool EqualTo(INode other);
    bool GreaterThan(INode other);
    bool LessThan(INode other);

    // Tree operations
    void AddChild(INode node);
    bool RemoveChild(string symbol);
    INode GetChild(string symbol);
    bool HasChild(string symbol);
}
```

### Generic Node Base

```csharp
public abstract class Node<T> : INode where T : notnull
{
    public string Symbol { get; }
    public T Value { get; }
    public NodeCardinality Cardinality { get; }
    public abstract NodeType NodeType { get; }

    private Dictionary<string, INode> _children;  // O(1) lookup
}
```

## Narrative Modeling Patterns

### Character Relationships

```
character.relationships.?other.reputation!?score
character.relationships.?other.tags.?type
```

### Character Attributes

```
character.attributes.?attribute!?value
character.attributes.personality.?trait!?score
```

### Narrative State

```
story.acts.?act.scenes.?scene.state.?key!?value
story.timeline.?event.participants.?participant.role!?role
```

### NPC Knowledge Queries

```csharp
// Find all characters this NPC has met
var query = new DBQuery()
    .Where("?character.met.?other");

// Find characters with negative reputation, exclude enemies
var query = new DBQuery()
    .Where("?character.relationships.?other.reputation!?rep")
    .Where("lt ?rep 0")
    .Where("not ?character.relationships.?other.tags.enemy");

// Find high-empathy characters
var query = new DBQuery()
    .Where("?character.traits.empathy!?e")
    .Where("gte ?e 0.5");
```

## Integration with Actor/Behavior Service

Re:Praxis provides a proven pattern for NPC knowledge bases:

### Per-Actor Database

```csharp
public class ActorBrain
{
    private readonly RePraxisDatabase _knowledge;

    public ActorBrain()
    {
        _knowledge = new RePraxisDatabase();

        // Computed property for current perception
        _knowledge.AddBeforeAccessListener("perception", db =>
        {
            db.Clear("perception");
            foreach (var percept in _currentPercepts)
                db.Insert($"perception.{percept.Type}.{percept.Id}");
        });
    }

    public void RecordEncounter(string otherId, double sentiment)
    {
        _knowledge.Insert($"self.met.{otherId}");
        _knowledge.Insert($"self.relationships.{otherId}.sentiment!{sentiment}");
    }

    public bool HasMet(string otherId)
    {
        return _knowledge.Assert($"self.met.{otherId}");
    }
}
```

### ABML Integration

```yaml
# Query knowledge base from behavior
condition: "${db.Assert('self.relationships.?target.sentiment!?s')} and ${db.gt ?s 0.5}"
action: "greet_friendly"
```

### Variable Provider Pattern

```csharp
public class PraxisVariableProvider : IVariableProvider
{
    private readonly RePraxisDatabase _db;

    public string Prefix => "kb";  // ${kb.path.to.value}

    public async Task<object?> GetValueAsync(string path, string entityId, CancellationToken ct)
    {
        // Query database for single value
        var result = new DBQuery()
            .Where($"self.{path}!?value")
            .Run(_db);

        return result.Success && result.Bindings.Length > 0
            ? result.Bindings[0]["?value"]
            : null;
    }
}
```

## Known Limitations

| Limitation | Impact | Mitigation |
|------------|--------|------------|
| **Single-threaded** | No concurrent access | One database per actor |
| **No persistence** | In-memory only | Serialize on save, deserialize on load |
| **No indexing** | O(n) tree traversal | Acceptable for NPC-scale data |
| **No transactions** | No rollback | Careful operation ordering |
| **Cardinality fixed** | Design-time decision | Plan schema carefully |

## Key Findings for Storyline SDK

1. **Hierarchical tree storage** - Natural for entity-relationship data
2. **Cardinality constraints** - ONE (!) for exclusive values, MANY (.) for collections
3. **Pattern matching queries** - Variables (?var) bind to matching paths
4. **Immutable query builder** - Thread-safe query construction
5. **Before-access listeners** - Lazy/computed properties
6. **No SQL** - Purpose-built for narrative simulation, not relational data
7. **Proven in production** - Based on Versu's 10+ year history

## References

- Re:Praxis: ~/repos/re-praxis
- Versu: Interactive storytelling engine (Richard Evans, Emily Short)
- Praxis: Original exclusion logic language from Versu
- Evans, R. (2014). "Versu: A Simulationist Storytelling System"
