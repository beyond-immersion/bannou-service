# Why Does Bannou Generate Music Procedurally Instead of Using Audio Files?

> **Short Answer**: Because music in Arcadia is a game system, not ambiance. NPCs compose it, players collect it, areas theme it, and the Collection service gates which tracks are available. Pre-recorded audio files can't participate in the content flywheel or respond to world state. Procedural music can.

---

## The Normal Way Games Do Music

Most games handle music by hiring a composer, recording tracks, and playing them based on triggers: enter a dungeon, play the dungeon theme; enter combat, crossfade to the combat theme; visit a town, play the town theme. This is well-understood, reliable, and produces beautiful results.

It also means music is **finite, static, and disconnected from gameplay systems**. The 50th time you hear the dungeon theme, you stop hearing it. The music never changes based on what happened in the world. It cannot participate in economies, collections, or progression. It is a layer of audio sitting on top of the game, not a system embedded within it.

---

## Music as a Game System

In Arcadia, music is not ambiance -- it is a dimension of the simulation:

### NPCs Compose Music

Bard characters, using the Music service's GOAP-based composition system, generate pieces through a narrative-driven process. The `MusicStoryteller` SDK plans emotional arcs (tension, resolution, melancholy, triumph), and the `MusicTheory` SDK realizes those arcs as actual harmonic progressions, melodies, and voicings following formal music theory rules.

This means an NPC bard in a tavern is not playing a pre-recorded track. They are generating a composition influenced by their personality, their current emotional state, and the narrative context. A bard who witnessed a battle might compose a piece with more tension and dissonance than one who spent the day trading peacefully.

### Players Collect Music

The Collection service (L2) manages music libraries alongside other collectible content types (bestiaries, scene archives, recipe books). Players unlock tracks by experiencing them -- hearing a bard perform, discovering a music box in a dungeon, witnessing a significant event with an associated composition.

This creates a progression system around music. A player's collection reflects their unique journey through the world. Two players have different music libraries because they lived different lives.

### Areas Theme Music

The Collection service provides dynamic track selection based on unlocked tracks and area theme configurations. Different locations influence what music is generated and what tracks are available. A forest region might favor pentatonic scales and pastoral textures. A war-torn city might favor minor keys and martial rhythms.

This means the musical character of a location emerges from the intersection of NPC composers, area themes, and what has been unlocked -- not from a level designer assigning a track.

---

## Why This Requires a Service

The Music service is pure computation -- it takes parameters (key, tempo, emotional arc, seed) and produces structured output (MIDI-JSON representation of a composition). It has no external service dependencies. It does not call other services or subscribe to events.

So why is it a service and not a library function?

### Deterministic Caching

When seeded, the Music service is deterministic: the same seed and parameters always produce the same composition. This enables Redis caching. A composition generated once can be served to every client that requests it without recomputation.

This matters at scale. If 10,000 NPCs in a city are all generating ambient music, many of them will share parameters (same key, same tempo, same emotional profile). Caching means the computational cost is proportional to the number of unique compositions, not the number of requests.

### Client-Server Separation

The Music service outputs MIDI-JSON, not audio. The server decides WHAT to play (harmonic content, melody, rhythm). The client decides HOW to play it (instrument samples, audio rendering, spatial mixing). This separation means:

- The server never transmits audio data over the network. MIDI-JSON is orders of magnitude smaller than audio.
- Different clients can render the same composition with different instrument sets based on their platform, quality settings, or artistic preferences.
- The composition can be stored, transmitted, and cached as lightweight data.

### Decoupled from the Collection Pipeline

If music generation were embedded in the Collection service, the Collection service would need music theory dependencies. If it were embedded in the Actor service, every NPC would need composition capabilities even if they are not bards. As a standalone service, music generation is available to any consumer: Collection for track management, Actor for NPC composition, Event Brain for cinematic scoring, Puppetmaster for scenario ambiance.

---

## GOAP as the Universal Planner

This is one of Arcadia's design principles: GOAP (Goal-Oriented Action Planning) is the universal planner used across NPC behavior, narrative generation, combat choreography, and music composition.

For music, this means composition is structured as goal-seeking behavior. The `MusicStoryteller` SDK defines a desired emotional trajectory (start contemplative, build tension, resolve triumphantly) as a goal state. The GOAP planner searches the action space of available musical techniques (modulation, rhythmic intensification, harmonic tension, melodic development) to find a path from the current state to the goal.

This is architecturally significant because improvements to the GOAP planner benefit music composition alongside every other system that uses GOAP. A better A* heuristic makes NPCs smarter AND compositions more sophisticated.

---

## The Content Flywheel Connection

Pre-recorded music cannot participate in the content flywheel. Procedural music can.

When a legendary bard NPC dies and their archive is compressed, their most notable compositions can be preserved as seeds and parameters. A future narrative seed might involve "a young musician discovers the lost compositions of the great bard Aldric" -- and those compositions actually exist as reproducible MIDI-JSON because the seeds are stored in the archive.

This is a small example of the flywheel's power: a system that seems peripheral (music generation) becomes generative content because it produces structured, replayable, storable data rather than opaque audio blobs.

---

## What About Licensed Music or Composer-Authored Tracks?

Nothing prevents Arcadia from also using pre-recorded audio for specific moments -- menu themes, cinematic set pieces, marketing trailers. The Music service handles the procedural layer. Traditional audio pipelines handle the authored layer. They coexist.

The point is not that all music must be procedural. The point is that the game's living, reactive, collectible music system cannot work with static files alone. The Music service provides the dynamic layer that makes music a game system rather than a soundtrack.
