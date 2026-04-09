# Dark Revelry: A Compositional Style Objective for Assisted Authoring

> **Type**: Design
> **Status**: Aspirational
> **Created**: 2026-04-03
> **Last Updated**: 2026-04-03
> **North Stars**: #5
> **Related Plugins**: Music
> **Related SDKs**: MusicTheory, MusicStoryteller, MusicComposer (planned)
> **Related Documents**: [COUNTERPOINT-COMPOSER-SDK.md](COUNTERPOINT-COMPOSER-SDK.md), [MUSIC-SYSTEM.md](../guides/MUSIC-SYSTEM.md), [VIDEO-DIRECTOR.md](VIDEO-DIRECTOR.md)

## Summary

Defines "dark revelry" as a compositional style objective for the MusicComposer SDK's assisted authoring features. Dark revelry is a convergent aesthetic — identified across multiple independent musical genres and character archetypes — where darkness, villainy, or moral transgression is presented as delight, glamour, or celebration rather than suffering or aggression. This document codifies the aesthetic into EmotionalState targets, YAML style parameters, GOAP planning constraints, and assisted authoring guidance so that both the procedural Storyteller and the human-composer workbench can produce or guide composition toward this specific emotional configuration.

The aesthetic occupies a unique and counterintuitive position in the EmotionalState space: **high tension with positive valence, high energy with low brightness**. Most music cognition frameworks treat tension and positive valence as inversely correlated. Dark revelry specifically inhabits the quadrant where both are simultaneously elevated — the musical equivalent of a villain who is having the time of their life.

---

## Part 1: Defining the Aesthetic

### 1.1 What Dark Revelry Is

Dark revelry is music where the narrator or persona occupies a position of darkness, danger, or moral transgression and presents it not as torment or aggression, but as **delight, glamour, seduction, or celebration**. The production is polished and accessible (not harsh or abrasive). The energy is high and infectious. The listener is invited to enjoy the villainy *with* the villain, not to fear or judge it.

The term describes a convergence observed across multiple established genres — electro-swing, dark cabaret, dark pop, theatrical villain music, and anthemic alternative rock — that share a specific posture without belonging fully to any single genre. Each contributing genre donates specific qualities:

