# Storyline Theory SDK

Pure computation narrative theory library for story generation. Provides formal narrative primitives based on peer-reviewed research and practitioner frameworks.

## Overview

This SDK implements the theoretical foundations for narrative generation:

- **Propp Functions**: Vladimir Propp's 31 narrative functions from *Morphology of the Folktale* (1928)
- **Story Grid**: Shawn Coyne's genre conventions, obligatory scenes, and Five Commandments
- **Save the Cat**: Blake Snyder's 15-beat structure with timing percentages
- **Emotional Arcs**: Reagan et al.'s six validated arc shapes from SVD analysis
- **Kernel/Satellite Classification**: Barthes' narrative unit categorization

## Design Philosophy

1. **No dependencies** - Pure C# with no external libraries
2. **Deterministic** - Same inputs produce same outputs when seeded
3. **Research-backed** - Implements peer-reviewed narrative theory
4. **Configurable weights** - All scoring algorithms use tunable parameters

## Usage

```csharp
using BeyondImmersion.Bannou.StorylineTheory.Elements;
using BeyondImmersion.Bannou.StorylineTheory.Scoring;

// Check Propp function significance
var significance = ProppFunctions.GetPhaseSignificance("struggle"); // 0.9

// Calculate kernel score
var score = KernelIdentifier.Calculate(
    proppFunctionPhase: "complication",
    isObligatoryScene: true,
    valueChangeMagnitude: 0.7,
    consequenceRatio: 0.3);

// Score genre compliance
var genreScore = GenreComplianceScorer.Calculate(
    genre: StoryGridGenres.Thriller,
    inputs: new GenreComplianceInputs(...));
```

## Academic References

| Framework | Source | Year |
|-----------|--------|------|
| Propp Functions | *Morphology of the Folktale* | 1928 |
| Story Grid | Shawn Coyne, *The Story Grid* | 2015 |
| Save the Cat | Blake Snyder, *Save the Cat!* | 2005 |
| Emotional Arcs | Reagan et al., EPJ Data Science | 2016 |
| Kernels/Satellites | Roland Barthes, "Introduction to Structural Analysis" | 1966 |
