# Generated Variable Provider Reference

> **Source**: `schemas/variable-providers.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all ABML variable providers used by the Actor runtime.
Variable providers supply entity data to behavior expressions via the
`IVariableProviderFactory` / `IVariableProvider` pattern.

## Variable Providers

| Namespace | Service | Purpose |
|-----------|---------|---------|
| `${backstory.*}` | CharacterHistory | Character backstory elements for ABML expressions (${backstory.*}) |
| `${combat.*}` | CharacterPersonality | Combat preference data for ABML expressions (${combat.*}) |
| `${encounters.*}` | CharacterEncounter | Encounter history and sentiment for ABML expressions (${encounters.*}) |
| `${faction.*}` | Faction | Faction membership and status data for ABML expressions (${faction.*}) |
| `${location.*}` | Location | Location context data for ABML expressions (${location.*}) |
| `${obligations.*}` | Obligation | Contract obligation action cost modifiers for ABML expressions (${obligations.*}) |
| `${personality.*}` | CharacterPersonality | Personality trait values for ABML expressions (${personality.*}) |
| `${quest.*}` | Quest | Active quest data for ABML expressions (${quest.*}) |
| `${seed.*}` | Seed | Seed growth and capability data for ABML expressions (${seed.*}) |

**Total**: 9 providers

## How Providers Work

1. **Schema**: Provider namespaces are defined in `schemas/variable-providers.yaml`
2. **Constants**: Generated to `VariableProviderDefinitions` in `bannou-service/Generated/`
3. **Registration**: Each plugin registers its `IVariableProviderFactory` implementation via DI
4. **Discovery**: Actor runtime discovers all factories via `IEnumerable<IVariableProviderFactory>`
5. **Resolution**: ABML expressions like `${personality.openness}` resolve the `personality` namespace,
   then delegate to the provider for sub-path resolution

## Usage in ABML

```yaml
# Access personality traits
condition: ${personality.openness} > 0.7

# Check quest state
condition: ${quest.active_count} > 0

# Encounter sentiment
condition: ${encounters.sentiment.{targetId}} < -0.5
```

## Generated Code

Variable provider definitions are generated to `bannou-service/Generated/VariableProviderDefinitions.cs`,
providing:

- **Name constants**: `VariableProviderDefinitions.Personality`, `VariableProviderDefinitions.Quest`, etc.
- **Metadata**: `VariableProviderDefinitions.Metadata` for validation and tooling

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