| Contributing Genre | What It Donates | Key Reference |
|---|---|---|
| **Dark cabaret** | Theatrical performance, "wink and a knife-edge" tone, marriage of morbid and entertaining | The Dresden Dolls, Voltaire ("When You're Evil") |
| **Electro-swing** | Vintage-modern production fusion, jazzy rhythmic energy, the "charming but dangerous" character | Hazbin Hotel's Alastor (Black Gryph0n & Baasik — "Insane"), "Hell's Greatest Dad" |
| **Dark pop** | Modern production values, pop accessibility, darkness-as-empowerment | Mad Tsai ("Killer Queen"), PEGGY & Ella Red ("Alice"), Melanie Martinez |
| **Electropunk / glam-dark** | "Decaying and desperate glamour" fused with "savagery and sex"; synth-driven dark pop with glam-punk allure | Sohodolls ("Bang Bang Bang Bang") — their self-description is nearly a genre definition for the production aesthetic |
| **Villain era / villain arc** | Cultural framing of villainy as self-empowerment and liberation | TikTok "villain era" playlists, the broader cultural phenomenon |
| **Anthemic alt-rock** | Arena-scale kinetic energy, driving beat-based production | Imagine Dragons ("Enemy", "Natural", "Bones") — the *energy template* |
| **Musical theatre villain songs** | Character-driven narrative, psychological inhabitation, "I Want" song inverted | Stephen Schwartz (Hellfire, "Badder"), Danny Elfman (Oogie Boogie, Jack's Lament), SIX ("Don't Lose Ur Head"), Heathers ("Candy Store") |

### 1.2 What Dark Revelry Is Not

The definition is precise. Adjacent aesthetics that share some qualities but diverge on the critical axis — **the villain's relationship to their own darkness** — are explicitly excluded:

| Aesthetic | Shared Quality | Divergence from Dark Revelry | Boundary Example |
|---|---|---|---|
| **Death metal / extreme metal** | Thematic darkness, high energy | Deliberately harsh and abrasive; the sound itself is meant to be aggressive, not accessible or pleasant | — |
| **Dark pop (melancholic variant)** | Dark production, modern sound | The narrator is *haunted* or *suffering*; the darkness is introspective pain, not gleeful villainy | Lou Bliss — "Killing Butterflies" (dark pop about heartbreak, not villainy) |
| **Dark pop (anxiety variant)** | Dark persona, alt-pop production | The narrator is *struggling* with their dark side, not celebrating it | Royal & the Serpent — "overwhelmed" (explicitly about anxiety and OCD) |
| **Battle junkie / arena rock** | High energy, dark tonality, driving rhythm | The darkness is about *fighting*, not *being evil*; the character enjoys the contest, not the cruelty | See § 2.3 and § 2.4 for detailed boundary analysis |
| **Horror soundtrack** | Darkness, dissonance, tension | Designed to frighten; the listener is positioned as victim, not co-conspirator | — |
| **Institutional horror** | Polite villain, bureaucratic cruelty, the villain enjoys themselves | The audience is horrified BY the polite cruelty, not laughing WITH it; realism removes aesthetic distance | Dolores Umbridge (Harry Potter) — "poisoned honey" voice, "at her happiest when inflicting pain," but the audience is positioned as victim. Nurse Ratched (One Flew Over the Cuckoo's Nest) — polite institutional control as horror. See § 2.5 Camp A vs Camp B for the critical distinction |
| **Gothic rock / darkwave** | Atmospheric darkness, theatrical elements | Melancholic and brooding; the tone is mournful, not celebratory | — |
| **Villain arc (anger variant)** | Empowerment through darkness | The villainy emerges from pain and is often angry/aggressive; dark revelry's villain was never hurt — they just enjoy it | Taylor Swift — "Look What You Made Me Do" (revenge origin, not gleeful villainy) |
| **Theatrical eccentric** | Theatrical delivery, quirky energy | Theatrical without darkness; eccentric and whimsical, but not villainous | Tally Hall — "The Bidding" (theatrical auction metaphor for dating — clever, not dark) |

### 1.3 The Diagnostic Question

The single question that distinguishes dark revelry from all adjacent aesthetics:

> **"Does the music make you feel like you're enjoying being evil, or does it make you feel something else that happens to be dark?"**

- *Enjoying being evil* → Dark revelry
- *Winning a fight* → Battle junkie (adjacent, not the same)
- *Enjoying the dark side while being good* → Line Rider (the bridge zone — see § 2.4)
- *Suffering beautifully* → Dark pop / gothic
- *Being frightened* → Horror
- *Being angry and powerful* → Villain arc (anger variant)

### 1.4 Playlist Validation: Spotify's "Villain Mode"

Spotify's curated "Villain Mode" playlist (37i9dQZF1DX3R7OWWGN4gH, 100 tracks, 1.4M saves, tagline: "entering my reputation era") provides empirical validation of the convergence — and demonstrates why formal style discrimination is needed.

Analysis of all 100 tracks reveals the playlist is approximately:
- **~40% pure dark revelry** — theatrical, character-driven, gleeful villainy (Scissor Sisters, MARINA, SIX, Melanie Martinez, Black Gryph0n, Ashnikko)
- **~30% battle junkie / power anthem** — kinetic energy, fight-scene soundtrack (Imagine Dragons, Fall Out Boy, KISS, Woodkid)
- **~30% melancholic dark / revenge** — suffering, anger, or empowerment-through-pain (Billie Eilish, Radiohead, Taylor Swift revenge tracks, Nirvana)

This confirms that even professional curators cannot cleanly separate dark revelry from adjacent aesthetics — the genre boundaries are felt but not formalized. The tracks that are *most* dark revelry share exactly the properties defined in this document: **theatrical, character-driven, gleeful, accessible production, the villain is happy**. The ones that drift toward adjacent zones are identifiable by the diagnostic question in § 1.3.

The playlist also surfaced several tracks and artists not previously identified that strengthened the reference corpus — see § 8.1.

---

## Part 2: The Character Archetype Spectrum

Dark revelry as a musical aesthetic serves a specific character archetype. Understanding the archetype clarifies what the music must express. Eight positions have been identified on the spectrum from pure dark revelry to the battle junkie boundary, each requiring slightly different musical treatment while sharing the core "darkness as positive experience" posture.

### 2.1 The Archetype Map

| Sub-Type | Description | Character Examples | Musical Energy |
|---|---|---|---|
| **The Charismatic Maniac** | Fully aware of evil, finds destruction genuinely joyful | Kefka (FFVI), Alastor (Hazbin Hotel), Oogie Boogie | Manic, circus-like, electro-swing, distorted laughter as rhythm |
| **The Scheming Virtuoso** | Enjoys the *craft* of manipulation as an art form | Noel Stollen (The Talker) | Sophisticated, controlled, jazz-influenced, builds with precision |
| **The Zealous Conqueror** | Unaware they're evil; sees carnage as divine triumph | Hekat (Empress of Mijak), Frollo (Hunchback) | Triumphant, martial, war drums, fanfares in minor key |
| **The Performative Villain** | Knows they're playing a role and luxuriates in it | Pencilgon (Shangri-La Frontier), The Grandmaster (Thor: Ragnarok), Dr. Villain (Poor Man's Poison) | Glamorous, runway-ready, fierce and fashionable, dark pop |
| **The Amused Duality** | Could be the worst thing in the world, finds this quietly funny | Seika (Strongest Exorcist), Tanya (Youjo Senki) | Controlled power, ironic contrast (cheerful melody + dark lyrics), military march energy |
| **The Sardonic Overseer** | Treats villainy as bureaucratic normalcy; cruelty expressed through politeness and passive aggression | GLaDOS (Portal), Hades (Disney's Hercules), The Stanley Parable Narrator | **Inverted dark revelry**: aggressively bright, major-key music weaponized as passive aggression — see § 2.5 |
| **The Line Rider** | Fundamentally good, but *genuinely enjoys* the dark/dangerous aspects of their role | Kaulder (Last Witch Hunter), Dante (Devil May Cry) | The bridge zone — seductive dark atmosphere for a protagonist who savors the shadow side |

### 2.2 What Unites the Core Six

The six primary sub-types (excluding The Line Rider) share three properties:

1. **The villain is happy.** Not empowered-through-pain. Not coolly detached. Not grimly determined. Genuinely, actively enjoying themselves.
2. **The darkness is the point, not a side effect.** The character doesn't enjoy *fighting* and happen to be dark (that's a battle junkie). They enjoy the *evil itself* — the manipulation, the destruction, the transgression.
3. **The audience is invited to enjoy it too.** The music positions the listener as co-conspirator, not victim or judge. The aesthetic distance (it's fiction, it's performance, it's a game) makes this psychologically safe.

### 2.3 The Dark Revelry / Battle Junkie Boundary

This boundary is critical for musical composition because the two archetypes share high energy and dark tonality but require fundamentally different musical expression:

```
DARK REVELRY ◄──────────────────────────────────────────────────────────► BATTLE JUNKIE
(enjoys the evil)                                                         (enjoys the fight)

                                                          Sardonic
Charismatic  Scheming  Zealous  Performative  Amused    Overseer  Line    │  Combat    Pure
Maniac       Virtuoso  Conqueror Villain      Duality   (inverted)Rider   │  Pragmatist Contest
   ▲            ▲         ▲         ▲           ▲          ▲        ▲     │     ▲         ▲
   │            │         │         │           │          │        │     │     │         │
Destruction  Manipul-  Triumph   Aesthetic   Quiet     Polite    Savors  │  Slips into Joy of
as joy       ation as  in god's  of being   amusement  cruelty   the    │  darkness   contest
             art form  name      evil       at irony   as process shadow │  under stress
```

Musical markers that place a piece on one side or the other:

| Musical Feature | Dark Revelry Indicator | Battle Junkie Indicator |
|---|---|---|
| Rhythmic feel | Swing, groove, dance-ready | Driving straight-eighth, march |
| Vocal character | Theatrical, character voice, spoken asides | Raw, powerful, belt |
| Harmonic language | Jazzy extensions, chromatic color, diminished passing | Power chords, modal, pentatonic |
| Dynamic profile | Sudden theatrical contrasts, dramatic pauses | Steady escalation, builds to climax |
| Lyrical posture | First-person villain monologue, "I" perspective | Second-person challenge, "you" address |
| Production aesthetic | Polished, layered, orchestrated | Raw, guitar-forward, compressed |

### 2.4 The Line Rider: Where Dark Revelry Meets Battle Junkie

The Line Rider is the sixth archetype position — a character who lives at the exact boundary between dark revelry and battle junkie. Unlike the core five, the Line Rider is fundamentally a *protagonist* (protector, hero, good person) who genuinely enjoys the dark, dangerous, or violent aspects of their role without letting it corrupt them or produce real consequences. The audience shares their enjoyment of "getting to be a little bad while being good."

**What defines a Line Rider:**
- Fundamentally a good person with a heroic or protective role
- Genuinely *enjoys* the darkness, danger, or violence — not just tolerates it
- The enjoyment is experienced as *spice* rather than *identity* — they can turn it off
- No real corruption or consequence from their enjoyment
- The audience is positioned to share their guilty pleasure

**Character examples:**
- **Kaulder** (Last Witch Hunter) — "positively jolly, gleefully seducing air hostesses, harassing witches, and being chummy with Michael Caine." Acknowledges "Salem was wrong" while clearly enjoying the hunt. Says "I'm not [concerned], but I do care."
- **Sung Jin-Woo** (Solo Leveling) — fundamentally selfless (resets reality to save everyone), but in combat shows cruelty, savors the fight, and nearly risks allies to fight an A-rank boss during a mining job
- **Kelvin** (Black Summoner) — openly called a Battle Junkie by everyone around him, "daydreams about life-and-death battles" and "does little to fix it," but balanced with genuine kindness
- **Dante** (Devil May Cry) — styles on demons while cracking jokes; the poster child for "having way too much fun being the good guy in a dark world"

**Musical implication:** The Line Rider needs elements of *both* dark revelry and battle junkie — the theatrical personality-driven quality of dark revelry (they have *character*, not just power) AND the kinetic driving force of battle junkie (they *are* in action). Ciara's "Paint It, Black" cover for The Last Witch Hunter lands precisely here: seductive atmospheric control (dark revelry) soundtracking an action hero (battle junkie). See Appendix A for the full case study.

**Style variant:**

```yaml
# The Line Rider (Kaulder, Dante, Jin-Woo in combat)
variant: "line_rider"
parent: "dark-revelry"
overrides:
  swingFactor: 0.15               # Less swing than core dark revelry, more drive
  defaultTempo: 130               # Action pace
  modeDistribution:
    harmonicMinor: 0.35           # Retains the dark color
    minor: 0.30
    dorian: 0.15
    mixolydian: 0.20              # More brightness — this is still a hero
  emotionalProfile:
    warmth: { min: 0.3, max: 0.6 }    # Cooler than core — action context
    valence: { min: 0.5, max: 0.8 }   # Still positive, but less gleeful
    tension: { min: 0.5, max: 0.85 }  # Can push higher — real stakes
  harmonyStyle:
    cadenceResolutionStrength: 0.85    # Very strong resolution — the hero wins
  dynamicsProfile:
    dramaticContrastProbability: 0.35  # Theatrical but not as extreme as core
    baseLevel: 0.65                    # Solid presence
  articulationProfile:
    legatoProbability: 0.30            # Less flowing than scheming virtuoso
    marcatoProbability: 0.25           # More commanding — action energy
    accentProbability: 0.25            # Emphatic strikes
```

### 2.5 The Sardonic Overseer: Dark Revelry Through Inverted Brightness

The Sardonic Overseer is a unique sub-type that operates through an *inverted* mechanism compared to all other dark revelry positions. Where standard dark revelry puts dark content in a bright emotional frame (minor key + positive energy), the Sardonic Overseer puts dark content in a bright *musical* frame (major key + pleasant melody). The darkness exists entirely in the lyrics and narrative context, while the music aggressively pretends everything is fine.

**What defines a Sardonic Overseer:**
- Treats villainy as *process* — bureaucratic, procedural, routine, tedious but necessary
- Expresses cruelty through *politeness and passive aggression*, not spectacle or force
- Is genuinely having fun but will *never admit it* — the enjoyment leaks out through sarcasm
- Considers the victim an inconvenience to be processed, not an enemy to be conquered
- Derives pleasure from the *process* (testing, evaluating, controlling) rather than the outcome

**Character examples:**
- **GLaDOS** (Portal) — "This was a triumph. I'm making a note here: HUGE SUCCESS." A passive-aggressive AI who treats your murder attempt as a mildly interesting workplace incident, and sings a cheerful pop song about it. "Still Alive" is the definitive sardonic overseer anthem — see § 8.1.
- **Hades** (Disney's Hercules) — treats being the Lord of the Dead as a tedious corporate management job. Makes deals with sarcastic professionalism ("Name's Hades, Lord of the Dead, hi, how ya doin'"), schemes with eye-rolling exasperation at his own minions. Villainy expressed through passive-aggressive professionalism.
- **The Stanley Parable Narrator** — becomes increasingly passive-aggressive as the player deviates from the "story." Starts polite and professional, progressively drops the mask. The entire game is a Sardonic Overseer having the time of their life being condescending at you.
- **Crowley** (Good Omens) — a demon who treats evil as a 9-to-5 job he's good at but slightly bored by. Takes credit for the M25 motorway as one of his greatest achievements (designing a road that generates low-level misery in millions of commuters forever). Evil expressed through systemic inconvenience.

**The critical boundary — Camp A vs Camp B:**

Not all "polite evil" characters are dark revelry. The Sardonic Overseer archetype splits into two camps based on whether the audience laughs WITH the villain or is horrified BY them:

| | Camp A — Dark Revelry | Camp B — Institutional Horror |
|---|---|---|
| **Audience position** | Co-conspirator (laughing with) | Victim (horrified by) |
| **Humor** | Load-bearing — the comedy provides aesthetic distance | Absent or bitter — the realism removes distance |
| **Examples** | GLaDOS, Hades, Stanley Parable Narrator, Crowley | Dolores Umbridge (Harry Potter), Nurse Ratched (One Flew Over the Cuckoo's Nest) |
| **Why the split?** | Sci-fi/fantasy context + absurdist humor = safe | Realistic institutional power + recognizable abuse = terrifying |

Umbridge is "at her happiest when inflicting pain," speaks in "poisoned honey," and wraps cruelty in paperwork, procedure, and pink cardigans. She *enjoys* herself. But the audience is NOT invited to enjoy it — everyone has met an Umbridge, and the lack of aesthetic distance makes her one of fiction's most *hated* villains rather than most *beloved*. Nurse Ratched operates identically: polite, bureaucratic, enjoys control, but positioned as institutional horror rather than entertainment.

**The humor is load-bearing.** Remove it and the Sardonic Overseer collapses from dark revelry into horror. GLaDOS saying "This was a triumph" is hilarious. Umbridge saying "I must not tell lies" while a child bleeds is nightmarish. Same mechanism (polite cruelty), opposite audience experience.

**Style variant (inverted brightness — see also § 7.1 for EmotionalState comparison):**

```yaml
# The Sardonic Overseer (GLaDOS, Hades, Stanley Parable Narrator)
variant: "sardonic_overseer"
parent: "dark-revelry"
overrides:
  # THE INVERSION: brightness is HIGH, not low — the music weaponizes cheerfulness
  emotionalProfile:
    brightness: { min: 0.6, max: 0.9, default: 0.75 }  # Aggressively bright
    tension: { min: 0.2, max: 0.4, default: 0.3 }      # Music avoids tension
    energy: { min: 0.5, max: 0.7, default: 0.6 }       # Office-casual, not manic
    warmth: { min: 0.7, max: 0.9, default: 0.8 }       # Saccharine, "poisoned honey"
    stability: { min: 0.8, max: 1.0, default: 0.9 }    # The bureaucratic machine hums smoothly
    valence: { min: 0.6, max: 0.9, default: 0.75 }     # Still positive — the villain IS happy
  modeDistribution:
    major: 0.60                   # The inversion: predominantly MAJOR key
    mixolydian: 0.20              # Slightly less bright than pure major
    dorian: 0.15                  # Hint of jazz sophistication
    minor: 0.05                   # Rare — only for the mask-slip moments
  swingFactor: 0.10               # Light, casual — not dramatic
  defaultTempo: 110               # Conversational pace, not urgent
  harmonyStyle:
    primaryCadence: "AuthenticMajor"
    diminishedPassingProbability: 0.05  # Almost none — the music refuses to be dark
    commonProgressions:
      - "I-IV-V-I"               # The most pleasant progression possible
      - "I-vi-IV-V"              # Pop standard — aggressively normal
      - "I-V-vi-IV"              # The "four chords" — weaponized banality
  dynamicsProfile:
    baseLevel: 0.45               # Moderate — conversational, not commanding
    dramaticContrastProbability: 0.1  # Minimal — the overseer doesn't need drama
  articulationProfile:
    legatoProbability: 0.55       # Smooth, pleasant, unthreatening
    staccatoProbability: 0.25     # Bouncy, cheerful
    accentProbability: 0.10       # Rare emphasis — passive aggression doesn't accent
    marcatoProbability: 0.10      # Rare — no commanding presence needed
```

---

## Part 3: EmotionalState Mapping

### 3.1 The Counterintuitive Configuration

The existing EmotionalState model (from `MusicStoryteller.State`) has six dimensions. Dark revelry requires a specific configuration that most music cognition frameworks would flag as contradictory:

| Dimension | Dark Revelry Range | Typical Association | Why Dark Revelry Breaks It |
|---|---|---|---|
| **Tension** | 0.5 – 0.8 | High tension → negative affect | The tension is *thrilling*, not uncomfortable; contrastive valence reframes it as pleasure |
| **Brightness** | 0.1 – 0.3 | Low brightness → sadness, gloom | The darkness is *atmospheric*, not mournful; combined with high energy it reads as "dangerous fun" |
| **Energy** | 0.7 – 1.0 | High energy + low brightness is rare | This combination is the core signature; the upbeat tempo prevents the darkness from settling into melancholy |
| **Warmth** | 0.5 – 0.8 | Warmth + darkness is contradictory | The villain is *inviting* — seductive, charismatic, welcoming you into their world |
| **Stability** | 0.6 – 0.9 | High stability → comfort, grounding | Strong rhythmic foundation gives the listener a solid groove *even as* the harmony ventures into dark territory |
| **Valence** | 0.6 – 0.9 | Positive valence + dark harmony = ??? | **This is the key.** The villain is *happy*. The music must express genuine positive affect through dark harmonic language |

### 3.2 Why This Configuration Is Novel

In the cross-book mapping of Huron (ITPRA), Lerdahl (TPS), and Juslin (BRECVEMA), the standard model associates:
- High tension → low valence (discomfort)
- Low brightness → low valence (sadness)
- High energy → high valence (joy/excitement)

Dark revelry resolves this tension through **Huron's contrastive valence mechanism**: the darkness creates tension that the confident performance and strong rhythmic drive *immediately* reframe as pleasure. The "safe context → positive reframe" Appraisal pathway from the ITPRA model explains why: the listener *knows* it's fiction/performance, so the limbic Reaction (surprise, tension from dark harmony) is consciously reappraised as pleasurable.

This means the GOAP planner, when targeting a dark revelry emotional state, must recognize `{high tension, positive valence}` as a valid target rather than a contradiction to be resolved. The planner should not try to reduce tension to achieve positive valence — it should maintain *both simultaneously* through the specific mechanisms described in Part 4.

### 3.3 BRECVEMA Mechanism Activation Profile

Each of the eight BRECVEMA pathways has a target activation level for dark revelry:

| Mechanism | Target Activation | How It's Achieved |
|---|---|---|
| **Brain Stem Reflex** | Moderate (0.4–0.6) | Sudden dynamic contrasts, theatrical pauses before dramatic returns, bass drops |
| **Rhythmic Entrainment** | High (0.7–0.9) | Strong pulse at 110–145 BPM, swing feel, danceable groove that locks the body into movement |
| **Emotional Contagion** | High (0.7–0.9) | Bold, confident vocal prosody; the villain's *delight* transmits directly via contagion, bypassing moral reasoning |
| **Evaluative Conditioning** | Moderate (0.4–0.6) | Genre cues from jazz, swing, theatrical traditions activate "entertainment" associations |
| **Visual Imagery** | Moderate-High (0.5–0.7) | Minor key + theatrical production triggers cinematic "villain scene" imagery |
| **Episodic Memory** | N/A | Listener-specific; cannot be composed for |
| **Musical Expectancy** | Moderate-High (0.5–0.7) | Dark harmonic surprises that resolve *satisfyingly* — the listener ventures into dangerous harmonic territory and comes back safely |
| **Aesthetic Judgment** | Moderate (0.4–0.6) | Sophisticated production and harmonic language triggers appreciation |

The critical insight: **Rhythmic Entrainment and Emotional Contagion must both be high.** Entrainment keeps the body moving (preventing the darkness from inducing passivity), and Contagion transmits the villain's joy directly (overriding the moral discomfort the harmonic darkness might otherwise produce).

---

## Part 4: Style Definition

### 4.1 YAML Style Template

The following style definition follows the established YAML format from the MusicTheory SDK's `StyleLoader`:

```yaml
id: "dark-revelry"
name: "Dark Revelry"
category: "theatrical"
description: >
  Music expressing gleeful villainy — darkness experienced as celebration.
  Minor-key harmonic language with high energy, strong rhythmic drive,
  and theatrical dramatic contrasts. The villain is happy.
  Production aesthetic: "decaying glamour meets savagery" — polished
  surface with sinister substance.

modeDistribution:
  harmonicMinor: 0.40    # The "exotic evil" sound — raised 7th
  minor: 0.30            # Standard darkness
  dorian: 0.15           # The jazz-villain color (natural 6th over minor)
  phrygian: 0.10         # Exotic tension, the "Mediterranean menace"
  mixolydian: 0.05       # Occasional brightness-in-darkness

intervalPreferences:
  stepProbability: 0.50   # Less stepwise than folk — more dramatic leaps
  skipProbability: 0.30   # Thirds and fourths for theatrical declamation
  leapProbability: 0.20   # Bold leaps for dramatic emphasis

defaultTempo: 128
defaultMeter:
  numerator: 4
  denominator: 4

swingFactor: 0.25         # Light swing even in non-jazz contexts

formTemplates:
  - sections: "ABAB"      # Verse-Chorus standard
  - sections: "AABA"      # Show tune / villain monologue
  - sections: "ABABCAB"   # Extended with bridge (the villain's revelation)

tuneTypes:
  - name: "villain_anthem"
    meter: { numerator: 4, denominator: 4 }
    tempoRange: [118, 145]
    defaultForm: "ABABCAB"
    description: "Full theatrical villain number with dramatic bridge"

  - name: "sinister_groove"
    meter: { numerator: 4, denominator: 4 }
    tempoRange: [110, 130]
    defaultForm: "ABAB"
    description: "Stripped-down groove — the scheming virtuoso's backdrop"

  - name: "dark_waltz"
    meter: { numerator: 3, denominator: 4 }
    tempoRange: [140, 170]
    defaultForm: "AABA"
    description: "Menacing elegance — the performative villain's dance"

  - name: "carnival_march"
    meter: { numerator: 4, denominator: 4 }
    tempoRange: [130, 150]
    defaultForm: "ABAB"
    description: "Manic circus energy — the charismatic maniac's parade"

  - name: "seductive_dark"
    meter: { numerator: 4, denominator: 4 }
    tempoRange: [95, 118]
    defaultForm: "AABA"
    description: "Slow-burn atmospheric menace — jazz-swing with dark cinematic overtures (Cil 'Bloodsucker' energy)"

harmonyStyle:
  primaryCadence: "AuthenticMinor"
  dominantPrepProbability: 0.7        # Strong dominant preparations
  secondaryDominantProbability: 0.35  # Frequent chromatic color
  diminishedPassingProbability: 0.25  # Tritone as passing color, not sustained dread
  commonProgressions:
    - "i-III-VII-VI"         # The "epic minor" progression
    - "i-iv-VII-III"         # Descending power — the villain's swagger
    - "i-VI-III-VII"         # Ascending through darkness
    - "iv-VII-III-V"         # Pre-chorus build — the tension before the villain's revelation
    - "i-v-VI-VII"           # The ironic march (Youjo Senki energy)
  cadenceResolutionStrength: 0.8  # Strong V→i resolutions — darkness resolves SATISFYINGLY

emotionalProfile:
  tension: { min: 0.5, max: 0.8, default: 0.65 }
  brightness: { min: 0.1, max: 0.3, default: 0.2 }
  energy: { min: 0.7, max: 1.0, default: 0.85 }
  warmth: { min: 0.5, max: 0.8, default: 0.65 }
  stability: { min: 0.6, max: 0.9, default: 0.75 }
  valence: { min: 0.6, max: 0.9, default: 0.75 }

dynamicsProfile:
  baseLevel: 0.6                    # Moderately loud baseline
  dramaticContrastProbability: 0.4  # Frequent sudden dynamic shifts
  sforzandoProbability: 0.15        # Theatrical accents
  subito_piano_probability: 0.10    # The villain's dramatic whisper before the kill

articulationProfile:
  staccatoProbability: 0.30         # Bouncy, playful articulation
  accentProbability: 0.20           # Emphatic
  legatoProbability: 0.35           # Flowing theatrical lines
  marcatoProbability: 0.15          # Commanding presence
```

### 4.2 Sub-Type Variants

Each archetype sub-type modifies the base style. These can be implemented as style overlays or separate style definitions:

```yaml
# The Charismatic Maniac (Kefka, Alastor)
variant: "charismatic_maniac"
parent: "dark-revelry"
overrides:
  swingFactor: 0.50               # Full swing feel
  modeDistribution:
    dorian: 0.35                  # Jazz-forward
    harmonicMinor: 0.30
  defaultTempo: 140               # Manic energy
  dynamicsProfile:
    dramaticContrastProbability: 0.6  # Maximum theatrical contrast
  articulationProfile:
    staccatoProbability: 0.40     # Bouncy, circus-like
  harmonyStyle:
    diminishedPassingProbability: 0.35  # More tritone color

# The Scheming Virtuoso (Noel Stollen)
variant: "scheming_virtuoso"
parent: "dark-revelry"
overrides:
  swingFactor: 0.35               # Cool jazz swing
  defaultTempo: 118               # Controlled, deliberate
  modeDistribution:
    dorian: 0.40                  # Sophisticated jazz color
    harmonicMinor: 0.25
    minor: 0.20
    phrygian: 0.15
  dynamicsProfile:
    baseLevel: 0.5                # Quieter — the schemer doesn't need to shout
    dramaticContrastProbability: 0.25
  articulationProfile:
    legatoProbability: 0.50       # Smooth, calculated

# The Zealous Conqueror (Hekat)
variant: "zealous_conqueror"
parent: "dark-revelry"
overrides:
  swingFactor: 0.05               # Straight — martial, not jazzy
  defaultTempo: 125               # Marching pace
  modeDistribution:
    harmonicMinor: 0.45           # Exotic religious intensity
    phrygian: 0.25                # Sacred menace
    minor: 0.20
    dorian: 0.10
  harmonyStyle:
    dominantPrepProbability: 0.8  # Relentless drive toward resolution
  dynamicsProfile:
    baseLevel: 0.7                # Loud — the conqueror commands
  articulationProfile:
    marcatoProbability: 0.35      # Commanding emphasis

# The Performative Villain (Pencilgon)
variant: "performative_villain"
parent: "dark-revelry"
overrides:
  defaultTempo: 120               # Fashion-show pace
  modeDistribution:
    minor: 0.40                   # Pop-minor foundation
    harmonicMinor: 0.25
    dorian: 0.20
    mixolydian: 0.15              # More brightness — glamour
  dynamicsProfile:
    dramaticContrastProbability: 0.3
  articulationProfile:
    legatoProbability: 0.45       # Flowing, elegant

# The Amused Duality (Tanya, Seika)
variant: "amused_duality"
parent: "dark-revelry"
overrides:
  swingFactor: 0.10               # Mostly straight, hint of irony
  defaultTempo: 132               # Brisk military march
  harmonyStyle:
    commonProgressions:
      - "i-v-VI-VII"             # The ironic march
      - "i-III-iv-V"             # Building with restraint
    secondaryDominantProbability: 0.2  # Less chromatic — controlled
  emotionalProfile:
    warmth: { min: 0.3, max: 0.5 }    # Cooler — the duality keeps distance

# The Line Rider (Kaulder, Dante) — see § 2.4 for full definition
```

---

## Part 5: Storyteller Integration

### 5.1 GOAP Planning Targets

When the MusicStoryteller receives a composition request with `styleId: "dark-revelry"`, the GOAP planner must:

1. **Accept the high-tension/positive-valence target as non-contradictory.** The default planning heuristic should not attempt to reduce tension to achieve positive valence. Instead, the planner should recognize that for this style, tension and valence are *decoupled* — both can be high simultaneously.

2. **Prioritize rhythmic entrainment.** The planner should select actions that establish and maintain a strong pulse early, because the groove is what prevents the darkness from inducing melancholy. Loss of rhythmic drive is the single most damaging failure mode for this style.

3. **Use harmonic darkness for *color*, not *dread*.** Diminished chords, tritones, and chromatic alterations should be used as passing flavors that resolve quickly, not as sustained tension. The darkness is *playful*, not oppressive.

4. **Favor theatrical dynamic contrasts.** The planner should select `sudden_dynamic_accent` and `subito_piano` actions at higher frequency than neutral styles. These BSR-triggering moments are what give dark revelry its performative, character-driven energy.

5. **Maintain strong cadential resolution.** Every dark harmonic excursion should resolve satisfyingly. The Lerdahl chord distance (δ) may be high during phrases (adventurous harmony), but the phrase-level tension must resolve via strong V→i cadences. The listener ventures into dangerous territory and *always comes back safely*.

### 5.2 Narrative Template: "The Villain's Monologue"

A new narrative template optimized for dark revelry compositions:

```yaml
narrative:
  id: "villain_monologue"
  description: >
    The classic villain song structure: confident introduction,
    escalating revelation of power/scheme, theatrical bridge
    (the villain's moment of philosophical reflection), and
    triumphant final declaration.
  phases:
    - name: "Entrance"
      bars: 4
      emotional_target:
        tension: 0.4
        brightness: 0.2
        energy: 0.6
        warmth: 0.7
        stability: 0.8
        valence: 0.7
      description: >
        The villain appears. Confident, controlled, charismatic.
        Groove established, dark harmonic color introduced.
        The musical equivalent of a slow smile.

    - name: "Declaration"
      bars: 8
      emotional_target:
        tension: 0.6
        brightness: 0.2
        energy: 0.8
        warmth: 0.6
        stability: 0.7
        valence: 0.8
      description: >
        The villain reveals who they are and what they want.
        Energy builds, harmonic language becomes bolder.
        The "I Want" song, but what they want is deliciously wrong.

    - name: "Escalation"
      bars: 8
      emotional_target:
        tension: 0.7
        brightness: 0.15
        energy: 0.9
        warmth: 0.6
        stability: 0.6
        valence: 0.85
      description: >
        The villain's power on full display. Chromatic harmony,
        rhythmic intensity, theatrical dynamic contrasts.
        The audience is swept along.

    - name: "Reflection"
      bars: 4
      emotional_target:
        tension: 0.5
        brightness: 0.25
        energy: 0.4
        warmth: 0.8
        stability: 0.7
        valence: 0.6
      description: >
        The dramatic pause. The villain gets philosophical.
        Energy drops, warmth increases — this is their most
        human moment. A breath before the final surge.

    - name: "Triumph"
      bars: 8
      emotional_target:
        tension: 0.75
        brightness: 0.2
        energy: 1.0
        warmth: 0.65
        stability: 0.8
        valence: 0.9
      description: >
        The full villain anthem. Maximum energy, maximum positive
        valence, strong rhythmic drive, dark harmonic language
        resolving triumphantly. The villain wins — and loves it.

    - name: "Exit"
      bars: 4
      emotional_target:
        tension: 0.3
        brightness: 0.2
        energy: 0.5
        warmth: 0.7
        stability: 0.9
        valence: 0.75
      description: >
        The theatrical bow. Energy subsides but the villain
        remains in control. A final knowing gesture.
        The musical equivalent of a wink.
```

### 5.3 Lyric Frame Suggestions

When the assisted authoring system generates frame suggestions (structural prompts for the composer to fill), dark revelry frames should follow the villain song tradition analyzed by Stephen Schwartz:

| Phase | Frame Suggestion | Psychological Approach |
|---|---|---|
| **Entrance** | "Introduce the character's presence — who are they to the world?" | Inhabit the villain's perspective; no external judgment |
| **Declaration** | "What does the villain want? What delights them?" | M. Scott Peck's definition: evil as "imposing one's will" — but the villain experiences this as freedom |
| **Escalation** | "Show the scope of their power or scheme" | The triple-rhyme escalation technique (Schwartz): building rhythmic momentum through stacking claims |
| **Reflection** | "The one vulnerable moment — what drives them beneath the surface?" | The best villain songs don't judge; they inhabit so completely that the audience experiences the villain's pleasure firsthand |
| **Triumph** | "The final declaration — the villain fully realized" | The villain is not transformed by the song; they were already this. The song is revelation, not development |
| **Exit** | "The promise of return, the lingering presence" | The villain doesn't need the audience's approval. They exit on their own terms |

---

## Part 6: MusicComposer SDK Integration

### 6.1 Assisted Authoring with Dark Revelry

When a human composer selects the dark revelry style in the MusicComposer workbench (described in [COUNTERPOINT-COMPOSER-SDK.md](COUNTERPOINT-COMPOSER-SDK.md)), the constraint system and suggestion engine should be configured with dark-revelry-specific behavior:

**Template Validation Adjustments:**
- **Energy compliance**: Flag sections where energy drops below 0.6 during the Declaration/Escalation/Triumph phases as warnings — "Dark revelry requires sustained energy here; energy below 0.6 risks losing the groove"
- **Valence monitoring**: If harmonic analysis detects the composition entering a low-valence region (sustained unresolved dissonance, descending energy without rhythmic drive), flag it — "The darkness is becoming melancholic; consider adding rhythmic energy or resolving to a stronger cadence"
- **Rhythmic drive detection**: Monitor note onset density and rhythmic regularity; flag passages where the pulse becomes ambiguous — "Rhythmic entrainment is critical for this style; the groove is weakening here"

**Note Suggestion Preferences:**
- At conflict points, prefer chord tones from diminished and dominant-seventh chords (the "spicy" options) over plain triadic tones
- Suggest chromatic passing tones more frequently than in neutral styles
- When a consonant alternative is needed, prefer the option that maintains melodic momentum (ascending options during build sections, the more dramatic leap during climax sections)

**Counterpoint-Specific Guidance:**
- When composing a dark revelry counterpoint against an existing piece, the workbench should prioritize contrary motion during the Escalation and Triumph phases (maximum textural independence = maximum theatrical impact)
- Rhythmic complementarity should be high (0.7+) — when one voice is active, the other should breathe, creating call-and-response theatrical dialogue

### 6.2 The Dark Revelry Decision Tree

Following the convergent decision tree model from the MusicComposer SDK (Part 4B of COUNTERPOINT-COMPOSER-SDK.md), dark revelry compositions offer a specifically flavored set of choices at each level:

| Decision Level | Neutral Style Options | Dark Revelry Options |
|---|---|---|
| Section mood | "Bright, neutral, dark, tense" | "Scheming, theatrical, manic, triumphant, seductive" |
| Phrase opening | "Start on root, third, or fifth" | "Start on root (power), flat third (darkness), or seventh (tension)" |
| Climax note | "Step up or leap up" | "Diminished leap (evil drama) or tritone resolution (satisfying darkness)" |
| Rhythmic feel | "Straight, swung, or syncopated" | "Swung strut, syncopated menace, or driving march" |
| Dynamic gesture | "Crescendo, decrescendo, steady" | "Theatrical sforzando, subito piano whisper, or relentless build" |

At every level, the options are renamed and reweighted to match the dark revelry aesthetic, but the underlying constraint system remains the same. Bad choices have been filtered out; the composer chooses character within guaranteed musical validity.

---

## Part 7: Distinguishing from Adjacent Styles

### 7.1 Style Comparison Matrix

For implementation, adjacent styles must be distinguishable by their EmotionalState profiles. The following comparison ensures the GOAP planner and the style system can unambiguously select the correct target:

| Dimension | Dark Revelry | Sardonic Overseer | Line Rider | Battle Junkie | Dark Pop (Melancholic) | Horror | Gothic |
|---|---|---|---|---|---|---|---|
| **Tension** | 0.5–0.8 | **0.2–0.4** | 0.5–0.85 | 0.6–0.9 | 0.3–0.6 | 0.7–1.0 | 0.4–0.7 |
| **Brightness** | 0.1–0.3 | **0.6–0.9** | 0.15–0.4 | 0.2–0.5 | 0.1–0.3 | 0.0–0.2 | 0.1–0.3 |
| **Energy** | **0.7–1.0** | 0.5–0.7 | **0.7–1.0** | **0.8–1.0** | **0.2–0.5** | 0.3–0.7 | **0.2–0.5** |
| **Warmth** | **0.5–0.8** | **0.7–0.9** | 0.3–0.6 | 0.2–0.4 | 0.4–0.7 | 0.0–0.2 | 0.3–0.5 |
| **Stability** | **0.6–0.9** | **0.8–1.0** | 0.6–0.9 | 0.5–0.8 | 0.4–0.7 | **0.1–0.4** | 0.5–0.7 |
| **Valence** | **0.6–0.9** | 0.6–0.9 | 0.5–0.8 | 0.5–0.8 | **0.1–0.4** | **0.0–0.2** | **0.2–0.4** |

The unique dark revelry (standard) signature: **simultaneously high energy, high warmth, high stability, high valence, AND low brightness, high tension.** No other style shares this exact configuration.

The Sardonic Overseer is dark revelry's *mirror image*: it shares the high valence (villain is happy) and high warmth (villain is inviting) but inverts brightness to high and tension to low. The music aggressively pretends everything is fine — the darkness exists only in context.

The most easily confused pair is dark revelry vs. battle junkie. The discriminators:
- **Warmth**: Dark revelry is warm (inviting, seductive); battle junkie is cold (impersonal, aggressive)
- **Stability**: Dark revelry has a slightly more solid groove; battle junkie allows more rhythmic chaos
- **Swing factor**: Dark revelry swings; battle junkie drives straight

The Line Rider overlaps with both — distinguishable by intermediate warmth (0.3–0.6, between dark revelry's inviting warmth and battle junkie's coldness) and slightly higher brightness allowance (the hero is still fundamentally in the light).

### 7.2 Automated Style Classification

When the system needs to classify an incoming MIDI-JSON composition or detect style drift during assisted authoring, the following heuristics can supplement EmotionalState analysis:

| Heuristic | Dark Revelry Signal | Counter-Signal |
|---|---|---|
| Swing ratio > 0.15 at tempo > 110 | Strong | (battle junkie: swing < 0.10) |
| Diminished chords used as passing (< 2 bars) | Strong | (horror: diminished sustained > 4 bars) |
| V→i cadence strength > 0.7 | Strong | (gothic: cadence avoidance, deceptive resolutions) |
| Dynamic contrast events > 3 per 16 bars | Strong | (dark pop melancholic: steady dynamics) |
| Harmonic minor mode > 30% of passage | Strong | (battle junkie: modal/pentatonic) |
| Note onset density > 0.6 with regular spacing | Strong | (gothic: sparse, irregular) |
| Jazz-swing sampling or Portishead-style trip-hop texture | Strong | (Cil "Bloodsucker" lineage — jazz-dark-pop fusion) |

---

## Part 8: Reference Material

### 8.1 Musical Reference Points

Tracks that exemplify the dark revelry aesthetic, organized by sub-type. Tracks marked with ✦ were identified through analysis of Spotify's "Villain Mode" playlist.

**The Charismatic Maniac:**
- Black Gryph0n & Baasik — "Insane" (Hazbin Hotel)
- Hazbin Hotel OST — "Hell's Greatest Dad", "Stayed Gone", "Alastor's Reprise"
- Danny Elfman — "Oogie Boogie's Song" (Nightmare Before Christmas)
- Danny Elfman — "Friends on the Other Side" (Princess and the Frog)
- Voltaire — "When You're Evil"
- ✦ Scissor Sisters — "I Can't Decide" — the purest dark revelry track identified; upbeat disco-pop show tune about cheerfully debating how to murder someone. Used in Doctor Who's "Last of the Time Lords" where the Master dances to it while torturing the Doctor. Listed on the Villain Song Wiki. The definitive "the villain is having the time of their life" song.

**The Scheming Virtuoso / Performative Villain:**
- Mad Tsai — "Killer Queen", "HOUNDSOFHELL"
- PEGGY & Ella Red — "Alice (Red Queen Version)"
- ✦ Ella Red — "He Asked for It" (solo villain energy from the "Alice" collaborator)
- John Michael Howell — "Medusa"
- Melanie Martinez — "Mad Hatter", "Carousel", "Class Fight"
- Scene Queen — "Barbie & Ken"
- ✦ Cil — "Bloodsucker" — jazz-swing production with dark cinematic overtures, samples Portishead's "Glory Box"; "vampirically irresistible" sound. The jazz-swing + dark cinematic + pop polish formula is textbook dark revelry.
- ✦ MARINA — "Bubblegum Bitch" — gleeful performative villainy in pop-coated menace
- ✦ Shaya Zamora — "Pretty Little Devil" — the title literally names the archetype
- ✦ Banshee — "Daughter of Eve" — self-described "fairy metal" (hyperpop + witch house + symphonic black metal); the juxtaposition of delicate and brutal. Edge case but shares the structural principle of darkness through an aesthetically beautiful frame.

**The Zealous Conqueror / Amused Duality:**
- Youjo Senki — "Los! Los! Los!" (Aoi Yuuki as Tanya Degurechaff)
- Poor Man's Poison / Dr. Villain — "Welcome to the Show", "This is the End"
- ✦ ILUKA — "Cry Evil!" — "stomp-a-licious country/folk anthem" reclaiming the word "evil" as female empowerment; "Eve took a bite of the fruit from the tree / Brought death to the world with a sin so deep." Zealous conqueror energy — triumphant evil through righteous framing.

**Musical Theatre Villain Songs (the theatrical tradition):**
- Stephen Schwartz — "Hellfire" (Hunchback of Notre Dame)
- Stephen Schwartz — "No Good Deed" (Wicked)
- Alan Menken — "Gaston" (Beauty and the Beast)
- ✦ SIX — "Don't Lose Ur Head" — musical theatre villain song as modern pop banger (Anne Boleyn)
- ✦ Heathers — "Candy Store" — theatrical villain number, the Heathers reveling in cruelty as social performance
- ✦ Sarah Jeffery / Disney — "Queen of Mean" — Disney Channel villain song, theatrical dark pop

**The Sardonic Overseer (inverted brightness — dark content in aggressively bright music):**
- Jonathan Coulton / Ellen McLain — "Still Alive" (Portal) — the definitive sardonic overseer anthem. A cheerful, catchy, major-key indie-pop tune where a passive-aggressive AI gloats about surviving your murder attempt and describes your death as a workplace memo. Both tracks are listed on the Villain Song Wiki. The music's *refusal to be dark* IS the darkest thing about it — the brightness is weaponized as contempt. GLaDOS conveys "emotion in a non-emotional way" (Coulton). The most extreme example of dark-content-over-bright-music in the entire corpus.
- Jonathan Coulton / Ellen McLain — "Want You Gone" (Portal 2) — the sardonic overseer with emotional depth. Coulton described it as "a break-up song about a computerized voice falling out of love with a mute girl." GLaDOS is still passive-aggressive ("Oh how we laughed and laughed / Except I wasn't laughing") but genuinely vulnerable beneath the sarcasm. Dark revelry mixed with something bittersweet — the villain is having fun AND having feelings.

**Dark-Content-Over-Bright-Music (the structural contrast lineage):**
- ✦ I DONT KNOW HOW BUT THEY FOUND ME — "Choke" — "channels both the spirit of the Beach Boys and My Chemical Romance in a single cohesive song" with "tongue-in-cheek irreverent humor and macabre songwriting." Explicitly builds on the dark-content-over-bright-music contrast mechanism.
- ✦ Sohodolls — "Bang Bang Bang Bang" — self-described as "decaying and desperate glamour" + "savagery and sex." Blends glam rock, punk, hip-hop, and big band swing into "twisted dark pop." A connective tissue artist between dark cabaret and modern dark pop.

**The Energy Template (dark revelry adjacent — bridge to battle junkie):**
- Imagine Dragons — "Enemy", "Natural", "Bones", "Sharks"
- ✦ Fall Out Boy — "Centuries", "My Songs Know What You Did In The Dark"
- ✦ Twenty One Pilots — "Heathens"

**The Line Rider (protagonist enjoying the dark side):**
- Ciara — "Paint It, Black" (The Last Witch Hunter cover) — see Appendix A
- ✦ Everybody Loves an Outlaw — "I See Red"
- ✦ Sam Tinnesz — "Play with Fire"
- ✦ Barns Courtney — "Glitter & Gold"

**Dark Reinterpretation (same music, villain context):**
- ✦ Lorde — "Everybody Wants to Rule the World" (Hunger Games cover) — transforms Tears for Fears synth-pop into dystopian villain threat. The zealous conqueror sub-type applied through cover arrangement. "It doesn't feel like a warm welcome, but a horrifying revelation that war has already begun."
- ✦ Soap&Skin — "Me and the Devil" (Robert Johnson cover) — dark reinterpretation of the Delta blues original

**The Missed Soundtrack (archetype without its music — see Appendix B):**
- The Vampire Chronicles adaptations (1994–2002) — Lestat is the foundational dark revelry character (a vampire who *loves* being a vampire and becomes a rock star to celebrate it publicly), but both soundtracks missed the aesthetic: Interview with the Vampire (1994, Elliot Goldenthal) scored Louis's *melancholy*, not Lestat's *joy*; Queen of the Damned (2002, Jonathan Davis/Korn) scored nu metal *aggression*, not vampire *glamour*. Aaliyah's planned vocal contribution — which might have captured Akasha's seductive dark sovereignty — was lost to her death in 2001. The cultural moment that would have needed dark revelry music for these films hadn't arrived yet.

### 8.2 Academic and Theoretical Grounding

| Source | Relevance |
|---|---|
| Huron, D. (2006). *Sweet Anticipation* — ITPRA model | Contrastive valence: why resolved dark stimuli produce disproportionate pleasure. The "safe context → positive reframe" Appraisal pathway explains the core dark revelry mechanism |
| Juslin, P. N. (2019). *Musical Emotions Explained* — BRECVEMA | Emotional Contagion pathway: confident vocal prosody transmits positive affect, bypassing moral reasoning about content. Explains why the villain's *delight* is contagious |
| Lerdahl, F. (2001). *Tonal Pitch Space* | Chord distance quantification: dark revelry uses high sequential tension (adventurous harmony) with clear hierarchical structure (predictable phrase-level resolution). Controlled danger |
| Cheung et al. (2019). "Uncertainty and surprise jointly predict musical pleasure" | The pleasure equation: `Pleasure ~ β₀ + β₁(H) + β₂(IC) + β₃(H × IC)`. Dark revelry operates in the moderate-entropy, moderate-IC sweet spot — enough harmonic unpredictability to be interesting, enough structure to be parseable |
| Bakhtin, M. (1965). *Rabelais and His World* — Carnivalesque theory | Dark revelry as musical carnival: temporary inversion of moral hierarchies where transgression becomes communal joy. The theoretical framework for why "embrace evil" music is psychologically healthy |
| Schwartz, S. (Interview, MusicalWriters.com) — Villain song composition | Technique of psychological inhabitation: "villains who don't know they're villainous." The composer must *become* the villain to write their song authentically |
| Gioia, T. "The Sound of Evil" — The American Scholar | Cultural history of the classical-music-villain association. Relevant to understanding why *sophisticated* darkness (not crude aggression) is the key to the dark revelry aesthetic |
| Grizzard et al. (2019). "Who can resist a villain?" — ScienceDirect | Psychology of villain enjoyment: identification > moral judgment. Personality trait correlations with villain engagement (openness, imaginative flexibility) |

### 8.3 Character Archetype References

| Character | Source | Sub-Type | Key Quality |
|---|---|---|---|
| Alastor | Hazbin Hotel | Charismatic Maniac | 1920s radio charm + serial killer delight |
| Kefka Palazzo | Final Fantasy VI | Charismatic Maniac | Joyful nihilism, "destruction is the goal" |
| Noel Stollen | The Most Notorious "Talker" | Scheming Virtuoso | Manipulation as art, "always has a justification on his lips" |
| Tanya Degurechaff | Youjo Senki | Amused Duality | Military genius in a child's body, slasher smile during combat |
| Hekat | Godspeaker Trilogy (Karen Miller) | Zealous Conqueror | "She hurts people and enjoys it" — through divine purpose |
| Pencilgon | Shangri-La Frontier | Performative Villain | Fashion model playing a sadistic PKer, "striking a pose even when caught off guard." Causes so much destruction in Galaxy Heroes they redesign the game around preventing it |
| The Grandmaster | Thor: Ragnarok (MCU) | Performative Villain | Jeff Goldblum as a cosmic being who runs a gladiatorial death arena as a cocktail party. Treats Thor's enslavement as a fun social event. "It's a tie!" Villainy as eccentric hospitality |
| Seika Lamprogue | Strongest Exorcist | Amused Duality | The Demon King fighting for humanity, quietly amused by the irony |
| Oogie Boogie | Nightmare Before Christmas | Charismatic Maniac | Loud, swingy jazz villain — gambling, showmanship, cruelty as entertainment |
| Frollo | Hunchback of Notre Dame | Zealous Conqueror | Self-righteous evil with genuine belief — Schwartz's "favorite villain character" |
| The Master | Doctor Who | Charismatic Maniac | Dances to "I Can't Decide" while torturing the Doctor — the canonical screen example |
| Lestat de Lioncourt | The Vampire Chronicles (Anne Rice) | Charismatic Maniac / Performative Villain | The foundational literary example: "loves being a vampire," becomes a rock star to *celebrate* his nature publicly. Established the archetype decades before it had a name — see Appendix B |
| Akasha | Queen of the Damned (Anne Rice) | Zealous Conqueror | The vampire queen as divine mandate — supreme power experienced as righteous destiny. Aaliyah's portrayal was pure dark glamour; her planned soundtrack contribution was lost to tragedy |
| GLaDOS | Portal (Valve) | Sardonic Overseer | Passive-aggressive AI treating murder as a workplace memo. "This was a triumph / I'm making a note here: HUGE SUCCESS." The canonical example — see § 2.5 |
| Hades | Hercules (Disney) | Sardonic Overseer | Lord of the Dead as corporate middle manager. Makes deals with sarcastic professionalism, schemes with eye-rolling exasperation at his own minions |
| The Narrator | The Stanley Parable | Sardonic Overseer | Increasingly passive-aggressive narrator who clearly enjoys the power dynamic of controlling someone's reality while judging their every decision |
| Crowley | Good Omens | Sardonic Overseer | A demon who treats evil as a 9-to-5 job; designed the M25 motorway to generate low-level misery in millions of commuters forever. Evil as systemic inconvenience |
| Kaulder | The Last Witch Hunter | Line Rider | "Positively jolly, gleefully harassing witches"; enjoys the hunt while acknowledging "Salem was wrong" |
| Dante | Devil May Cry | Line Rider | Styles on demons while cracking jokes; the poster child for "too much fun being good in a dark world" |

---

## Appendix A: "Paint It, Black" Across Contexts — How One Song Maps the Entire Spectrum

"Paint It, Black" (The Rolling Stones, 1966) is the single best teaching example for the dark revelry spectrum. The *same song* — same notes, same lyrics, same structure — shifts position on the spectrum depending entirely on *who is performing it and in what context*. This demonstrates a critical insight for the MusicComposer SDK: a dark template (harmonic structure, energy profile, tension curve) can produce music that lands anywhere on the spectrum depending on *performance choices* and *narrative context*.

### The Original: Grief, Not Villainy

The Rolling Stones' original is **not** dark revelry. Described as "rock's most nihilistic hit to date," the narrator wants everything painted black because they've lost someone and the color has drained from their world. The sitar adds exotic unease, the driving rhythm adds manic energy, but the emotional posture is *consumed by suffering*, not *celebrating darkness*.

However, the *sonic signature* — minor key + high energy + unstable intensity — is ambiguous enough to support completely different readings when recontextualized. The musical raw material is the same raw material dark revelry is built from.

### The Recontextualizations

| Version / Context | Year | Spectrum Position | Why |
|---|---|---|---|
| **Rolling Stones original** | 1966 | Neither (grief) | Narrator consumed by darkness, not celebrating it |
| **Full Metal Jacket** (Kubrick) | 1987 | Battle Junkie | Raw kinetic energy of combat, soldiers as machines |
| **The Devil's Advocate** (film) | 1997 | Charismatic Maniac | Al Pacino's Satan savoring his manipulation of humanity |
| **Twisted Metal: Black** (PS2, Track 1) | 2001 | Charismatic Maniac | Opening music for a cast of serial killers, cannibals, and maniacs "who want their dark desires fulfilled." Villain Song Wiki lists this usage. The "I want it painted black" lyric is reframed from grief to *embracing darkness as identity* |
| **Ciara cover** (The Last Witch Hunter) | 2015 | Line Rider | "Syrupy, mesmerizing slow-burner" — seductive atmospheric control for a protagonist who savors the shadow side. Ciara called it "a sound she'd always wanted to play with" that "pushes the edge and the limit." Transforms manic original energy into deliberate, inviting darkness |
| **Westworld** (HBO piano cover) | 2016 | Amused Duality | Instrumental reimagining for a show about discovering your reality is a performance. The "everything is a construct" revelation |
| **Taboo** (BBC, main theme) | 2017 | Line Rider | Tom Hardy's morally grey protagonist navigating 1800s London — "riding the line" between hero and villain |

### The Implication for Assisted Authoring

This case study demonstrates that the dark revelry *template* (harmonic structure, energy profile, tension curve) is a substrate, not a destination. The same structural DNA produces grief (original), battle junkie (Full Metal Jacket), charismatic maniac (Twisted Metal: Black), line rider (Ciara/Last Witch Hunter), or amused duality (Westworld) depending on:

1. **Production style**: Manic sitar-rock (original) vs. slow-burn R&B (Ciara) vs. solo piano (Westworld)
2. **Dynamic profile**: Relentless drive (original) vs. theatrical slow-build (Ciara) vs. contemplative (Westworld)
3. **Performance persona**: Grief-stricken narrator vs. action hero vs. self-aware host
4. **Narrative context**: Personal loss vs. supernatural combat vs. existential discovery

The MusicComposer SDK's assisted authoring should communicate this to composers: "This template gives you the structural DNA of darkness + energy. Where it lands on the spectrum — grief, villainy, heroic darkness, or the line between — depends on the performance and production choices you make on top of it." The template is the scaffold; the character is the composer's decision.

---

## Appendix B: The Vampire Chronicles Gap — Why the Archetype Predated Its Music by Two Decades

Anne Rice's Vampire Chronicles (1976–2003) created the foundational dark revelry *character archetype* — Lestat de Lioncourt, a vampire who "relishes his vampire nature," "loves being a vampire," and becomes a rock star to publicly celebrate what he is. He is simultaneously the Charismatic Maniac (genuinely dangerous, finds immortality joyful) and the Performative Villain (consciously performing his dangerous glamour for an audience). When he temporarily becomes human again, he realizes "how much he actually loves being a vampire and how he would never return back to the mortal coil." Rice's inspiration for Lestat's rock star persona was Jim Morrison — charisma, danger, and artistic self-expression as a single identity.

Yet neither film adaptation produced music that matched the character's dark revelry energy. This gap is historically instructive: it reveals why the aesthetic couldn't crystallize as a musical genre until two decades later.

### Interview with the Vampire (1994): Louis's Music, Not Lestat's

Elliot Goldenthal's Oscar-nominated score is beautiful — full orchestral with boys' choir, glass armonica, hammered dulcimer, Handel excerpts. But it is pure **gothic atmospheric** music. It captures the melancholy, the romance, the centuries of elegant suffering. It is Louis's music: the vampire who *hates* being a vampire. The score has no swagger, no glee, no "I love being this." It sounds like being a vampire is a beautiful tragedy.

Goldenthal scored for the film's emotional center (Louis's torment, Brad Pitt staring through rain-streaked windows) rather than its most magnetic character (Lestat's joy, Tom Cruise gleefully biting someone and laughing about it). The musical language available in 1994 film scoring had no template for "gleeful immortal predator" — only for "tragic immortal beauty."

### Queen of the Damned (2002): Nu Metal, Not Vampire Glamour

Jonathan Davis of Korn produced the soundtrack with vocals from Wayne Static (Static-X), David Draiman (Disturbed), Chester Bennington (Linkin Park), Marilyn Manson, and Jay Gordon (Orgy). The result is nu metal, gothic rock, and industrial — the **battle junkie** side of darkness. It sounds like *fighting* vampires, not like *being* a glamorous vampire who loves every second of it. The aggression, distortion, and raw angst channel torment and hunger, not Lestat's charismatic joy.

Aaliyah, who played Queen Akasha, never recorded for the soundtrack — plans for a duet with Davis were lost to her death in August 2001. Given that her portrayal of Akasha was pure seductive dark sovereignty, her vocal contribution might have been the one element that tilted the soundtrack toward dark revelry. We will never know.

### Why the 1990s–2000s Couldn't Produce Dark Revelry

The era's cultural gravity pulled everything dark toward *suffering*:

| Cultural Force | What It Produced | Why It Blocked Dark Revelry |
|---|---|---|
| **Goth rock / darkwave** | Atmospheric, melancholic darkness (Bauhaus, Sisters of Mercy, The Cure) | Darkness as *suffering*. Vampires as tragic, not gleeful |
| **Nu metal emergence** | Aggressive, angst-driven darkness (Korn, Linkin Park, Disturbed) | Darkness as *rage*. The pain was the point |
| **Post-grunge malaise** | Brooding introspection (Bush, Creed, Silverchair) | Darkness as *weight*, not energy |
| **Rice's own narrative tone** | "Self-tormentors who struggled with alienation, loneliness, and the human condition" | Even Lestat, who loves being a vampire, was surrounded by narrative framing that treated immortality as philosophical tragedy |
| **Donnie Darko era** | Covers like Gary Jules' "Mad World" — stripping energy from dark songs to produce pure sadness | The era's instinct was to take dark + energetic material and make it *more sad*, not *more fun* |

The entire cultural apparatus around vampires in the 1990s was built on the **gothic** reading — darkness as elegance, beauty, and *melancholy*. Rice's enormous influence on the goth community was precisely in this register: "melancholic elegance, the juxtaposition of beauty and decay, the embrace of the outsider." Lestat-as-rock-star was dark revelry trying to emerge from a gothic cultural framework — and the framework won.

### What Changed

Dark revelry as a musical aesthetic required a specific cultural convergence that didn't exist until the 2010s–2020s:

| Enabling Factor | When It Arrived | What It Contributed |
|---|---|---|
| Disney Renaissance villain songs reaching adulthood nostalgia | ~2010s | A generation that grew up *loving* villain songs and wanting more — "Hellfire," "Be Prepared," "Friends on the Other Side" were formative musical experiences |
| Electro-swing revival | ~2012–2015 | The production vocabulary for "vintage glamour + modern energy" — the jazz-meets-electronic fusion that gives dark revelry its sonic signature |
| Hazbin Hotel and the animation villain music community | 2019+ | The crystallization moment — Alastor's "Insane" proved that gleeful villain music could become a massive cultural phenomenon |
| TikTok "villain era" phenomenon | 2021+ | Mass cultural framing of villainy as positive identity — "entering my reputation era" normalized the aesthetic |
| Anime with gleeful villain protagonists reaching Western audiences | 2020s | Tanya, Noel, Pencilgon, and others demonstrated the archetype across media, creating demand for matching music |

### What the Vampire Chronicles DID Contribute

Even though the music missed, Rice's literary work seeded the archetype that would eventually find its soundtrack:

1. **The charismatic immortal who loves what they are** — Lestat established that a dark being could be the protagonist precisely *because* they enjoy their nature, not despite it. Before Lestat, vampires in fiction were either monsters or tragic figures. After Lestat, they could be *fun*.
2. **Vampire glamour as aesthetic vocabulary** — Rice's influence on goth fashion ("Victorian and Edwardian clothing, capes, lace-up boots, cravat shirts") created the *visual* language that dark revelry would later inherit. The aesthetic of sophisticated darkness — not crude, not aggressive, but *glamorous* — traces directly to these novels.
3. **The rock star vampire** — the literal concept of a dark being who becomes a *performer* to celebrate their nature publicly. This is the Performative Villain archetype in its purest form, decades before the term existed. Lestat didn't just enjoy being a vampire privately — he went on stage and *sang about it to millions*.
4. **The audience as co-conspirators** — Rice's narrative voice (especially in The Vampire Lestat, told from Lestat's own perspective) pioneered the reader-as-villain's-confidant relationship that dark revelry music creates with listeners. The reader doesn't judge Lestat; they inhabit his perspective and share his delight.

### The Implication

Lestat is the archetype that *still hasn't gotten the soundtrack he deserves*. If "Insane" by Black Gryph0n captured Alastor's energy — a 1920s radio host serial killer having the time of his afterlife — the same compositional approach applied to Lestat would produce the definitive vampire dark revelry track: electro-swing meets glam rock, vintage glamour fused with modern rock star energy, minor key with massive positive energy, theatrical and character-driven, and above all — **the villain is happy**.

The MusicComposer SDK's dark revelry style is, in a sense, the tool that would finally let a composer write Lestat's real theme.

---

## Open Questions

### Q1: Procedural Dark Revelry Generation
Should the MusicStoryteller be able to generate dark revelry compositions procedurally (without human authoring) for runtime use? Use case: a villain NPC's theme music generated on the fly from their personality traits. This would require the GOAP planner to handle the high-tension/positive-valence target robustly. The MusicComposer workbench (human-assisted) is the primary path; procedural generation is an extension.

### Q2: Dynamic Style Blending at Archetype Boundaries
When a character sits between sub-types (a scheming virtuoso who occasionally goes manic), should the style system support real-time blending between variant profiles? The EmotionalState interpolation already supports this, but the harmonic preferences (mode distribution, swing factor) would need smooth transitions.

### Q3: Vocal Prosody Modeling
Emotional Contagion is critical to dark revelry (the villain's delight transmits through vocal performance). Can the style system encode prosody targets (pitch contour, dynamic emphasis, vibrato characteristics) that guide vocal performance or synthesized vocal lines toward the "confident, theatrical" quality the aesthetic requires?

### Q4: Counterpoint Across the Dark Revelry / Battle Junkie Boundary
The Imagine Dragons connection and the Line Rider archetype suggest a bridge zone between the two aesthetics. Can a template be designed where the verse is dark revelry (scheming, theatrical) and the chorus is battle junkie (driving, aggressive), with the MusicComposer workbench validating counterpoint compatibility across the style transition? The "Paint It, Black" case study (Appendix A) suggests this is not only possible but is already how real songs work — the same harmonic substrate supports both positions depending on production choices.

### Q5: Integration with Actor ABML Behaviors
When a god-actor or NPC with musical capabilities (a bard, a siren, a cult leader) performs dark revelry music in-game, should the ABML behavior system be able to select the dark revelry style and sub-type variant based on the character's personality traits? This would connect `${personality.aggression}`, `${personality.theatricality}`, and `${personality.sadism}` variables to style selection.

### Q6: Cover Reinterpretation as a Compositional Technique
The "Paint It, Black" case study demonstrates that *reinterpretation* (same structure, different performance/production) can shift a piece across the entire spectrum. Should the MusicComposer SDK explicitly support a "reinterpret" workflow — taking an existing template and generating production/performance variant suggestions that shift it toward a target archetype? This would be distinct from the counterpoint workflow (creating new music against a template) and more akin to an arrangement/production assistant.

---

*This document defines the dark revelry compositional objective. No implementation exists yet; MusicTheory and MusicStoryteller SDKs provide the foundation primitives it builds on. The MusicComposer SDK (COUNTERPOINT-COMPOSER-SDK.md) is the primary delivery vehicle for the assisted authoring features described here.*
