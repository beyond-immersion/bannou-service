# Compression Gameplay Patterns: Visual Guide

> Quick reference charts for explaining what character compression enables.
> See `COMPRESSION-GAMEPLAY-PATTERNS.md` for full design details.

---

## Chart 1: What Happens When a Character Dies

```
                         CHARACTER DIES
                              │
                              ▼
        ┌─────────────────────────────────────────┐
        │         COMPRESSION ARCHIVE             │
        │                                         │
        │  ┌─────────────┐  ┌─────────────────┐  │
        │  │ Personality │  │    Backstory    │  │
        │  │ BRAVE: 0.8  │  │ GOAL: find bro  │  │
        │  │ LOYAL: 0.9  │  │ TRAUMA: war     │  │
        │  │ AGGRO: 0.3  │  │ ORIGIN: north   │  │
        │  └─────────────┘  └─────────────────┘  │
        │                                         │
        │  ┌─────────────┐  ┌─────────────────┐  │
        │  │  Memories   │  │   Encounters    │  │
        │  │ 47 meetings │  │ +Elena (wife)   │  │
        │  │ with Aldric │  │ -Lord Vex (foe) │  │
        │  └─────────────┘  └─────────────────┘  │
        │                                         │
        │  ┌─────────────────────────────────┐   │
        │  │     Combat Preferences          │   │
        │  │  Style: DEFENSIVE               │   │
        │  │  Protect Allies: TRUE           │   │
        │  └─────────────────────────────────┘   │
        └─────────────────────────────────────────┘
                              │
                              │ NOT deleted...
                              │ TRANSFORMED into:
                              ▼
     ┌────────────┬────────────┬────────────┬────────────┐
     │            │            │            │            │
     ▼            ▼            ▼            ▼            ▼
 ┌───────┐  ┌─────────┐  ┌─────────┐  ┌────────┐  ┌──────────┐
 │ GHOST │  │ QUESTS  │  │ LEGACY  │  │MEMORIES│  │   NPC    │
 │haunts │  │ hooks   │  │children │  │in NPCs │  │TOMBGUARD │
 │ grave │  │generated│  │inherit  │  │who knew│  │ guards   │
 └───────┘  └─────────┘  └─────────┘  └────────┘  └──────────┘
```

---

## Chart 2: The Resurrection Spectrum

```
  DATA FIDELITY
       │
  100% ┼─────────────────────────────────────────────────────
       │  ┌──────────────┐
       │  │ TRUE REVIVAL │  Full decompression
       │  │ "Back from   │  All data restored
   90% │  │  the dead"   │  Same person
       │  └──────────────┘
       │
       │           ┌──────────────┐
   70% │           │   REVENANT   │  Full mind, warped purpose
       │           │ "Unfinished  │  Obsession amplified
       │           │  business"   │  Seeks resolution
       │           └──────────────┘
       │
       │                    ┌──────────────┐
   50% │                    │    GHOST     │  Memories + emotions
       │                    │  "Haunting   │  No physical form
       │                    │   presence"  │  Location-bound
       │                    └──────────────┘
       │
       │                             ┌──────────────┐
   20% │                             │    ZOMBIE    │  Combat instincts
       │                             │  "Walking    │  Fragmented memory
       │                             │   corpse"    │  Recognizes family
       │                             └──────────────┘
       │
       │                                      ┌──────────────┐
    5% │                                      │    CLONE     │  Template only
       │                                      │  "New entity │  No memories
       │                                      │   same mold" │  Fresh start
    0% ┼──────────────────────────────────────┴──────────────┴───────────
       │
       └──────────────────────────────────────────────────────► ENTITY TYPE
```

---

## Chart 3: Quest Generation from Archives

