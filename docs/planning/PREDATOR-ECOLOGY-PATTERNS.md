# Predator Ecology Patterns: Behavioral Modeling for Living Worlds

> **Status**: Research Document (ecological analysis for behavioral modeling)
> **Priority**: High (Living Game Worlds -- North Star #1, Emergent Over Authored -- North Star #5)
> **Related**: `docs/plugins/ETHOLOGY.md`, `docs/plugins/ACTOR.md`, `docs/plugins/ENVIRONMENT.md`, `docs/plugins/DISPOSITION.md`, `docs/reference/ORCHESTRATION-PATTERNS.md`
> **Domain**: Species-level predator behavior, interspecific competition, spatial ecology, niche partitioning
> **Sources**: Established wildlife ecology literature (Mech & Boitani 2003, Sunquist & Sunquist 2002, Carbone et al. 1999/2007, Palomares & Caro 1999, Creel & Creel 1996/2002, Durant 1998/2000, Ripple & Beschta 2004/2012, Ritchie & Johnson 2009, Swanson et al. 2014/2016, Gaynor et al. 2018)

---

## Executive Summary

Real-world predator ecosystems are governed by discoverable rules that produce emergent complexity from simple parameters. Multiple carnivorous predator species **routinely coexist** in the same geographic area -- this is the norm in healthy ecosystems, not the exception. The African savanna supports 7+ large predator species; North American temperate forests support 6+. Coexistence is not about tolerance; it is about **niche partitioning along multiple axes simultaneously** (temporal, spatial, prey size, hunting style). These rules translate directly into Ethology service behavioral axes and ABML behavior expressions, producing realistic predator ecosystems from parameter interactions rather than scripted encounters.

The key insight for Arcadia: **predator behavior is the strongest possible demonstration of the "Emergent Over Authored" principle**. A player witnessing a leopard kill, vultures circling, hyenas arriving, the leopard dragging its kill into a tree, and a lion pride stealing the hyenas' next meal -- all emerging from 30 behavioral float values and 6 interaction rules -- is the living world thesis in action.

---

## Part 1: Predator Coexistence Rules

### The Competitive Exclusion Principle

Gause's Law states that two species competing for the exact same niche cannot coexist indefinitely -- one outcompetes the other. The corollary: species that DO coexist **must differ along at least one niche axis**. The more axes of separation, the more stable the coexistence.

**Body size ratio rule** (Hutchinson's Rule): Coexisting predators in the same guild tend to differ in body mass by a factor of ~2-3x, naturally separating their optimal prey size. African large carnivore guild: lion (~190 kg) > leopard (~60 kg) > cheetah (~50 kg) > African wild dog (~25 kg) > black-backed jackal (~8 kg).

### Niche Partitioning Axes

Predators divide shared space along multiple axes simultaneously:

#### Temporal Partitioning (When They Hunt)

| Activity Pattern | Examples | Simulation Parameter |
|-----------------|----------|---------------------|
| **Diurnal** | Cheetahs, raptors, wild dogs | `nocturnality` 0.0-0.2 |
| **Nocturnal** | Leopards, owls, small felids | `nocturnality` 0.7-1.0 |
| **Crepuscular** | Lions, many canids, servals | `crepuscular_bias` 0.6-0.8 |
| **Cathemeral** | Hyenas, wolves, tigers | `temporal_flexibility` 0.5-0.8 |

Critical finding from camera-trap studies: when a dominant predator is active during a given period, **subordinate predators shift their activity away from that period** ("temporal avoidance"). Leopards shift to later night hours in high-lion-density areas. Cheetahs are almost exclusively diurnal as an anti-lion/hyena adaptation. Activity timing also changes with season -- crepuscular predators compress their window in winter.

#### Spatial Partitioning (Where They Hunt)

**Vertical stratification**:
- Canopy: harpy eagles, arboreal snakes, margays
- Mid-story: ocelots, clouded leopards, genets
- Ground surface: wolves, lions, cheetahs
- Subterranean: badgers, ferrets, weasels
- Aquatic/riparian: otters, fishing cats, crocodilians

**Horizontal habitat selection within the same biome**:
- Cheetahs prefer open grassland (need speed and visibility)
- Leopards prefer dense bush, riverine thickets, rocky outcrops (need ambush cover)
- Lions use intermediate habitats
- In mountains: snow leopards occupy 3,500-5,500m; common leopards occupy lower elevations

Same "biome" supports multiple predator species because they select **different microhabitats**.

#### Prey Size Partitioning (What They Eat)

Carbone et al. (1999, 2007) scaling law: predators under ~21 kg primarily eat prey smaller than themselves; predators over ~21 kg primarily eat prey near their own mass or larger.

**African savanna prey partitioning**:

| Predator | Mass (kg) | Primary Prey Size (kg) | Primary Targets |
|----------|-----------|----------------------|-----------------|
| Lion | 150-250 | 100-600 | Buffalo, zebra, wildebeest |
| Spotted hyena | 45-65 | 50-200 | Wildebeest, zebra (flexible) |
| Leopard | 40-90 | 15-80 | Impala, bushbuck, warthog |
| Cheetah | 35-65 | 20-60 | Thomson's gazelle, impala |
| African wild dog | 20-30 | 15-60 | Impala, kudu (cooperative allows larger) |
| Black-backed jackal | 6-13 | 0.5-5 | Hares, rodents, lambs |
| Serval | 8-18 | 0.02-0.2 | Rodents, birds |

Even species of similar body size partition by **hunting style**: cheetahs are cursorial (speed pursuit, open terrain), leopards are ambush (stealth, strength, arboreal caching), wild dogs are endurance pursuit pack hunters.

#### Dietary Flexibility

Predators exist on a specialist-to-generalist spectrum:

| Position | Examples | Flexibility Score |
|----------|----------|------------------|
| Hyper-specialist | Lynx (95%+ snowshoe hare) | 0.0-0.1 |
| Primary specialist | Cheetah (60-80% gazelle) | 0.2-0.4 |
| Flexible generalist | Leopard (90+ prey species) | 0.6-0.8 |
| Hyper-generalist | Bears (omnivorous, seasonal) | 0.8-1.0 |

---

## Part 2: Intraguild Predation & Dominance

### Intraguild Predation (IGP)

The killing of a competing predator by another predator within the same guild. Almost always flows from larger to smaller species. This is one of the most important forces structuring predator communities.

**Concrete examples**:
- Lions kill leopards, cheetahs, wild dogs, and hyena cubs (competitive killing, rarely eat them)
- Wolves kill coyotes -- wolf reintroduction in Yellowstone reduced coyote density by 39%
- Tigers kill leopards and dholes throughout their range
- Coyotes kill foxes -- red fox density inversely correlates with coyote density across North America

**Kleptoparasitism** (theft rather than killing):
- Spotted hyenas steal from cheetahs/wild dogs in ~10-15% of observed kills
- Lions obtain up to 30% of food by stealing from hyenas (Ngorongoro Crater)
- Bald eagles steal fish from ospreys

### The Dominance Hierarchy

**African savanna** (quantified from interspecific encounter studies):

```
Rank 1: LION (male coalition)
  Dominates: everything
  Contest win rate vs hyena clan: ~70%
  Contest win rate vs single hyena: ~99%

Rank 2: SPOTTED HYENA (clan of 5+)
  Dominates: leopard, cheetah, wild dog, jackal
  Contest win rate vs wild dog pack: ~60-70%

Rank 3: LEOPARD (adult)
  Dominates: cheetah, wild dog (single), jackal
  Avoidance strategy: arboreal caching, nocturnal shift

Rank 4: CHEETAH (adult/coalition)
  Kill loss rate to kleptoparasites: ~10-15%
  Strategy: diurnal activity, flight (fastest land animal)

Rank 5: AFRICAN WILD DOG (pack of 10+)
  Kill loss rate to kleptoparasites: ~8-12%
  Compensates with 80% hunt success rate + rapid consumption

Rank 6: BLACK-BACKED JACKAL
  Strategy: scavenging, speed, burrow escape
```

**North American** (documented post-wolf-reintroduction):

```
Rank 1: GRIZZLY BEAR - wins ~85% of carcass contests vs wolf packs
Rank 2: WOLF PACK (6+) - kills coyotes frequently
Rank 3: COUGAR - shifts to rugged terrain to avoid wolf packs
Rank 4: BLACK BEAR - primarily scavenger/omnivore at this trophic level
Rank 5: COYOTE - suppressed by wolves, suppresses foxes
Rank 6: BOBCAT / RED FOX - subordinate to all above
```

### Hierarchy Rules

1. **Body mass is the primary determinant** -- larger species wins >90% of one-on-one encounters
2. **Group size can override body mass** -- a hyena clan of 10+ drives a single lion off a kill; a wolf pack can contest a grizzly (rarely)
3. **Context matters** -- kill site residents are more motivated; a leopard defends a tree-cached kill against a single hyena but abandons a ground kill
4. **The "dear enemy" effect** -- known neighbors receive lower-intensity responses than unknown intruders

### The Landscape of Fear

The mere **presence** of a dominant predator changes subordinate behavior across the landscape, even without direct encounters:

- Cheetahs avoid areas within 2 km of recent lion sightings
- Wild dogs avoid areas within 5 km of lion prides
- Leopards increase nocturnal activity by 20-30% in high-lion areas
- Coyotes avoid areas within 1-3 km of wolf den sites
- Cougars shift to steeper terrain within 2 years of wolf recolonization
- **Orca presence causes great white sharks to abandon entire areas** (documented at Seal Island, South Africa)

---

## Part 3: Mesopredator Release

When an apex predator is removed, medium-sized predators increase in abundance and activity, often with cascading negative effects on their prey.

### The Mechanism

1. Apex predator present -> suppresses mesopredator through IGP, kleptoparasitism, and behavioral avoidance
2. Apex predator removed -> mesopredator freed from suppression -> expands activity in time and space
3. Smaller prey species decline (sometimes catastrophically)

### Documented Examples

| System | Apex Removed | Mesopredator Released | Cascade Effect |
|--------|-------------|----------------------|---------------|
| Yellowstone | Wolves extirpated | Coyote range exploded coast-to-coast | Small mammal and ground-nesting bird decline |
| Yellowstone (reversed) | Wolves reintroduced | Coyote density -39%, red fox rebounded | Pronghorn fawn survival increased |
| Australia | Dingo culled | Feral cat density +5-10x | 20+ native mammal extinctions |
| Eastern North America | Cougar extirpated | Raccoon, opossum, feral cat exploded | Increased nest predation on birds and sea turtles |
| South Africa | Leopard declining | Baboon populations expanding | Crop raiding, smaller primate competition |

### Simulation Rules

- When apex predator density drops below threshold, mesopredator activity multiplier increases proportionally
- The increase has a lag (compressed for game time)
- Cascading effect on small prey populations is visible
- Reintroducing apex predators reverses the effect (also with lag)

**Gameplay implication**: Players who hunt too many wolves find the rabbit population collapsing because foxes overrun the area. A god-actor (Silvanus/Forest) might intervene by spawning a new apex predator to restore balance. This is the content flywheel operating through ecological dynamics.

---

## Part 4: Movement & Territory Patterns

### Territory Strategy Spectrum

| Strategy | Examples | Key Pattern | Territory Size |
|----------|----------|-------------|---------------|
| **Strictly territorial** | Wolves, tigers, leopards | Defended borders, scent marking every 200-400m, buffer zones 2-5 km between neighbors | Species-dependent |
| **Overlapping ranges** | Bears, jaguars | Same areas, temporal avoidance; male grizzly ranges overlap 50-80% | 25-2,000 km² |
| **Nomadic/wandering** | Wild dogs, cheetah females | Vast ranges, follow prey, no defense | 400-2,000 km² |
| **Migratory** | Arctic wolves, Serengeti hyena commuters | Seasonal den-based -> nomadic following herds | 500-1,000+ km seasonal |
| **Central-place forager** | Denning wolves, nesting eagles | Radiate from fixed point, constrained hunting radius (10-30 km) | Derived from radius |
| **Ambush chokepoint** | Crocodiles, water-hole leopards | Tiny territory at critical resource (water crossing, game trail) | 0.01-0.1 km² |

### Territory Size Determinants

Territory size follows a power law with body mass, strongly modified by prey density:

```
territory_size = base_size * (body_mass / reference_mass)^1.2
               * (reference_prey_density / local_prey_density)^0.7
               * pack_modifier(pack_size)
               * terrain_modifier(terrain_type)
               * season_modifier(season)
```

| Predator | Mass (kg) | Typical Territory (km²) |
|----------|-----------|------------------------|
| Weasel | 0.1-0.3 | 0.01-0.1 |
| Red fox | 4-7 | 1-10 |
| Bobcat | 8-13 | 10-50 |
| Coyote | 10-18 | 10-80 |
| Wolf pack | 30-60 | 150-1,500 |
| Cougar | 50-100 | 100-800 |
| Lion pride | 150-250 | 20-400 |
| Tiger | 100-300 | 20-1,000+ |
| Polar bear | 300-700 | 10,000-300,000+ |

### Floater Behavior

Non-territorial individuals (typically young adults dispersing from natal groups) make up **10-25% of saturated populations**:

- Travel 30-70 km/day in straight-line segments (vs 15-25 km/day for territorial animals in loops)
- Suffer 2-5x higher mortality than territory holders
- Fill vacancies within weeks when territory holders die

**Strategies**: straight-line dispersal, satellite/shadowing at territory edges, coalition formation with other floaters, territory takeover.

**Vacancy chain dynamics**: When a territory holder dies, a cascade follows -- nearest floater claims vacancy, floater's marginal position opens, younger floater moves to that position, etc. Creates dynamic "ripple effects" from a single death event.

### Seasonal Territory Shifts

| Season | Effect | Magnitude |
|--------|--------|-----------|
| **Summer/denning** | Range contracts around den/rendezvous sites | 40-60% of winter size |
| **Winter** | Range expands as prey disperses | 1.5-2.5x summer size |
| **Drought** | Predators concentrate at water sources | Territory collapses toward water |
| **Prey migration** | Pursuit predators follow; ambush predators fast/scavenge | Variable |
| **Breeding** | Increased territory defense; boundaries re-harden | Defense intensity +30-50% |

### Territory Intrusion Response Escalation

```
Level 1: PASSIVE MARKING (prevention) -- scent, scratch marks, rubs
Level 2: ACTIVE SIGNALING (warning) -- howling, roaring, increased marking
Level 3: INVESTIGATION (assessment) -- approach, intensive sniffing, identity check
Level 4: PARALLEL PATROL (deterrence) -- move along boundary, posturing
Level 5: PURSUIT (active defense) -- chase intruder, short range
Level 6: COMBAT (last resort) -- direct confrontation, potentially lethal
```

Response intensity scales with: distance from core (closer = more aggressive), intruder group size vs defender group size (wolves and lions assess numerical odds), reproductive state (denning females escalate faster), resource value (near fresh kill = rapid escalation), and familiarity with intruder (known neighbors get reduced response -- the "dear enemy effect").

Lions count roars in playback experiments -- they approach when they outnumber the roars by ~3:1 or better.

---

## Part 5: Real-World Predator Guild Case Studies

### African Savanna (Serengeti-Mara Ecosystem)

7+ large carnivore species coexisting through multi-axis partitioning:

| Species | Social | Activity | Habitat | Prey Focus | Anti-theft Strategy |
|---------|--------|----------|---------|-----------|-------------------|
| **Lion** | Prides 5-15 | Crepuscular/nocturnal | Open woodland, plains edge | Large ungulates 100-600 kg | Dominant -- steals from others |
| **Spotted hyena** | Clans up to 80 | Primarily nocturnal, flexible | Generalist | Medium ungulates 50-200 kg | Numbers, rapid consumption |
| **Leopard** | Solitary | Strongly nocturnal | Dense cover, trees | Medium prey 15-80 kg | **Caches kills in trees** |
| **Cheetah** | Solitary/coalition | Strictly diurnal | Open grassland | Small-medium gazelles 20-60 kg | Speed pursuit, abandons if contested |
| **African wild dog** | Packs 6-20 | Diurnal | Open woodland | Medium ungulates 15-60 kg | 80% hunt success rate, rapid consumption |
| **Black-backed jackal** | Pairs | Crepuscular | Edges, open areas | Small prey 0.5-5 kg | Speed, burrow escape |
| **Serval** | Solitary | Crepuscular/nocturnal | Tall grass, wetland margins | Rodents 0.02-0.2 kg | Completely different prey base |

### North American Temperate Forest (Greater Yellowstone)

The post-wolf-reintroduction trophic cascade (documented 1995-present):

- Wolves reintroduced -> elk behavior changed (avoiding riparian areas) -> riparian vegetation recovered -> beaver returned -> stream morphology changed -> songbird populations increased
- Wolves suppress coyotes -> red fox increases -> small rodent predation patterns shift
- Grizzlies steal wolf kills -> wolves hunt more frequently
- Cougars shift to more rugged terrain, reducing overlap with wolves

### Asian Tropical Forest (Indian Subcontinent)

- Tiger (apex, 180-260 kg): ambush specialist, large prey, kills leopards and dholes
- Leopard (subordinate, 40-80 kg): shifts to smaller prey and more nocturnal/arboreal behavior near tigers; in India near tiger reserves, eats more dogs, livestock, primates
- Dhole/Asian wild dog (pack, 15-20 kg): pack hunter, can occasionally harass leopards through numbers; severely reduced by tigers
- Clouded leopard (arboreal, 12-23 kg): nocturnal canopy specialist; minimal ground-level competition

---

## Part 6: Recommended Ethology Behavioral Axes

### Hunting Strategy Domain (`hunting.*`)

| Axis | Range | Description |
|------|-------|-------------|
| `ambush_preference` | 0.0-1.0 | Pure pursuit (0) vs pure ambush (1) |
| `pursuit_endurance` | 0.0-1.0 | Duration tolerance for active chases |
| `stalking_patience` | 0.0-1.0 | Time invested in stealthy approach before strike |
| `cooperative_hunting` | 0.0-1.0 | Degree of coordinated group hunting |
| `prey_size_ratio` | 0.1-5.0 | Preferred prey mass relative to own mass |
| `prey_size_flexibility` | 0.0-1.0 | Willingness to take non-preferred prey sizes |
| `surplus_killing` | 0.0-1.0 | Propensity to kill beyond immediate need |
| `hunt_success_learning` | 0.0-1.0 | How quickly hunting location preferences update |

### Temporal Domain (`temporal.*`)

| Axis | Range | Description |
|------|-------|-------------|
| `nocturnality` | 0.0-1.0 | Night activity preference baseline |
| `crepuscular_bias` | 0.0-1.0 | Dawn/dusk activity peak |
| `temporal_flexibility` | 0.0-1.0 | How much activity period shifts under competitive pressure |

### Spatial Domain (`spatial.*`)

| Axis | Range | Description |
|------|-------|-------------|
| `territory_fidelity` | 0.0-1.0 | Home range defense intensity |
| `range_size_factor` | 0.5-20.0 | Home range size multiplier |
| `vertical_preference` | 0.0-1.0 | Ground (0) vs arboreal/aerial (1) habitat preference |
| `den_attachment` | 0.0-1.0 | How central a fixed den/lair is to spatial behavior |
| `water_dependency` | 0.0-1.0 | How frequently the predator needs water access |

### Competition Domain (`competition.*`)

| Axis | Range | Description |
|------|-------|-------------|
| `dominance_assertion` | 0.0-1.0 | Willingness to contest resources with other predators |
| `kleptoparasitism_tendency` | 0.0-1.0 | Food theft propensity |
| `conspecific_tolerance` | 0.0-1.0 | Tolerance of same-species non-group individuals |
| `mobbing_susceptibility` | 0.0-1.0 | Deterrence by prey mobbing behavior |
| `competitive_weight` | 0.1-100.0 | Derived: effective body mass for competitive interactions (includes group bonus) |

### Metabolic Domain (`metabolic.*`)

| Axis | Range | Description |
|------|-------|-------------|
| `metabolic_urgency` | 0.0-1.0 | Rate at which hunger drives risk-taking |
| `injury_aversion` | 0.0-1.0 | Baseline risk sensitivity |
| `fasting_tolerance` | 0.0-1.0 | Duration before desperation behaviors activate |
| `scavenging_willingness` | 0.0-1.0 | Carrion feeding propensity |
| `dietary_breadth` | 0.0-1.0 | Range of acceptable food types (1.0 = true omnivore) |

### Sensory Domain (`sensory.*`)

| Axis | Range | Description |
|------|-------|-------------|
| `scent_tracking` | 0.0-1.0 | Ability to follow scent trails |
| `visual_detection_range` | 0.0-1.0 | Long-distance visual detection (high for raptors, vultures) |
| `carrion_detection` | 0.0-1.0 | Ability to detect dead prey at distance |
| `ambush_camouflage` | 0.0-1.0 | Concealment effectiveness while waiting |

### Seasonal Modifier Domain (`seasonal.*`)

| Axis | Range | Description |
|------|-------|-------------|
| `winter_activity_modifier` | 0.3-1.2 | Activity level change in cold season |
| `winter_range_modifier` | 0.5-2.0 | Range size change in winter |
| `drought_water_attraction` | 0.0-1.0 | How strongly drought pulls predator to water sources |
| `breeding_aggression_modifier` | 0.0-0.5 | Additional aggression during breeding season |
| `cubbing_range_reduction` | 0.0-0.8 | Range reduction when rearing young |

---

## Part 7: Interaction Rules

### Rule 1: Hunt Target Selection

```
For each potential prey in perception range:
  viability = prey_caloric_value / own_caloric_need
  size_match = 1.0 - abs(prey_mass / own_mass - prey_size_ratio) * (1 - prey_size_flexibility)
  risk = prey_danger_rating * injury_aversion * (1 - hunger * metabolic_urgency)
  score = viability * size_match * (1 - risk)
Select highest-scoring target; if all scores < threshold, consider scavenging or waiting.
```

### Rule 2: Interspecific Encounter Resolution

```
competitive_weight = body_mass * (1 + cooperative_hunting * group_size_factor) * dominance_assertion

When detecting another predator species at a resource:
  weight_ratio = own_competitive_weight / other_competitive_weight
  If weight_ratio > 2.0: approach and displace (automatic)
  If weight_ratio 1.3-2.0: contest likely, outcome probabilistic weighted by ratio
  If weight_ratio < 0.5: flee or avoid
  Else: standoff, modified by dominance_assertion and hunger
```

### Rule 3: Temporal Niche Adjustment

```
perceived_apex_activity = recent_apex_encounters_during_preferred_time
temporal_shift = temporal_flexibility * perceived_apex_activity * injury_aversion
effective_nocturnality = base_nocturnality + temporal_shift
```

Produces the documented pattern where mesopredators become more nocturnal in areas with high apex predator density.

### Rule 4: Hunger-Driven Risk Escalation

```
desperation = hunger * metabolic_urgency
effective_injury_aversion = injury_aversion * (1 - desperation * 0.7)
effective_prey_size_flexibility = prey_size_flexibility + desperation * 0.3
effective_territory_fidelity = territory_fidelity * (1 - desperation * 0.5)
effective_nocturnality_shift = nocturnality * (1 - desperation * 0.3)
```

Hunger is the **master variable** -- almost all interesting predator behavior emerges from its interaction with risk tolerance.

### Rule 5: Scavenger Cascade Arrival

```
On kill event at location:
  For each scavenger-type predator in detection range:
    detection_time = distance / (carrion_detection * detection_speed_factor)
    arrival_priority = detection_time + (1 / competitive_weight) * random_factor
  Natural result: vultures first, hyenas next, jackals last, lions steal if present
```

### Rule 6: Mesopredator Release Detection

```
if apex_predator_encounter_frequency < threshold_for_location:
  mesopredator_confidence += confidence_recovery_rate
  effective_range_size *= 1 + mesopredator_confidence * 0.3
  effective_nocturnality -= temporal_flexibility * mesopredator_confidence * 0.2
```

When apex predators are removed, mesopredators become bolder, more active, wider-ranging.

### Rule 7: Territory Intrusion Response

```
response_level = base_level(distance_from_core)
               + reproductive_modifier
               + resource_proximity_modifier
               - familiarity_modifier(intruder_id)  // "dear enemy" effect
               + numerical_disadvantage_modifier(group_sizes)
Clamp to levels 1-6. Each level has energy cost, time cost, injury probability.
```

---

## Part 8: Species Profile Examples

### Wolf (Apex, Pack Hunter, Strictly Territorial)

```
hunting.ambush_preference:        0.15
hunting.pursuit_endurance:        0.85
hunting.stalking_patience:        0.3
hunting.cooperative_hunting:      0.9
hunting.prey_size_ratio:          2.5
hunting.prey_size_flexibility:    0.4
temporal.nocturnality:            0.3
temporal.crepuscular_bias:        0.6
temporal.temporal_flexibility:    0.3
spatial.territory_fidelity:       0.75
spatial.range_size_factor:        12.0
spatial.den_attachment:           0.7  (seasonal -- high during denning)
competition.dominance_assertion:  0.75
competition.kleptoparasitism:     0.3
competition.conspecific_tolerance: 0.1
metabolic.metabolic_urgency:      0.4
metabolic.injury_aversion:        0.6
metabolic.scavenging_willingness: 0.45
metabolic.dietary_breadth:        0.3
seasonal.winter_range_modifier:   1.5
seasonal.cubbing_range_reduction: 0.5
```

### Leopard (Apex, Solitary Ambush, Overlapping Range + Chokepoint)

```
hunting.ambush_preference:        0.8
hunting.pursuit_endurance:        0.3
hunting.stalking_patience:        0.85
hunting.cooperative_hunting:      0.05
hunting.prey_size_ratio:          0.8
hunting.prey_size_flexibility:    0.6
temporal.nocturnality:            0.7
temporal.temporal_flexibility:    0.4  (shifts to later night near lions)
spatial.territory_fidelity:       0.9
spatial.range_size_factor:        3.0
spatial.vertical_preference:      0.6  (arboreal kill caching)
competition.dominance_assertion:  0.5
competition.kleptoparasitism:     0.1
metabolic.injury_aversion:        0.8
metabolic.scavenging_willingness: 0.15
```

### Spotted Hyena (Mesopredator/Kleptoparasite, Clan, Fission-Fusion)

```
hunting.ambush_preference:        0.1
hunting.pursuit_endurance:        0.9
hunting.cooperative_hunting:      0.75
hunting.prey_size_ratio:          1.5
hunting.prey_size_flexibility:    0.6
temporal.nocturnality:            0.65
temporal.temporal_flexibility:    0.5
spatial.territory_fidelity:       0.6
spatial.den_attachment:           0.8  (communal den anchor)
competition.dominance_assertion:  0.55
competition.kleptoparasitism:     0.8
metabolic.scavenging_willingness: 0.7
metabolic.dietary_breadth:        0.5
sensory.carrion_detection:        0.8
```

### Cheetah (Subordinate Apex, Speed Pursuit, Nomadic Females)

```
hunting.ambush_preference:        0.05
hunting.pursuit_endurance:        0.3  (explosive sprinter, overheats)
hunting.stalking_patience:        0.6
hunting.cooperative_hunting:      0.2  (male coalitions only)
hunting.prey_size_ratio:          0.8
hunting.prey_size_flexibility:    0.3  (specialist)
temporal.nocturnality:            0.1  (strictly diurnal -- anti-lion adaptation)
temporal.temporal_flexibility:    0.1  (locked into diurnal)
spatial.territory_fidelity:       0.3  (nomadic females, small territory males)
spatial.range_size_factor:        8.0
competition.dominance_assertion:  0.1  (abandons kills, avoids all competitors)
competition.kleptoparasitism:     0.0
metabolic.injury_aversion:        0.95 (injury = death for a speed specialist)
metabolic.scavenging_willingness: 0.05
```

### African Wild Dog (Mesopredator, Pack Endurance, Nomadic)

```
hunting.ambush_preference:        0.05
hunting.pursuit_endurance:        0.95 (marathon endurance pursuit)
hunting.cooperative_hunting:      0.95
hunting.prey_size_ratio:          1.5
temporal.nocturnality:            0.15
temporal.crepuscular_bias:        0.7
spatial.territory_fidelity:       0.2  (nomadic over 500-2000 km²)
spatial.range_size_factor:        15.0
spatial.den_attachment:           0.8  (when denning -- normally 0.0)
competition.dominance_assertion:  0.15
metabolic.metabolic_urgency:      0.5
metabolic.injury_aversion:        0.7
seasonal.cubbing_range_reduction: 0.7
```

### Nile Crocodile (Apex, Ambush Chokepoint)

```
hunting.ambush_preference:        0.98
hunting.pursuit_endurance:        0.05
hunting.stalking_patience:        0.95
hunting.cooperative_hunting:      0.0
hunting.prey_size_ratio:          1.0-3.0  (takes large prey at water)
temporal.nocturnality:            0.4
spatial.territory_fidelity:       0.95
spatial.range_size_factor:        0.01  (tiny river section)
spatial.water_dependency:         1.0
competition.dominance_assertion:  0.9  (at position -- immediate escalation)
competition.conspecific_tolerance: 0.1
metabolic.metabolic_urgency:      0.05 (can fast for weeks)
metabolic.fasting_tolerance:      0.95
metabolic.injury_aversion:        0.4  (armored, low injury risk)
```

### Vulture (Obligate Scavenger)

```
hunting.ambush_preference:        0.0
hunting.pursuit_endurance:        0.0
hunting.prey_size_ratio:          0.0  (does not hunt)
spatial.vertical_preference:      0.95
spatial.range_size_factor:        15.0
competition.dominance_assertion:  0.15
competition.conspecific_tolerance: 0.9
metabolic.scavenging_willingness: 0.98
metabolic.fasting_tolerance:      0.7
sensory.visual_detection_range:   0.95
sensory.carrion_detection:        0.95
```

### Fox (Mesopredator, Opportunist)

```
hunting.ambush_preference:        0.5
hunting.pursuit_endurance:        0.4
hunting.cooperative_hunting:      0.05
hunting.prey_size_ratio:          0.3
hunting.prey_size_flexibility:    0.7
hunting.surplus_killing:          0.7
temporal.nocturnality:            0.6
temporal.temporal_flexibility:    0.7  (high -- shifts heavily under pressure)
spatial.territory_fidelity:       0.6
competition.dominance_assertion:  0.2
metabolic.metabolic_urgency:      0.65
metabolic.injury_aversion:        0.85
metabolic.scavenging_willingness: 0.55
metabolic.dietary_breadth:        0.75
```

---

## Part 9: Simulation Simplifications

### Simplification 1: Location-Based Prey Availability

Instead of tracking every prey animal individually, each Location has a `prey_availability` score:

```
prey_availability: 0.0 (barren) to 1.0 (abundant)
recovery_rate: how fast prey bounces back after predation
carrying_capacity: maximum for this location type
```

When a predator hunts successfully, `prey_availability` decreases. It recovers over game-time via the Worldstate clock. The few individually-tracked prey NPCs (quest targets, named animals, rare species) coexist with the abstract pool.

### Simplification 2: Scent Trails as Decaying Location Metadata

When a predator passes through a Location, it leaves a scent record:

```
scent_record: { species_code, competitive_weight, timestamp, direction }
decay: disappears after N game-hours (species-dependent, climate-dependent)
```

Prey avoids locations with fresh predator scent. Subordinate predators avoid fresh dominant predator scent. Tracking predators follow prey scent. Creates the landscape of fear without particle simulation.

### Simplification 3: Hunger as the Universal Motivator

A single `hunger` value (0.0 satiated to 1.0 starving) increases at a rate determined by `metabolic_urgency`:

```
hunger_increase_per_game_hour = metabolic_urgency * base_hunger_rate
```

As hunger approaches 1.0, `injury_aversion` decreases, `prey_size_flexibility` widens, `territory_fidelity` decreases, and temporal preferences weaken. This single state variable produces most of the behavioral variation that full metabolic models produce.

### Simplification 4: Territory as Familiarity Bonus

Rather than explicit territory polygons:

```
location_familiarity: increases with time spent, decays when absent
hunting_bonus: familiarity * territory_fidelity * 0.3
```

Predators hunt more effectively in familiar locations. This naturally creates "territories" from success patterns without explicit boundary maintenance. Territorial encounters occur when familiarity zones overlap at high-value locations.

### Simplification 5: Group Dynamics as Modified Individual Behavior

A predator in a group gets modified axes:

```
effective_prey_size_ratio = prey_size_ratio * (1 + cooperative_hunting * (group_size - 1) * 0.3)
effective_dominance_assertion = dominance_assertion * (1 + cooperative_hunting * (group_size - 1) * 0.2)
effective_pursuit_endurance = pursuit_endurance * (1 + cooperative_hunting * relay_bonus)
```

A lone wolf targets deer-sized prey. A pack of 6 effectively targets elk-sized prey. No explicit pack coordination simulation needed.

### Simplification 6: Predator-Prey Ratio Constraint

Sustainable predator density is approximately 1-3% of prey biomass. For 1,000 deer averaging 100 kg (= 100,000 kg prey biomass), expect ~1,000-3,000 kg predator biomass, or ~20-50 wolves at 45 kg each. This constrains how many predator NPCs make ecological sense per area.

---

## Part 10: Bannou Architecture Mapping

### Ethology Service (`${ethology.*}`)

Stores per-species baseline values for all axes, with hierarchical overrides (realm, location) and per-individual deterministic noise. Provides the `${ethology.*}` variable namespace to Actor behaviors.

### Environment Service Integration (`${environment.*}`)

Seasonal and weather modifiers multiply against ethology baselines to produce effective values. `${environment.season}`, `${environment.precipitation}`, `${environment.temperature}` feed into ABML expressions at behavior-time. The Environment service also maintains the `prey_availability` pool per Location as part of its ecological resource layer.

### Character Personality Overlay (`${personality.*}`)

Individual predator NPCs have personality traits as multipliers on ethology baselines. A particularly aggressive wolf has `${personality.aggression}` > 0.5, which ABML multiplies against `${ethology.competition.dominance_assertion}`. Ethology provides the species norm; personality provides individual variation.

### Location Service Integration

Prey availability, scent trails, and familiarity values are stored as Location-associated state. Per the "no metadata bag contracts" tenet, the predator Actor owns its own scent/familiarity data in its own domain.

### Transit Service Integration

Range and territory calculations use Transit's connectivity graph to determine reachable Locations within a predator's range. A predator with `range_size_factor` 12.0 considers all locations within proportional transit distance; one with 3.0 considers only nearby locations.

### Worldstate Integration (`${world.*}`)

Game clock determines time-of-day for temporal partitioning. `${world.time_of_day}` compared against `${ethology.temporal.nocturnality}` determines whether a predator is active. Season boundaries trigger seasonal modifier application.

### ABML Behavior Document Structure

A predator ABML behavior document references ethology axes in GOAP action preconditions and cost calculations:
- Hunt action cost modified by `${ethology.hunting.ambush_preference}`
- Target selection filtered by `${ethology.hunting.prey_size_ratio}`
- Temporal scheduling checked against `${ethology.temporal.nocturnality}` vs `${world.time_of_day}`
- Competitive encounters resolved via `${ethology.competition.competitive_weight}` ratios
- Risk decisions gated by `hunger * ${ethology.metabolic.metabolic_urgency}` vs `${ethology.metabolic.injury_aversion}`

### The Emergent Showcase

The scavenger cascade is the most visually compelling demonstration of the system:

1. A leopard kills an impala (hunt action completes based on `ambush_preference` + `stalking_patience`)
2. Vultures begin circling (high `visual_detection_range` + `carrion_detection`)
3. A hyena pack arrives (high `carrion_detection` + following vulture presence)
4. The leopard drags the kill into a tree (`vertical_preference` 0.6, anti-kleptoparasitism behavior)
5. The hyenas wait below (cannot follow -- `vertical_preference` 0.0)
6. A lion pride passes through, hyenas scatter (`competitive_weight` ratio > 2.0 for individual hyenas)
7. Lions cannot reach tree-cached kill; they move on
8. Hyenas return after lions leave; leopard feeds in the tree

All of this emerges from parameter interactions. No scripts, no event sequences, no hand-authored encounters. This is the living world thesis -- North Star #5 (Emergent Over Authored) -- demonstrated through ecological dynamics.

---

## References

### Primary Ecological Literature

- **Carbone et al. (1999, 2007)**: Body size scaling laws for predator-prey relationships
- **Creel & Creel (1996, 2002)**: African wild dog behavioral ecology, kleptoparasitism quantification
- **Durant (1998, 2000)**: Cheetah spatial and temporal avoidance of lions/hyenas
- **Gaynor et al. (2018)**: Human activity shifting predator activity patterns
- **Jetz et al. (2004)**, **Tucker et al. (2014)**: Home range allometry meta-analyses
- **Palomares & Caro (1999)**: Landmark review of intraguild predation among mammalian carnivores
- **Prugh et al. (2009)**: Global analysis of mesopredator release
- **Ripple & Beschta (2004, 2012)**: Yellowstone wolf reintroduction trophic cascades
- **Ritchie & Johnson (2009)**: Meta-analysis of mesopredator release across ecosystems
- **Rosenzweig (1966)**, **Schoener (1974)**: Foundational niche partitioning theory
- **Shores et al. (2019)**: Cougar-wolf spatial partitioning post-wolf-reintroduction
- **Swanson et al. (2014, 2016)**: Camera-trap temporal partitioning studies (Snapshot Serengeti)

### Reference Books

- Mech & Boitani, *Wolves: Behavior, Ecology, and Conservation* (2003)
- Sunquist & Sunquist, *Wild Cats of the World* (2002)
- Macdonald & Loveridge, *Biology and Conservation of Wild Felids* (2010)

---

*This document is research input for the Ethology service behavioral archetype system. It does not prescribe implementation details -- those belong in the Ethology deep dive (`docs/plugins/ETHOLOGY.md`) and implementation plans. The ecological patterns described here should inform the choice of behavioral axes, their default values per species, and the interaction rules encoded in ABML behavior documents.*
