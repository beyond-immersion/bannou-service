# Emotional Arcs SVD Methodology - Technical Summary

> **Source**: Reagan, A.J., et al. (2016). "The emotional arcs of stories are dominated by six basic shapes." EPJ Data Science, 5(31).
> **arXiv**: https://arxiv.org/abs/1606.07772
> **DOI**: https://doi.org/10.1140/epjds/s13688-016-0093-1
> **Reference Implementation**: ~/repos/core-stories
> **Implementation Relevance**: HIGH - provides quantitative foundation for emotional arc classification and narrative state trajectories

## Core Discovery

Using Singular Value Decomposition (SVD) on sentiment time-series from 1,327 Project Gutenberg stories, the researchers identified that narrative emotional arcs are dominated by **six fundamental shapes** that explain 94.1% of variance.

## Data Pipeline

```
Raw Text Files (Project Gutenberg)
    ↓
Preprocessing (extract main text, remove headers/footers)
    ↓
Tokenization + Sentence Parsing (spaCy NLP)
    ↓
Sliding Window Sentiment Analysis (labMTsimple)
    ↓
Time-Series Matrix (N_books × 200 time points)
    ↓
Mean Centering (per-book normalization)
    ↓
SVD Decomposition → 6 dominant modes
```

## Critical Numeric Constants

| Parameter | Value | Description |
|-----------|-------|-------------|
| **n_points** | 200 | Time-series sampling resolution (per story) |
| **min_size** | 10,000 | Sliding window size in words |
| **step_size** | `(total_words - min_size) / (n_points - 1)` | Overlap between windows |
| **min_downloads** | 40+ (later 150+) | Corpus filtering threshold |
| **word_count_range** | 20,000 - 100,000 | Story length filter |
| **num_modes** | 6 | Primary SVD components used |
| **sentiment_scale** | 1-9 | labMT happiness score range |

## SVD Variance Explained

| Mode | Variance % | Cumulative % |
|------|------------|--------------|
| Mode 1 | 75.6% | 75.6% |
| Mode 2 | 10.2% | 85.8% |
| Mode 3 | 3.1% | 88.9% |
| Mode 4 | 2.0% | 90.9% |
| Mode 5 | 1.8% | 92.7% |
| Mode 6 | 1.4% | 94.1% |

## The Six Fundamental Arcs

| Arc Name | Pattern | SVD Derivation | Examples |
|----------|---------|----------------|----------|
| **Rags to Riches** | ↗ continuous rise | Mode 1, positive loading | Cinderella origin, success stories |
| **Riches to Rags** (Tragedy) | ↘ continuous fall | Mode 1, negative loading | Greek tragedy, fall narratives |
| **Man in Hole** | ↘↗ fall then rise | Mode 2, positive loading | **Most common arc** - comedy, recovery |
| **Icarus** | ↗↘ rise then fall | Mode 2, negative loading | Hubris narratives |
| **Cinderella** | ↗↘↗ rise-fall-rise | Mode 1+2 combination | Fairy tales, romance |
| **Oedipus** | ↘↗↘ fall-rise-fall | Mode 1+2 inverted | Complex tragedy |

## Sentiment Scoring Algorithm

Using labMTsimple (Language Assessment by Mechanical Turk):

```python
def calculate_sentiment(word_frequencies, labmt_scores):
    """
    labmt_scores: dict mapping words to happiness scores (1-9 scale)
    Returns: weighted average happiness score
    """
    total_score = 0
    total_weight = 0
    for word, frequency in word_frequencies.items():
        if word in labmt_scores:
            total_score += labmt_scores[word] * frequency
            total_weight += frequency
    return total_score / total_weight if total_weight > 0 else 5.0  # 5.0 = neutral
```

## Sliding Window Calculation

```python
def chopper_sliding(text_words, min_size=10000, num_points=200):
    """
    Generate overlapping sentiment windows across story.
    """
    total_words = len(text_words)
    step = (total_words - min_size) // (num_points - 1)

    timeseries = []
    for i in range(num_points):
        start = i * step
        end = start + min_size
        window = text_words[start:end]
        sentiment = calculate_sentiment(word_frequencies(window), labmt_scores)
        timeseries.append(sentiment)

    return timeseries  # Length: 200 points
```

## Matrix Construction

```python
# Construct sentiment matrix
big_matrix = np.zeros((N_books, 200))
for i, book in enumerate(filtered_books):
    big_matrix[i, :] = book.timeseries

# Mean-center each book's trajectory
means = big_matrix.mean(axis=1, keepdims=True)
big_matrix_centered = big_matrix - means

# Perform SVD
U, S, V = np.linalg.svd(big_matrix_centered, full_matrices=False)

# V contains the mode shapes (200-point trajectories)
# S contains the singular values (importance weights)
# U contains the mode coefficients per book
```

