# Character Communication - Lexicon-Shaped Social Interaction

> **Version**: 1.0
> **Status**: Aspirational (no implementation yet)
> **Location**: New `lexicon` Chat room type, `${social.*}` Variable Provider Factory
> **Related**: [Chat Deep Dive](../plugins/CHAT.md), [Lexicon Deep Dive](../plugins/LEXICON.md), [Hearsay Deep Dive](../plugins/HEARSAY.md), [Disposition Deep Dive](../plugins/DISPOSITION.md), [Collection Deep Dive](../plugins/COLLECTION.md), [Behavior System](./BEHAVIOR-SYSTEM.md), [Story System](./STORY-SYSTEM.md)
> **Dependencies**: lib-chat (L1, implemented), lib-collection (L2, implemented), lib-lexicon (L4, aspirational), lib-hearsay (L4, aspirational), lib-disposition (L4, aspirational)

Character Communication is the social interaction layer for Arcadia's living world. NPCs communicate using structured Lexicon entry combinations rather than free text, creating a universal protocol that both NPCs and players can understand, that discovery levels gate progressively, and that Hearsay distorts as it propagates through social networks.

---

## Table of Contents

1. [Core Insight](#1-core-insight)
2. [Why This Matters](#2-why-this-matters)
3. [Architecture](#3-architecture)
4. [The Lexicon Room Type](#4-the-lexicon-room-type)
5. [Message Structure and Grammar](#5-message-structure-and-grammar)
6. [Discovery-Gated Vocabulary](#6-discovery-gated-vocabulary)
7. [NPC Native Understanding](#7-npc-native-understanding)
8. [The Social Variable Provider](#8-the-social-variable-provider)
9. [Hearsay Integration](#9-hearsay-integration)
10. [Disposition Integration](#10-disposition-integration)
11. [ABML Social Behavior Templates](#11-abml-social-behavior-templates)
12. [Player Experience](#12-player-experience)
13. [Services Involved](#13-services-involved)
14. [Implementation Phases](#14-implementation-phases)
15. [Open Design Questions](#15-open-design-questions)

---

## 1. Core Insight

**NPCs communicate in the same ontological building blocks they think in.**

Lexicon entries are structured representations of concepts: wolves, danger, locations, emotions, behaviors. The Actor runtime already operates on Lexicon-derived knowledge via `${lexicon.*}` variables. A message composed of Lexicon entries is not "natural language the NPC must parse" -- it IS structured cognition, directly consumable by the GOAP planner.

A received message like `[DIREWOLF] + [DANGER] + [NORTH_GATE]` decomposes into concepts the NPC already has in its cognitive state. No NLP needed. No "NPC parses natural language" problem. The message IS structured cognition.

This is metaphysically grounded. Logos are "pure information particles, the words that define what things are." Lexicon entries ARE logos. Communication via Lexicon entries is literally speaking in the fundamental language of reality.

---

## 2. Why This Matters

North Star #1 is "Living Game Worlds" -- NPCs that "pursue their own aspirations, form relationships, run businesses, participate in politics, and generate emergent stories." Without social behavior coordination, NPCs are individually intelligent but socially inert. They can make decisions but cannot interact with each other in the casual, everyday ways that make a world feel alive.

The existing infrastructure provides:
- **Individual cognition**: Actor + ABML + GOAP + Variable Provider Factory
- **Emotional depth**: Character-Personality, Disposition (drives and feelings)
- **Memory**: Character-Encounter (memorable interactions, sentiment)
- **Relationships**: Relationship (bonds, family trees, alliances)
- **World knowledge**: Lexicon (structured concept ontology)
- **Information spread**: Hearsay (belief propagation with distortion)
- **Real-time channels**: Chat (typed message rooms with validation)

What is missing is the **coordination layer** that connects these into social behavior: a shared communication protocol, a social perception channel, daily routine behaviors, and need broadcasting. This guide describes that coordination layer.

---

## 3. Architecture

### The Communication Stack

```
                    PLAYER UX LAYER
┌───────────────────────────────────────────────────────────────┐
│                    Guardian Spirit (Agency)                     │
│  Early: See emotional tones ([HAPPY], [FEAR])                 │
│  Mid:   See full Lexicon combinations character is thinking   │
│  Late:  Compose Lexicon messages as "spirit nudges"           │
└───────────────────────────┬───────────────────────────────────┘
                            │
                    MESSAGE TRANSPORT LAYER
┌───────────────────────────────────────────────────────────────┐
│               Chat Service (L1) - Lexicon Room Type            │
│                                                                │
│  Validates: every element is a known Lexicon entry code       │
│  Validates: combination follows grammar rules                  │
│  Validates: sender has discovery level for each concept        │
│  Stores:   ephemeral (Redis TTL) or persistent (MySQL)        │
│  Delivers: client events to room participants                  │
│  Publishes: service events (metadata only, no content)         │
└───────────────────────────┬───────────────────────────────────┘
                            │
                    COGNITION LAYER
┌───────────────────────────────────────────────────────────────┐
│            Actor Runtime (L2) - Social Perception              │
│                                                                │
│  ${social.*} variable provider reads recent messages          │
│  Messages decompose into GOAP-consumable concept tuples       │
│  Cognition pipeline processes social perceptions               │
│  ABML social flows compose and send Lexicon messages          │
└───────────────────────────┬───────────────────────────────────┘
                            │
                    KNOWLEDGE LAYER
┌───────────────────────────────────────────────────────────────┐
│  Lexicon (L4)     - What concepts exist, traits, associations │
│  Collection (L2)  - What this character has discovered         │
│  Hearsay (L4)     - What this character believes (may be wrong)│
│  Disposition (L4) - How this character feels about things      │
└───────────────────────────────────────────────────────────────┘
```

### Execution Model: Game Server Batches, Bannou Stores

A critical architectural point: NPCs do not individually call Bannou APIs in real-time. The **game server** (Stride) runs the spatial and social simulation, then **batches** results to Bannou for persistence, validation, and downstream consumption.

```
┌─────────────────────────────────────────────────────────────────┐
│                    GAME SERVER (Stride)                           │
│                                                                   │
│  Runs NPC simulation tick:                                       │
│  • Actor runtime produces social intents (ABML outputs)          │
│  • Game server knows who is near whom (spatial authority)        │
│  • Game server aggregates: "these 30 NPCs at the market had     │
│    these 12 conversations this tick"                              │
│                                                                   │
│  Batches results to Bannou:                                       │
│  • Chat: SendMessageBatch (already exists) for social messages   │
│  • Hearsay: bulk belief recording for propagated information     │
│  • Collection: bulk discovery advancement for teaching events     │
│                                                                   │
│  NPCs have no WebSocket connections, no per-session queues,      │
│  no individual RabbitMQ subscriptions. They are server-side       │
│  entities managed by the game server.                             │
└─────────────────────────────────────────────────────────────────┘
```

This means:
- **Scale is a batching problem, not an event problem.** 100K NPCs don't generate 100K individual API calls. The game server aggregates interactions per tick/region and sends bulk requests.
- **Bannou APIs need bulk variants.** Chat already has `SendMessageBatch`. Hearsay will need bulk belief recording. Collection will need bulk discovery advancement. These are standard batch endpoints, not architectural changes.
- **The social variable provider reads from Chat/Hearsay state**, not from live event streams. It queries recent messages and beliefs, which Bannou has already persisted from the game server's batch submissions.
- **Player-visible conversations** (where a player is in the same location as chatting NPCs) flow through Chat's normal client event delivery to the player's WebSocket session. This is the only real-time path.

### Data Flow: NPC Social Interaction (Batched)

```
1. Actor runtime (per NPC) produces social intents via ABML:
   NPC Guard: intent=WARN, elements=[direwolf, danger, north_gate]
   NPC Merchant: intent=TRADE_OFFER, elements=[iron_ore, gold_coin]
   NPC Elder: intent=TEACH, elements=[direwolf, pack_hunter, noise_averse]

2. Game server aggregates intents for this tick at North Gate:
   3 social messages to send in the "location-northgate-social" room

3. Game server calls Chat SendMessageBatch:
   POST /chat/message/send-batch
   {
     roomId: "location-northgate-social",
     messages: [
       { senderId: "guard-01", customPayload: { intent: "warn", ... } },
       { senderId: "merchant-05", customPayload: { intent: "trade_offer", ... } },
       { senderId: "elder-02", customPayload: { intent: "teach", ... } }
     ]
   }

4. Chat validates each message against Lexicon's room type rules,
   stores valid messages, delivers client events to any players present.

5. Game server also calls Hearsay bulk belief recording for
   any belief propagation that occurred this tick (NPCs who heard
   the warning and formed beliefs about direwolves at north gate).

6. On subsequent ticks, receiving NPCs' social variable providers
   read recent messages from Chat and beliefs from Hearsay,
   feeding into ABML cognition for response decisions.
```

### Data Flow: Information Becomes Belief

```
NPC A warns NPC B about direwolves at north gate
        │
        ▼
NPC B receives structured message: [WARN] + [DIREWOLF] + [NORTH_GATE]
        │
        ├─ Chat records the message in the room
        │
        ├─ Social variable provider delivers to B's cognition
        │
        ├─ B's Hearsay forms a location belief:
        │    domain: "location"
        │    subjectId: north_gate_location_id
        │    claimCode: "danger_direwolf"
        │    confidence: 0.5 (social_contact channel)
        │    valence: -0.8 (threatening)
        │
        └─ B meets NPC C later (encounter-triggered propagation):
           B's belief about north gate propagates to C
           confidence degrades: 0.5 * distortion → 0.3
           C receives: "I heard the north gate might be dangerous"
           C's version: [NORTH_GATE] + [DANGER] (less specific, lost [DIREWOLF])
```

---

## 4. The Lexicon Room Type

### Registration

Chat supports custom room types registered per game service. The `lexicon` room type is registered **by lib-lexicon** on startup -- it is not a built-in Chat type but a custom type that Lexicon creates via Chat's `RegisterRoomType` API. This keeps the dependency direction correct: Lexicon (L4) depends on Chat (L1), not the other way around. Lexicon owns the validation logic for its room type.

```yaml
# Room Type Definition
code: "lexicon"
scope: "{gameServiceId}"       # Per-game, not global
messageFormat: "lexicon"        # Custom format identifier
validatorConfig:
  grammarMode: "structured"     # Enforce grammar rules
  maxElements: 12               # Maximum Lexicon entries per message
  requireIntent: true           # Every message must have an intent
  validateDiscovery: true       # Sender must have discovery level
persistenceMode: "ephemeral"    # Social chatter uses Redis TTL
maxParticipants: 50             # Location-scoped social rooms
rateLimitPerMinute: 30          # NPCs don't spam
```

### Room Lifecycle

Lexicon chat rooms are **location-scoped**: each location (or sub-location) has a social room that characters automatically join when present and leave when they depart. This mirrors how real social spaces work -- you hear conversations happening around you.

```
Character arrives at North Gate
    → JoinRoom("location-northgate-social")
    → Permission state: "in_room" set
    → Receives recent message history (if persistent)

Character leaves North Gate
    → LeaveRoom("location-northgate-social")
    → Permission state cleared
    → No longer receives messages from this room
```

Contract-governed rooms are also valid: a diplomatic negotiation room governed by a treaty Contract, where the room locks on breach and archives on fulfillment.

### Room Types vs Sentiments vs Intents

Chat's existing `sentiment` room type handles emotional expressions as a predefined set of sentiment categories (joy, anger, fear, etc.). The `lexicon` room type is fundamentally different:

| Aspect | `sentiment` Room Type | `lexicon` Room Type |
|--------|----------------------|---------------------|
| **Message format** | Predefined sentiment category | Arbitrary Lexicon entry combinations |
| **Expressiveness** | Fixed emotional vocabulary | Open-ended, grows with Lexicon |
| **Validation** | Category must be in allowed set | Each element must be a valid Lexicon code |
| **Discovery gating** | None (universal emotions) | Per-element (character must know each concept) |
| **NPC consumption** | Simple emotion signal | Full structured cognition input |
| **Use case** | Quick emotional reactions | Complex thoughts, warnings, requests |

Both room types serve social communication. Sentiments are the quick emotional layer ("I'm happy", "I'm scared"). Lexicon messages are the cognitive layer ("Direwolves are dangerous at the north gate").

**Intents** are a category of Lexicon entries that describe communication acts. They are the "verb" of a Lexicon message:

```
Lexicon Category Tree:
  communication (root)
  ├── intent
  │   ├── inform       # Sharing knowledge
  │   ├── warn         # Alert about danger
  │   ├── request      # Ask for something
  │   ├── question     # Seek information
  │   ├── greet        # Social acknowledgment
  │   ├── threaten     # Hostile warning
  │   ├── compliment   # Positive social signal
  │   ├── trade_offer  # Economic proposition
  │   ├── teach        # Knowledge transfer
  │   └── command      # Authority-based directive
  └── emotion          # Emotional modifiers (overlap with sentiment)
      ├── urgency_high
      ├── urgency_low
      ├── certainty_high
      ├── certainty_low
      └── ...
```

Intents are Lexicon entries themselves -- meta-entries that describe what the communication is doing. This means they are also discovery-gated: a young child may only know `greet` and `question`, while a diplomat knows `negotiate`, `threaten_subtly`, and `propose_alliance`.

---

## 5. Message Structure and Grammar

### Basic Grammar

Lexicon messages follow a minimal structured grammar for expressiveness while remaining machine-parseable:

```
Message = Intent + Subject* + Modifier* + Context*

Intent:   A communication Lexicon entry (WARN, INFORM, REQUEST, etc.)
Subject:  The primary topic(s) -- what is being discussed
Modifier: Qualifiers -- intensity, temporality, certainty
Context:  Situational elements -- location, relationship, condition
```

### Examples

```yaml
# Simple warning
intent: warn
elements: [direwolf, north_gate]
# "Watch out -- direwolves at the north gate"

# Qualified warning with temporal context
intent: warn
elements: [direwolf, pack_hunter, north_gate, urgency_high, certainty_high]
# "Definitely a pack of direwolves hunting near the north gate right now"

# Trade offer
intent: trade_offer
elements: [iron_ore, quantity_large, gold_coin, quantity_small]
# "I have a lot of iron ore, looking for a little gold"

# Teaching
intent: teach
elements: [direwolf, pack_hunter, noise_averse]
# "Let me tell you about direwolves -- they hunt in packs but are scared of loud noises"

# Question
intent: question
elements: [witch_of_the_wolves, north_gate]
# "Have you seen the Witch of the Wolves near the north gate?"

# Greeting with emotional modifier
intent: greet
elements: [warmth, familiarity]
# A warm, familiar greeting (NPC recognizes the other)

# Charades-like expression from limited vocabulary
intent: inform
elements: [large_canine, danger, fear]
# "Big dog-like thing! Dangerous! I'm scared!"
# (NPC doesn't know the word "direwolf" yet -- discovery level too low)
```

### Vocabulary Depth Creates Expression Variety

The same concept expressed at different discovery levels:

| Discovery Level | Available Vocabulary | Message |
|-----------------|---------------------|---------|
| 1 (basic) | `animal`, `danger`, `fear` | `[WARN] + [ANIMAL] + [DANGER]` |
| 2 (familiar) | `canine`, `predator`, `pack` | `[WARN] + [CANINE] + [PREDATOR] + [NORTH_GATE]` |
| 3 (knowledgeable) | `direwolf`, `pack_hunter`, `noise_averse` | `[WARN] + [DIREWOLF] + [PACK_HUNTER] + [NORTH_GATE]` |
| 5 (expert) | `witch_of_the_wolves`, `summoned_by` | `[WARN] + [DIREWOLF] + [SUMMONED_BY] + [WITCH_OF_THE_WOLVES]` |

Higher discovery yields more precise communication. An expert can convey the full picture in one message; a novice needs multiple imprecise messages to approximate the same meaning.

---

## 6. Discovery-Gated Vocabulary

### Collection Controls the Lexicon

Collection (L2) tracks what each character has discovered. Lexicon (L4) defines what exists. The intersection determines available vocabulary:

```
Lexicon (ground truth):
  "direwolf" entry exists with 5 discovery levels
    tier 1: name, category (canine), basic traits (predator, fur)
    tier 2: behavior traits (pack_hunter, nocturnal)
    tier 3: strategies (make_loud_noise, climb_to_escape)
    tier 4: associations (witch_of_the_wolves → 0.3 strength)
    tier 5: weaknesses, rare traits

Collection (per character):
  Farmer Kael: direwolf discovery level = 2
  Scholar Mira: direwolf discovery level = 4
  Child Aldric: no direwolf entry (level 0)

Communication vocabulary:
  Kael can say: [DIREWOLF] + [PACK_HUNTER] + [NOCTURNAL]
  Mira can say: [DIREWOLF] + [WITCH_OF_THE_WOLVES] + [NOISE_AVERSE]
  Aldric can say: [ANIMAL] + [DANGER] (via category-level knowledge)
```

### Validation Flow

Lexicon owns the validation logic for its room type. When an NPC composes a Lexicon message, Lexicon's validation (invoked via Chat's `ValidatorConfig` delegation) checks:

1. **Entry existence**: Every element code must be a registered Lexicon entry
2. **Discovery gating**: Sender's Collection must contain each entry at sufficient discovery level
3. **Grammar compliance**: Intent required, element count within limits
4. **Category-level fallback**: If a character lacks a specific entry but knows the parent category, they can use the category code instead (expressing imprecision naturally)

Messages that fail validation are silently dropped -- the NPC literally "can't express" a concept they don't know. This is the behavioral equivalent of not having the words.

**Hierarchy note**: Chat (L1) provides the transport and room infrastructure. Lexicon (L4) registers the room type and provides the validation rules. The dependency flows downward: Lexicon depends on Chat, not the reverse.

### Cross-Species Communication

Different species may share Lexicon entries but weight them differently through Ethology (species-level behavioral archetypes). A wolf-species NPC communicates with more `[PACK]` and `[TERRITORY]` concepts. The same Lexicon, different usage patterns -- like real cultural differences in emphasis.

Ethology's `${nature.*}` variables bias ABML message composition flows:

```yaml
# Wolf-species NPC composing a greeting
- cond:
    if: "${nature.social_structure == 'pack' && nature.territoriality > 0.7}"
    then:
      # Emphasize pack and territory concepts in communication
      intent: greet
      elements: [pack_bond, territory_safe, welcome]
    else:
      # Default greeting
      intent: greet
      elements: [warmth]
```

---

## 7. NPC Native Understanding

### Messages Are Already Structured Cognition

The critical architectural advantage: Lexicon messages are composed of the same concepts NPCs already think in. When NPC B receives `[WARN] + [DIREWOLF] + [NORTH_GATE]`, it decomposes directly into:

1. **Intent recognition**: `warn` → classify as threat information, increase processing urgency
2. **Subject lookup**: `direwolf` → query `${lexicon.direwolf.*}` for known traits and strategies
3. **Context binding**: `north_gate` → bind to known location, compute distance, assess relevance
4. **Goal evaluation**: Do I care about this? Am I near north gate? Do I have strategies for direwolves?
5. **GOAP replanning**: If relevant, adjust plans (avoid area, prepare defense, warn others)

This is not "NPC parses natural language." The message format IS the cognitive representation. Zero translation cost.

### Understanding Depth Varies by Receiver

NPC B's response to the same message varies based on their own knowledge:

```yaml
# NPC B has direwolf discovery level 4 (expert)
# Understands fully: direwolves are pack hunters, noise-averse, associated with the Witch
# Response: tactical assessment, prepare specific counter-strategies

# NPC C has direwolf discovery level 1 (basic)
# Understands partially: "direwolf" = some kind of dangerous animal
# Response: general caution, avoid north gate

# NPC D has no direwolf knowledge
# Partial understanding: recognizes [WARN] intent and [NORTH_GATE] context
# Does not know what [DIREWOLF] means
# Response: vague unease about north gate, might ask for clarification
# Potential: [QUESTION] + [DIREWOLF] ("What is a direwolf?")
```

This creates natural information asymmetry. Experts communicate efficiently; novices must ask questions. Knowledge gaps drive social interaction.

---

## 8. The Social Variable Provider

### `${social.*}` Namespace

A new `IVariableProviderFactory` implementation that reads recent Chat messages from Lexicon rooms the NPC participates in, decoded into GOAP-consumable concept tuples. This is the "social perception" channel.

```
Provider: SocialProviderFactory
Service: lib-lexicon or standalone thin provider (L4)
Namespace: ${social.*}
Source: Chat API (recent messages from joined Lexicon rooms)
Cache: Short TTL (30-60 seconds), invalidated on new message events
```

### Available Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${social.message_count}` | int | Recent messages in joined rooms (sliding window) |
| `${social.recent.N.intent}` | string | Intent of Nth most recent message |
| `${social.recent.N.elements}` | string[] | Lexicon codes in Nth message |
| `${social.recent.N.sender_id}` | Guid | Who sent it |
| `${social.recent.N.sender_sentiment}` | float | My sentiment toward sender (from encounters) |
| `${social.recent.N.urgency}` | string | Message urgency level |
| `${social.has_warnings}` | bool | Any recent WARN intents |
| `${social.has_requests}` | bool | Any recent REQUEST intents directed at me |
| `${social.has_questions}` | bool | Any recent QUESTION intents |
| `${social.topic_frequency.CODE}` | int | How often a Lexicon code appears in recent messages |
| `${social.ambient_mood}` | float | Aggregate valence of recent messages (-1.0 to +1.0) |
| `${social.conversation_active}` | bool | Am I currently in an active exchange? |
| `${social.unanswered_questions}` | int | Questions directed at me I haven't responded to |

### ABML Usage

```yaml
flows:
  check_social_environment:
    # Respond to warnings in my area
    - cond:
        if: "${social.has_warnings && social.recent.0.intent == 'warn'}"
        then:
          # Check if I know what they're warning about
          - cond:
              if: "${lexicon.${social.recent.0.elements[1]}.known}"
              then:
                - call: assess_and_respond_to_warning
              else:
                # Don't know this concept -- ask about it
                - call: ask_for_clarification
                  with:
                    unknown_concept: "${social.recent.0.elements[1]}"

  respond_to_question:
    # Someone asked me something
    - cond:
        if: "${social.has_questions && social.unanswered_questions > 0}"
        then:
          - set:
              question_topic: "${social.recent.0.elements}"
          # Can I answer? Check my discovery level
          - cond:
              if: "${lexicon.${question_topic[0]}.discovery_level > 2}"
              then:
                - call: compose_teaching_response
              else:
                - call: express_ignorance
```

---

## 9. Hearsay Integration

### Structured Messages Propagate as Structured Beliefs

When Hearsay propagates beliefs through social networks via the telephone-game distortion mechanic, it operates on structured Lexicon combinations rather than free text. This means distortion is concept-level, not character-level:

```
Original message (NPC A, discovery 4):
  [WARN] + [DIREWOLF] + [PACK_HUNTER] + [NORTH_GATE] + [WITCH_OF_THE_WOLVES]

After 1 hop (NPC B tells NPC C, distortion applied):
  [WARN] + [DIREWOLF] + [NORTH_GATE]
  (lost: PACK_HUNTER detail, WITCH_OF_THE_WOLVES association)
  Confidence: 0.5 → 0.3

After 2 hops (NPC C tells NPC D):
  [WARN] + [CANINE] + [DANGER]
  (lost: specific DIREWOLF identity, specific NORTH_GATE location)
  Confidence: 0.3 → 0.15

After 3 hops (NPC D tells NPC E):
  [DANGER] + [ANIMAL]
  (reduced to vague threat, lost intent specificity)
  Confidence: 0.15 → 0.08
```

The concept simplifies and loses precision, which is exactly how rumors work. Specific concepts degrade to their parent categories (direwolf → canine → animal). Context is lost before subject. Associations are lost first.

### Distortion Rules

Hearsay's distortion mechanics operate on message elements:

1. **Association loss**: Elements connected by weak associations drop first
2. **Specificity decay**: Specific entries degrade to parent category codes
3. **Context stripping**: Location and modifier elements drop before subject elements
4. **Intent preservation**: The communication intent (WARN, INFORM) is the most stable element
5. **Confidence reduction**: Each hop multiplies confidence by `(1.0 - distortionFactor)`
6. **Personality mediation**: NPCs with high conscientiousness distort less; high openness transmits more elements

### Belief Formation from Messages

When an NPC receives a Lexicon message, Hearsay creates beliefs based on the source channel:

| Source | Channel | Confidence Range |
|--------|---------|-----------------|
| Direct observation (NPC saw the direwolf) | `direct_observation` | 0.85-1.0 |
| Trusted friend tells them | `trusted_contact` | 0.5-0.7 |
| Acquaintance mentions it | `social_contact` | 0.3-0.5 |
| Overheard in a crowded room | `rumor` | 0.1-0.3 |
| Cultural common knowledge | `cultural_osmosis` | medium |

The `sender_sentiment` from encounters determines whether the source is trusted, social, or unreliable.

---

## 10. Disposition Integration

### Drives Create Communication Needs

Disposition's drive system provides intrinsic motivations that generate social communication:

| Drive | Communication Behavior |
|-------|----------------------|
| `protect_family` (urgency 0.8) | Warn family members about dangers, check on their safety |
| `master_craft` (urgency 0.6) | Seek teaching from experts, share knowledge with apprentices |
| `gain_wealth` (urgency 0.7) | Initiate trade offers, negotiate prices, advertise goods |
| `earn_respect` (urgency 0.5) | Share accomplishments, offer help to others |
| `find_love` (urgency 0.4) | Compliment, greet warmly, seek proximity |
| `seek_justice` (urgency 0.9) | Accuse wrongdoers, rally support, demand action |

### Feelings Modulate Communication Style

Disposition's feeling axes toward specific entities affect how messages are composed:

```yaml
# NPC composing a message to someone they distrust
- cond:
    if: "${disposition.character.${target}.trust < 0.3}"
    then:
      # Guarded communication -- fewer details, no vulnerabilities shared
      compose_message:
        intent: inform
        elements: [danger, north_gate]
        # Deliberately omits detailed knowledge (withholds information)

# NPC composing a message to someone they admire
- cond:
    if: "${disposition.character.${target}.admiration > 0.7}"
    then:
      # Full disclosure -- shares everything they know
      compose_message:
        intent: teach
        elements: [direwolf, pack_hunter, noise_averse, witch_of_the_wolves]
```

### Need Broadcasting

An NPC in need (hungry, lonely, scared) cannot currently be discovered by nearby helpful NPCs. Disposition's drive system solves this through communication:

```yaml
# NPC with high frustration on a drive broadcasts need
flows:
  broadcast_need:
    - cond:
        if: "${disposition.drive.strongest.frustration > 0.7
              && disposition.drive.strongest.category == 'survival'}"
        then:
          # Broadcast need to social room
          compose_message:
            intent: request
            elements: ["${disposition.drive.strongest.code}", urgency_high]
            # e.g., [REQUEST] + [FOOD] + [URGENCY_HIGH]

  # Another NPC's ABML flow detects the request
  respond_to_need:
    - cond:
        if: "${social.has_requests
              && disposition.character.${social.recent.0.sender_id}.warmth > 0.3
              && personality.agreeableness > 0.5}"
        then:
          # Helpful NPC responds to the need
          - call: offer_assistance
```

This creates emergent social dynamics: helpful NPCs respond to broadcast needs, building relationships through mutual aid. Selfish NPCs ignore requests, creating reputation consequences over time.

---

## 11. ABML Social Behavior Templates

### Daily Routine Template

NPCs need time-aware behavior patterns for social communication:

```yaml
# behaviors/social/daily-routine-social.yaml
metadata:
  id: daily-routine-social
  type: character_agent
  requires_providers: [social, disposition, lexicon, personality]

flows:
  morning_greetings:
    - schedule:
        when: "${world.time.period == 'morning'}"
        call: greet_nearby_acquaintances

  greet_nearby_acquaintances:
    # Find NPCs I have positive sentiment toward
    - cond:
        if: "${social.conversation_active == false
              && personality.extraversion > 0.4}"
        then:
          # Check if any known NPCs are in my social room
          - foreach:
              collection: "${social.room_participants}"
              as: participant
              do:
                - cond:
                    if: "${encounters.has_met.${participant.id}
                          && disposition.character.${participant.id}.warmth > 0.3}"
                    then:
                      compose_message:
                        intent: greet
                        elements: [warmth, familiarity]
                        target: "${participant.id}"

  share_daily_observations:
    - schedule:
        when: "${world.time.period == 'afternoon'}"
        call: share_knowledge_socially

  share_knowledge_socially:
    # NPCs with high openness share interesting things they've learned
    - cond:
        if: "${personality.openness > 0.6
              && social.message_count < 5}"
        then:
          # Share a recently discovered concept
          - set:
              topic: "${lexicon.most_recent_discovery}"
          - cond:
              if: "${topic != null}"
              then:
                compose_message:
                  intent: inform
                  elements: ["${topic.code}", "${topic.most_interesting_trait}"]
```

### Visit Friend Template

```yaml
# behaviors/social/visit-friend.yaml
metadata:
  id: visit-friend
  type: character_agent
  requires_providers: [social, disposition, encounters, personality]

flows:
  consider_visiting_friend:
    - schedule:
        when: "${disposition.drive.has_drive.find_companionship
                && disposition.drive.find_companionship.frustration > 0.5}"

    # Find a friend I haven't seen recently
    - set:
        candidate: "${encounters.longest_unseen_positive_contact}"
    - cond:
        if: "${candidate != null
              && disposition.character.${candidate.id}.warmth > 0.5}"
        then:
          # GOAP goal: be at friend's location
          - add_goal:
              name: "visit_friend"
              condition: "distance_to.${candidate.id} < 5"
              priority: "${disposition.drive.find_companionship.urgency * 40}"

  on_arrival_at_friend:
    # Social interaction when reaching friend's location
    - cond:
        if: "${goal.visit_friend.satisfied}"
        then:
          compose_message:
            intent: greet
            elements: [warmth, familiarity, "${candidate.shared_interest}"]
          # Shared interest = Lexicon concept both characters know
          # e.g., two blacksmiths greet with [GREET] + [WARMTH] + [IRON_WORKING]
```

### Market Interaction Template

```yaml
# behaviors/social/market-trade.yaml
metadata:
  id: market-trade
  type: character_agent
  requires_providers: [social, disposition, lexicon, seed]

flows:
  browse_market:
    - schedule:
        when: "${world.time.period == 'morning'
                && disposition.drive.has_drive.gain_wealth}"

    # At market location, check for trade opportunities
    - cond:
        if: "${social.has_trade_offers}"
        then:
          - call: evaluate_trade_offer
        else:
          # Advertise own goods
          - cond:
              if: "${inventory.tradeable_count > 0}"
              then:
                compose_message:
                  intent: trade_offer
                  elements: ["${inventory.best_tradeable.lexicon_code}",
                             quantity_indicator,
                             "${inventory.desired_resource.lexicon_code}"]
                  # e.g., [TRADE_OFFER] + [IRON_ORE] + [QUANTITY_LARGE] + [GOLD_COIN]
```

### Festival / Event Attendance Template

```yaml
# behaviors/social/attend-festival.yaml
metadata:
  id: attend-festival
  type: character_agent
  requires_providers: [social, disposition, personality]

flows:
  respond_to_festival_announcement:
    # Regional watcher announces festival via broadcast message
    - cond:
        if: "${social.topic_frequency.festival > 0
              && personality.extraversion > 0.3
              && disposition.drive.find_companionship.intensity > 0.3}"
        then:
          # GOAP goal: attend festival location
          - add_goal:
              name: "attend_festival"
              condition: "at_location.${social.recent_festival_location}"
              priority: 50

  at_festival:
    # Social mingling behavior at festival
    - cond:
        if: "${goal.attend_festival.satisfied}"
        then:
          # Greet people, share knowledge, enjoy social atmosphere
          - call: social_mingle
            with:
              mood: "${disposition.emotional_volatility < 0.3 ? 'relaxed' : 'excitable'}"
              topics: "${lexicon.recently_discovered_codes}"
```

---

## 12. Player Experience

### Progressive Social Agency (from PLAYER-VISION.md)

The guardian spirit's social domain expands through accumulated experience:

| Stage | Social UX | Communication Capability |
|-------|-----------|------------------------|
| **Early** (new spirit) | See emotional tones only | Observe `[HAPPY]`, `[FEAR]`, `[ANGER]` emotional indicators |
| **Developing** | See intent categories | Observe that character is warning, greeting, requesting |
| **Intermediate** | See full Lexicon combinations | Read the complete messages characters exchange |
| **Advanced** | Compose spirit nudges | Suggest Lexicon combinations as "spirit influence" |
| **Expert** | Direct social orchestration | Compose complex multi-element messages through the character |

The character is ALWAYS communicating autonomously. The player's progressive agency reveals and eventually modulates what was always happening.

### Spirit Nudges as Lexicon Messages

When a player's social agency reaches the "compose" stage, spirit nudges for social interaction take the form of Lexicon message composition:

```
Player UX presents:
  Available intents: [GREET] [WARN] [REQUEST] [QUESTION]
  Available concepts: (filtered by character's Collection)

Player composes: [WARN] + [DIREWOLF] + [VILLAGE]

Spirit nudge delivered to character's Actor:
  perception_type: "spirit_social_nudge"
  suggested_message: {intent: "warn", elements: ["direwolf", "village"]}

Character's ABML evaluates:
  - Does this align with my personality? (compliance check via Disposition)
  - Do I know these concepts? (discovery check via Collection)
  - Is this appropriate right now? (context check via social environment)

  If compliance > threshold:
    → Character sends the message (possibly modified by personality)
  If compliance < threshold:
    → Character resists, sends modified version, or ignores
```

A peaceful character nudged to send `[THREATEN]` may refuse or soften it to `[WARN]`. An honest character nudged to `[DECEIVE]` may feel resentment toward the spirit. The guardian spirit relationship (trust, resentment, defiance) mechanically gates social manipulation.

---

## 13. Services Involved

### Existing Services (Implemented)

| Service | Layer | Role in Character Communication |
|---------|-------|---------------------------------|
| **Chat** | L1 | Message transport, room management, validation, delivery |
| **Collection** | L2 | Discovery-level gating of vocabulary per character |
| **Actor** | L2 | Cognition pipeline, ABML execution, perception processing |
| **Character-Encounter** | L4 | Relationship sentiment, recent interaction data |
| **Character-Personality** | L4 | Communication style modulation (extraversion, openness) |
| **Relationship** | L2 | Social graph for propagation paths, friend identification |

### Aspirational Services (Pre-Implementation)

| Service | Layer | Role in Character Communication |
|---------|-------|---------------------------------|
| **Lexicon** | L4 | Concept ontology, entry validation, trait/association data |
| **Hearsay** | L4 | Belief formation from messages, propagation with distortion |
| **Disposition** | L4 | Drive-motivated communication, feeling-modulated style |
| **Ethology** | L4 | Species-level communication patterns and preferences |
| **Agency** | L4 | Progressive social UX manifest for guardian spirit |
| **Worldstate** | L2 | Time-of-day triggers for daily routine behaviors |
| **Environment** | L4 | Weather/season context for social behavior |

### New Components (Required)

| Component | Location | Description |
|-----------|----------|-------------|
| **Lexicon Chat Room Type** | lib-chat registration | Custom room type with Lexicon validation |
| **Social Variable Provider** | lib-lexicon or standalone L4 | `${social.*}` namespace from Chat messages |
| **ABML Social Templates** | behavior content (not a service) | Authored behavior documents for social flows |
| **Location-Room Binding** | lib-chat or game server | Auto-join/leave rooms based on location presence |

---

## 14. Implementation Phases

### Phase 0: Prerequisites

These services must exist before character communication:

| Prerequisite | Status | Notes |
|--------------|--------|-------|
| lib-chat | Implemented (90%) | All 28 endpoints, custom room types supported |
| lib-collection | Implemented (78%) | Discovery advancement, ICollectionUnlockListener |
| lib-actor | Implemented (65%) | Variable Provider Factory, ABML execution |
| lib-lexicon | **Not implemented** | Core ontology needed first |
| lib-worldstate | **Not implemented** | Time-of-day triggers for daily routines |

**Lexicon is the critical path.** Without a concept ontology, there is no vocabulary for messages. Lexicon Phase 1 (core ontology) and Phase 2 (traits and strategies) are the minimum for communication.

### Phase 1: Lexicon Room Type and Basic Messaging

**Goal**: NPCs can send and receive structured Lexicon messages.

1. Register `lexicon` room type in Chat with validation rules
2. Implement message validation against Lexicon entry codes
3. Implement Collection discovery-level validation for senders
4. Create location-scoped social rooms (manual creation initially)
5. ABML `compose_message` action handler that calls Chat API

**Verification**: An NPC can compose `[GREET] + [WARMTH]` and another NPC receives it.

### Phase 2: Social Variable Provider

**Goal**: NPCs can perceive and react to social messages in their environment.

1. Implement `SocialProviderFactory` as `IVariableProviderFactory`
2. Read recent messages from joined Lexicon rooms via Chat API
3. Decode messages into `${social.*}` variables
4. Short TTL cache with invalidation on `chat.message.sent` events
5. Wire into Actor runtime alongside existing providers

**Verification**: An NPC receiving `[WARN] + [DIREWOLF]` adjusts its GOAP plan to avoid danger.

### Phase 3: ABML Social Behavior Templates

**Goal**: NPCs exhibit basic social behaviors using Lexicon communication.

1. Author `daily-routine-social` template (greetings, observations)
2. Author `respond-to-warning` template (threat assessment, relay)
3. Author `ask-question` template (knowledge gaps trigger questions)
4. Author `respond-to-question` template (knowledge sharing / teaching)
5. Location-room auto-join/leave bindings

**Verification**: NPCs greet each other in the morning, warn about dangers, ask and answer questions.

### Phase 4: Hearsay Belief Formation

**Goal**: Messages create beliefs that propagate through social networks.

1. Wire Chat message reception into Hearsay belief creation
2. Implement confidence assignment based on encounter sentiment
3. Implement distortion rules for Lexicon element propagation
4. Implement encounter-triggered belief propagation

**Verification**: NPC A warns NPC B about direwolves. NPC B later tells NPC C a degraded version.

### Phase 5: Disposition-Driven Social Behavior

**Goal**: NPCs seek social interaction based on internal drives and feelings.

1. Wire Disposition drives into social ABML templates (need broadcasting)
2. Wire Disposition feelings into message composition (trust modulates disclosure)
3. Author `visit-friend` template driven by companionship drive
4. Author `market-trade` template driven by wealth drive
5. Author `broadcast-need` template driven by high-frustration survival drives

**Verification**: Hungry NPCs broadcast need for food. Friendly NPCs respond. Lonely NPCs visit friends.

### Phase 6: Player Social Agency

**Goal**: Guardian spirit can progressively perceive and influence social communication.

1. Wire spirit social domain capability into Agency UX manifest
2. Implement progressive message visibility (emotional tones → full messages)
3. Implement spirit nudge input for Lexicon message composition
4. Wire compliance check through Disposition guardian feelings

**Verification**: Player can see NPC conversations, eventually suggest messages, character may resist.

---

## 15. Open Design Questions

### Grammar Complexity

How complex should the Lexicon message grammar be? The current proposal is minimal (intent + elements), but richer grammar enables more nuanced communication:

- **Option A: Flat combination** -- `[DIREWOLF] + [DANGER]` (simpler validation, charades-like)
- **Option B: Structured roles** -- `intent: WARN, subject: DIREWOLF, context: NORTH_GATE` (richer, more machine-parseable)
- **Option C: Sentence-like** -- `[I] + [FEAR] + [DIREWOLF] + [AT] + [NORTH_GATE]` (most expressive, hardest to validate)

Current recommendation: **Option B** for NPC-to-NPC (structured, precise), with Option A available for early-stage player spirit nudges (simpler UX).

### Room Scope and Lifecycle

Should Lexicon rooms be:
- **Location-scoped** (auto-created per location, characters auto-join/leave)
- **Relationship-scoped** (private channels between bonded characters)
- **Event-scoped** (temporary rooms for festivals, markets, meetings)
- **All of the above** (different room subtypes for different social contexts)

Current recommendation: Location-scoped as the primary pattern, with relationship-scoped for close bonds (like the existing `text` room type but using Lexicon format for NPC understanding).

### Social Variable Provider Placement

Where should the `${social.*}` provider live?
- **In lib-lexicon** (alongside `${lexicon.*}`, since it decodes Lexicon messages)
- **Standalone thin L4 service** (social-provider or lib-social, dedicated to social perception)
- **In lib-chat** (since it reads Chat data, but this would make L1 provide L4 variables)

Current recommendation: In **lib-lexicon** as a secondary provider factory. Lexicon already needs Chat data for knowledge transfer mechanics, and the social provider reads Chat messages decoded through the Lexicon ontology.

### Sentiment Rooms vs Lexicon Rooms for Emotions

Chat's built-in `sentiment` room type already handles emotional expressions. Should emotional communication use sentiment rooms (quick, universal) or Lexicon rooms (structured, discovery-gated)?

Current recommendation: **Both coexist.** Sentiment rooms are the "body language" layer -- universal, immediate, not gated by discovery. Lexicon rooms are the "verbal" layer -- structured, progressive, knowledge-dependent. An NPC might simultaneously express `[FEAR]` sentiment (visible to everyone, no vocabulary needed) and compose `[WARN] + [DIREWOLF] + [NORTH_GATE]` in the Lexicon room (only understandable by NPCs who know those concepts).

### Teaching Bandwidth and Knowledge Transfer

When an NPC teaches another through Lexicon messages, what determines how much knowledge transfers?

The Lexicon deep dive describes three bandwidth levels:
1. **Expensive**: Direct demonstration (full knowledge transfer, requires physical co-presence)
2. **Medium**: Structured verbal teaching via Lexicon messages (transmits specific Hearsay beliefs)
3. **Cheap**: Inference from associations (hypothesized traits via Lexicon association chains)

The open question: Should a single `[TEACH] + [DIREWOLF] + [PACK_HUNTER]` message grant the recipient the `pack_hunter` trait knowledge for direwolves? Or should teaching require sustained interaction (multiple messages, a teaching "session")?

Current recommendation: Teaching quality scales with:
- Teacher's discovery level (mastery enables efficient transmission)
- Relationship closeness (trusted contacts learn faster)
- Recipient personality (high openness = more receptive)
- Number of teaching interactions (single message = Hearsay belief; sustained mentorship = Collection advancement)

---

*This document describes the aspirational character communication system for Arcadia. No implementation exists yet. The architectural design composes existing Bannou primitives (Chat, Collection, Actor, Variable Provider Factory) with aspirational services (Lexicon, Hearsay, Disposition) to create emergent social behavior without a dedicated social service.*