```
                    COMPRESSED CHARACTER
                    ┌─────────────────────┐
                    │   "Aldric the Bold" │
                    │   Died seeking his  │
                    │   lost brother      │
                    └─────────────────────┘
                              │
          ┌───────────────────┼───────────────────┐
          │                   │                   │
          ▼                   ▼                   ▼
   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
   │  UNFINISHED  │   │   ENEMIES    │   │   SECRETS    │
   │    GOALS     │   │   & GRUDGES  │   │   & LEGACY   │
   └──────────────┘   └──────────────┘   └──────────────┘
          │                   │                   │
          ▼                   ▼                   ▼
   ╔══════════════╗   ╔══════════════╗   ╔══════════════╗
   ║    QUEST:    ║   ║    QUEST:    ║   ║    QUEST:    ║
   ║  "Continue   ║   ║   "Justice   ║   ║  "What He    ║
   ║  the Search" ║   ║    for the   ║   ║    Knew"     ║
   ║              ║   ║    Fallen"   ║   ║              ║
   ║ Find Aldric's║   ║ Lord Vex     ║   ║ Aldric knew  ║
   ║ lost brother ║   ║ betrayed him ║   ║ artifact loc ║
   ╚══════════════╝   ╚══════════════╝   ╚══════════════╝
          │                   │                   │
          └───────────────────┼───────────────────┘
                              │
                              ▼
                    ╔═══════════════════╗
                    ║  QUEST CHAIN:     ║
                    ║  "A Father's      ║
                    ║   Legacy"         ║
                    ║                   ║
                    ║ Help his widow    ║
                    ║ Train his orphans ║
                    ║ Clear his name    ║
                    ╚═══════════════════╝
```

---

## Chart 4: The Tombguard Pattern

```
    WHILE ALIVE                           AFTER COMPRESSION
    ──────────────────────────────────────────────────────────

    ┌─────────┐         ┌─────────┐
    │ SETSUNA │ ♥ ♥ ♥ ♥ │WEZAEMON │       Living NPC with
    │ (lover) │◄───────►│(warrior)│       strong bond
    └─────────┘         └─────────┘
         │                   │
         │   Setsuna dies    │
         ▼                   │
    ┌─────────────┐          │
    │  COMPRESSED │          │
    │   ARCHIVE   │          │
    │             │          │
    │ • memories  │          │
    │ • love bond │──────────┘
    │ • location  │          │
    └─────────────┘          │
                             │
                             │ Bond transforms him
                             ▼
                    ┌─────────────────┐
                    │    WEZAEMON     │
                    │ "THE TOMBGUARD" │
                    │                 │
                    │ • Guards grave  │
                    │ • Boss enemy    │
                    │ • Quest giver   │
                    │ • Tragic figure │
                    └─────────────────┘
                             │
                             ▼
                    ╔═════════════════╗
                    ║ QUEST UNLOCKED: ║
                    ║ "From the       ║
                    ║  Living World,  ║
                    ║  With Love"     ║
                    ╚═════════════════╝

    The dead character never acts...
    but defines the living character's entire existence.
```

---

## Chart 5: Live Compression (Without Deletion)

```
    OLD WAY: Query 5 services for NPC context
    ─────────────────────────────────────────────────

    Actor Brain needs character data:

    ┌─────────┐
    │  ACTOR  │──────┬──────┬──────┬──────┬──────┐
    │  BRAIN  │      │      │      │      │      │
    └─────────┘      │      │      │      │      │
                     ▼      ▼      ▼      ▼      ▼
               ┌─────┐┌─────┐┌─────┐┌─────┐┌─────┐
               │CHAR ││PERS-││HIST-││ENCO-││RELA-│
               │ACTER││ONAL-││ORY  ││UNTER││TION-│
               │     ││ITY  ││     ││     ││SHIP │
               └──┬──┘└──┬──┘└──┬──┘└──┬──┘└──┬──┘
                  │      │      │      │      │
                  │ 50ms │ 50ms │ 50ms │100ms │ 50ms
                  │      │      │      │      │
                  └──────┴──────┴──────┴──────┘
                              │
                         TOTAL: ~300ms
                         5 service calls


    NEW WAY: Single compressed snapshot
    ─────────────────────────────────────────────────

    ┌─────────┐         ┌──────────────────────────┐
    │  ACTOR  │────────►│   LIVE COMPRESSION       │
    │  BRAIN  │         │                          │
    └─────────┘         │  ┌────────────────────┐  │
         │              │  │ Complete character │  │
         │              │  │ context in ~1-2KB  │  │
         │              │  │                    │  │
         │              │  │ • Personality      │  │
         │ ONE call     │  │ • Backstory        │  │
         │ ~30ms        │  │ • Key encounters   │  │
         │              │  │ • Combat style     │  │
         │              │  │ • AI-ready summary │  │
         │              │  └────────────────────┘  │
         │              └──────────────────────────┘
         │
         ▼
    10x faster NPC initialization!
```