## Distance Metrics

**City Block (Manhattan) Distance** - Used for hierarchical clustering:
```python
def cityBlock(a, b):
    return np.sum(np.abs(a - b))
```

**Euclidean Distance** - Used for SOM and nearest-neighbor:
```python
def euclidean(a, b):
    return np.sqrt(np.sum((a - b) ** 2))
```

## Book Classification Algorithm

```python
def classify_arc(book_coefficients, mode_shapes):
    """
    Classify a book into one of 6 arc types based on SVD mode loadings.
    """
    # book_coefficients: U[book_index, :] * S (weighted by singular values)

    mode1_loading = book_coefficients[0]  # First mode coefficient
    mode2_loading = book_coefficients[1]  # Second mode coefficient

    # Threshold for significance (arbitrary, tune empirically)
    threshold = 0.1

    if abs(mode1_loading) > threshold and abs(mode2_loading) < threshold:
        if mode1_loading > 0:
            return "rags_to_riches"
        else:
            return "tragedy"

    if abs(mode2_loading) > threshold and abs(mode1_loading) < threshold:
        if mode2_loading > 0:
            return "man_in_hole"
        else:
            return "icarus"

    # Combined modes
    if mode1_loading > 0 and mode2_loading > 0:
        return "cinderella"
    elif mode1_loading < 0 and mode2_loading < 0:
        return "oedipus"

    return "mixed"  # No dominant pattern
```

## Validation: Control Experiments

The research validated results against randomized controls:

| Control Type | Method | Finding |
|--------------|--------|---------|
| **Word Salad** | Shuffle all words within each book | Emotional structure destroyed |
| **Markov Chains** | Generate text with same word frequencies | No coherent arcs emerge |

This proves the arcs are NOT artifacts of word frequency but reflect genuine narrative structure.

## Implementation Data Structures

### Arc Trajectory (200-point array)

```csharp
public sealed class EmotionalArcTrajectory
{
    /// <summary>Sampled sentiment values normalized to [0, 1]</summary>
    public double[] Points { get; }  // Length: 200

    /// <summary>Arc classification based on SVD mode loadings</summary>
    public EmotionalArcType ArcType { get; }

    /// <summary>Confidence in classification (0-1)</summary>
    public double Confidence { get; }
}

public enum EmotionalArcType
{
    RagsToRiches,    // Mode 1 positive
    Tragedy,         // Mode 1 negative
    ManInHole,       // Mode 2 positive (MOST COMMON)
    Icarus,          // Mode 2 negative
    Cinderella,      // Modes 1+2 positive
    Oedipus          // Modes 1+2 negative
}
```

### SVD Mode Shape

```csharp
public sealed class SvdModeShape
{
    public int ModeNumber { get; }        // 1-6
    public double VarianceExplained { get; }  // 0.756, 0.102, etc.
    public double[] Shape { get; }        // 200-point trajectory
}
```

## Integration with NarrativeState

The emotional arc provides a **single dimension** (sentiment valence) over story time. This maps to the STORYLINE_COMPOSER's NarrativeState as:

```csharp
// Emotional arc sentiment at position t maps to Hope dimension
narrativeState.Hope = arcTrajectory.Points[(int)(t * 199)];
```

For full NarrativeState, combine with genre-specific value spectrums:
- **Tension**: Derived from slope/derivative of arc
- **Stakes**: Derived from distance from neutral (5.0)
- **Hope**: Direct mapping from sentiment value

## Key Findings for Storyline SDK

1. **200-point resolution is standard** - All analyses used 200 time points
2. **10,000-word window is optimal** - Captures paragraph-level sentiment, smooths noise
3. **Mode 1 dominates (75.6%)** - Simple rise/fall explains most variance
4. **Man in Hole is most common** - Useful as default template
5. **Arcs are composable** - Complex arcs = linear combinations of 6 base shapes
6. **Randomization destroys arcs** - Validates structural reality

## References

- Reagan, A.J., Mitchell, L., Kiley, D., Danforth, C.M., & Dodds, P.S. (2016). The emotional arcs of stories are dominated by six basic shapes. EPJ Data Science, 5(31).
- Dodds, P.S., et al. (2011). Temporal patterns of happiness and information in a global social network: Hedonometrics and Twitter. PLoS ONE 6(12).
- Vonnegut, K. (1981). Palm Sunday: An Autobiographical Collage. (Shape of Stories lecture)
- labMTsimple: https://github.com/andyreagan/labMTsimple
- core-stories: ~/repos/core-stories (reference implementation)
