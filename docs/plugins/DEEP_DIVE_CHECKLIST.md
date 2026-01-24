# Deep Dive Document Checklist

> Ordered by plugin complexity (simplest first).
> File naming: `docs/plugins/{SERVICE-KEY-UPPER}.md` (e.g., `GAME-SERVICE.md`)
> Links are auto-generated in `docs/GENERATED-SERVICE-DETAILS.md` via `scripts/generate-service-details-docs.py`

---

## Status Legend

- [x] Complete
- [ ] Not started

---

## Tier 1: Simple CRUD / Wrappers (4-10 endpoints)

| # | Plugin | Endpoints | File | Status |
|---|--------|-----------|------|--------|
| 1 | lib-messaging | 4 | `MESSAGING.md` | [x] |
| 2 | lib-game-service | 5 | `GAME-SERVICE.md` | [x] |
| 3 | lib-state | 6 | `STATE.md` | [x] |
| 4 | lib-subscription | 7 | `SUBSCRIPTION.md` | [x] |
| 5 | lib-relationship | 7 | `RELATIONSHIP.md` | [x] |
| 6 | lib-voice | 7 | `VOICE.md` | [x] |
| 7 | lib-character-personality | 9 | `CHARACTER-PERSONALITY.md` | [x] |
| 8 | lib-realm | 10 | `REALM.md` | [x] |
| 9 | lib-character | 10 | `CHARACTER.md` | [x] |
| 10 | lib-realm-history | 10 | `REALM-HISTORY.md` | [x] |
| 11 | lib-character-history | 10 | `CHARACTER-HISTORY.md` | [x] |

## Tier 2: Moderate Complexity (7-15 endpoints with non-trivial logic)

| # | Plugin | Endpoints | File | Status |
|---|--------|-----------|------|--------|
| 12 | lib-mesh | 8 | `MESH.md` | [x] |
| 13 | lib-permission | 8 | `PERMISSION.md` | [x] |
| 14 | lib-music | 8 | `MUSIC.md` | [x] |
| 15 | lib-game-session | 11 | `GAME-SESSION.md` | [x] |
| 16 | lib-matchmaking | 11 | `MATCHMAKING.md` | [x] |
| 17 | lib-leaderboard | 12 | `LEADERBOARD.md` | [x] |
| 18 | lib-species | 13 | `SPECIES.md` | [x] |
| 19 | lib-relationship-type | 13 | `RELATIONSHIP-TYPE.md` | [x] |
| 20 | lib-item | 13 | `ITEM.md` | [x] |
| 21 | lib-actor | 15 | `ACTOR.md` | [x] |
| 22 | lib-inventory | 16 | `INVENTORY.md` | [x] |
| 23 | lib-location | 17 | `LOCATION.md` | [x] |

## Tier 3: Complex (many endpoints or significant internal subsystems)

| # | Plugin | Endpoints | File | Status |
|---|--------|-----------|------|--------|
| 24 | lib-connect | 5 | `CONNECT.md` | [x] |
| 25 | lib-behavior | 6 | `BEHAVIOR.md` | [x] |
| 26 | lib-analytics | 8 | `ANALYTICS.md` | [x] |
| 27 | lib-website | 17 | `WEBSITE.md` | [x] |
| 28 | lib-mapping | 18 | `MAPPING.md` | [x] |
| 29 | lib-character-encounter | 19 | `CHARACTER-ENCOUNTER.md` | [x] |
| 30 | lib-scene | 19 | `SCENE.md` | [x] |
| 31 | lib-escrow | 20 | `ESCROW.md` | [x] |
| 32 | lib-asset | 20 | `ASSET.md` | [x] |
| 33 | lib-achievement | 11 | `ACHIEVEMENT.md` | [x] |
| 34 | lib-auth | 13 | `AUTH.md` | [x] |
| 35 | lib-account | 16 | `ACCOUNT.md` | [x] |
| 36 | lib-orchestrator | 22 | `ORCHESTRATOR.md` | [x] |
| 37 | lib-save-load | 26 | `SAVE-LOAD.md` | [x] |
| 38 | lib-documentation | 27 | `DOCUMENTATION.md` | [ ] |
| 39 | lib-contract | 30 | `CONTRACT.md` | [ ] |
| 40 | lib-currency | 32 | `CURRENCY.md` | [ ] |

---

## Notes

- Tier placement considers internal complexity, not just endpoint count (e.g., lib-connect has 5 endpoints but is the WebSocket gateway)
- Already-completed documents were done in earlier sessions (analytics, asset, achievement, auth, account)
- Filename convention matches the service key from the API schema (e.g., `game-service-api.yaml` → `GAME-SERVICE.md`)