---

## Chart 6: Cross-Entity Compression

```
    The pattern works for MORE than just characters:

    ┌─────────────────────────────────────────────────────────────┐
    │                    COMPRESSIBLE ENTITIES                    │
    └─────────────────────────────────────────────────────────────┘

    ┌─────────────┐      ┌─────────────┐      ┌─────────────┐
    │  CHARACTER  │      │    SCENE    │      │    REALM    │
    │             │      │             │      │             │
    │ personality │      │ node tree   │      │ locations   │
    │ memories    │      │ assets      │      │ history     │
    │ encounters  │      │ history     │      │ cultures    │
    │ backstory   │      │ creators    │      │ species     │
    └──────┬──────┘      └──────┬──────┘      └──────┬──────┘
           │                    │                    │
           ▼                    ▼                    ▼
    ┌─────────────┐      ┌─────────────┐      ┌─────────────┐
    │   BECOMES   │      │   BECOMES   │      │   BECOMES   │
    │             │      │             │      │             │
    │ • Ghosts    │      │ • Ruins     │      │ • Lost      │
    │ • Quests    │      │   "what it  │      │   civiliza- │
    │ • Legacies  │      │    used to  │      │   tions     │
    │ • Memories  │      │    be"      │      │             │
    │   in NPCs   │      │             │      │ • Mythology │
    │             │      │ • Time      │      │   "legends  │
    │             │      │   travel    │      │    say..."  │
    │             │      │   mechanics │      │             │
    │             │      │             │      │ • Alternate │
    │             │      │ • Archaeol- │      │   dimen-    │
    │             │      │   ogy       │      │   sions     │
    └─────────────┘      └─────────────┘      └─────────────┘
```

---

## Chart 7: The Big Picture

```
    ┌─────────────────────────────────────────────────────────────────────┐
    │                                                                     │
    │                        TRADITIONAL VIEW                             │
    │                                                                     │
    │         Character Lives ───────────► Character Dies                 │
    │                                            │                        │
    │                                            ▼                        │
    │                                      ┌──────────┐                   │
    │                                      │ DELETED  │                   │
    │                                      │   gone   │                   │
    │                                      │ forever  │                   │
    │                                      └──────────┘                   │
    │                                                                     │
    └─────────────────────────────────────────────────────────────────────┘


    ┌─────────────────────────────────────────────────────────────────────┐
    │                                                                     │
    │                     COMPRESSION VIEW                                │
    │                                                                     │
    │         Character Lives ───────────► Character Dies                 │
    │                                            │                        │
    │                                            ▼                        │
    │                                   ┌────────────────┐                │
    │                                   │   COMPRESSED   │                │
    │                                   │    ARCHIVE     │                │
    │                                   └───────┬────────┘                │
    │                                           │                         │
    │              ┌────────────────────────────┼────────────────────┐    │
    │              │                            │                    │    │
    │              ▼                            ▼                    ▼    │
    │      ┌─────────────┐            ┌─────────────┐       ┌───────────┐│
    │      │   HAUNTS    │            │  GENERATES  │       │ INFLUENCES││
    │      │             │            │             │       │           ││
    │      │ Ghost at    │            │ Quests from │       │ Children  ││
    │      │ grave site  │            │ unfinished  │       │ inherit   ││
    │      │             │            │ business    │       │ traits    ││
    │      │ Zombie with │            │             │       │           ││
    │      │ fragments   │            │ Lore from   │       │ NPCs      ││
    │      │             │            │ secrets     │       │ remember  ││
    │      │ Revenant    │            │             │       │ them      ││
    │      │ seeking     │            │ Reputation  │       │           ││
    │      │ vengeance   │            │ for family  │       │ Tombguards││
    │      │             │            │             │       │ protect   ││
    │      └─────────────┘            └─────────────┘       └───────────┘│
    │                                                                     │
    │                                                                     │
    │            "THE DEAD ARE NEVER TRULY GONE.                          │
    │                  THEY'RE JUST COMPRESSED."                          │
    │                                                                     │
    └─────────────────────────────────────────────────────────────────────┘
```

---

## Chart 8: Data Flow Summary

```
    ┌───────────────────────────────────────────────────────────────┐
    │                    ONE DEAD CHARACTER                         │
    │                                                               │
    │    Personality ─────┐                                         │
    │                     │                                         │
    │    Backstory ───────┤                                         │
    │                     │      ┌────────────────────────┐         │
    │    Encounters ──────┼─────►│  COMPRESSION ARCHIVE   │         │
    │                     │      │     (~2-5 KB JSON)     │         │
    │    History ─────────┤      └───────────┬────────────┘         │
    │                     │                  │                      │
    │    Combat Style ────┘                  │                      │
    │                                        │                      │
    └────────────────────────────────────────┼──────────────────────┘
                                             │
                                             │ CAN BECOME:
                                             │
         ┌───────────────┬──────────────┬────┴────┬──────────────┐
         │               │              │         │              │
         ▼               ▼              ▼         ▼              ▼
    ┌─────────┐    ┌──────────┐   ┌─────────┐ ┌───────┐   ┌──────────┐
    │ 1 GHOST │    │ 3 QUESTS │   │MEMORIES │ │LEGACY │   │TOMBGUARD │
    │         │    │          │   │ IN 10   │ │ FOR 5 │   │   NPC    │
    │ haunts  │    │ revenge  │   │  NPCs   │ │DESCEN-│   │          │
    │location │    │ recovery │   │         │ │ DANTS │   │ guards   │
    │         │    │ closure  │   │"I knew  │ │       │   │  their   │
    │"I still │    │          │   │ them"   │ │inherit│   │  grave   │
    │remember"│    │"Finish   │   │         │ │traits │   │          │
    │         │    │ what they│   │dialogue │ │       │   │ boss     │
    │         │    │ started" │   │triggers │ │dreams │   │ enemy    │
    └─────────┘    └──────────┘   └─────────┘ └───────┘   └──────────┘

    One death = Dozens of gameplay elements
```

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────────────────────┐
│              COMPRESSION: QUICK REFERENCE                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  RESURRECTION TYPES                                             │
│  ─────────────────                                              │
│  Ghost     = Memories + Emotions, no body                       │
│  Zombie    = Body + Instincts, fragmented mind                  │
│  Revenant  = Full mind, single obsessive purpose                │
│  Clone     = Fresh start from template                          │
│                                                                 │
│  QUEST HOOKS                                                    │
│  ───────────                                                    │
│  Unfinished Goal     → "Continue Their Work"                    │
│  Negative Encounter  → "Avenge the Fallen"                      │
│  Secret Knowledge    → "What They Knew"                         │
│  Living Family       → "A Father's Legacy"                      │
│                                                                 │
│  NPC PATTERNS                                                   │
│  ────────────                                                   │
│  Memory Seeding  = New NPCs "remember" dead characters          │
│  Tombguard       = NPC defined by protecting the dead           │
│  Dialogue Hooks  = "That reminds me of [dead character]..."     │
│                                                                 │
│  LEGACY MECHANICS                                               │
│  ────────────────                                               │
│  Personality Inheritance = Children get parent traits           │
│  Ancestral Dreams        = Descendants "remember" ancestors     │
│  Family Reputation       = Dead's deeds affect living's welcome │
│                                                                 │
│  CROSS-ENTITY                                                   │
│  ────────────                                                   │
│  Scene Compression  → Ruins, time travel, archaeology           │
│  Realm Compression  → Lost civilizations, mythology             │
│  Item Compression   → Artifact histories, sentient weapons      │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│  "The dead are never truly gone. They're just compressed."      │
└─────────────────────────────────────────────────────────────────┘
```

---

*Charts for `docs/planning/COMPRESSION-GAMEPLAY-PATTERNS.md`*
