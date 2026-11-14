# Battle engine core

Objective: .NET 10 backend for an online Pokémon battle simulator (Gen 6 rules/catalog, Gen 1 Pokémon first), battles managed with Akka.NET.
Branch: `feat/battle-engine-core` · Delivery: ask-on-risk · Test runner: `dotnet test backend/PokemonBattleSimulator.slnx` (xUnit).

## Specs

- S1: "El backend debe estar hecho en .net 10 y usando akka.net para gestionar las batallas." Lives under `backend/` of the monorepo.
- S2: "Quiero que las reglas y el catalogo sigan el de la sexta generación con todos los items, movesets, pokemon, mega evoluciones, habilidades, etc de esa generación."
- S3: "Quiero en una primer etapa tener disponibles solamente los pokemon de la primera generación" (plus their Gen 6 megas).
- S4: "Quiero que pueda haber singles y dobles."
- S5: "Un turno no se resuelve completamente a partir del input inicial de los players. Hay eventos que obligan a que un jugudor tenga que cargar un nuevo input a la mitad del turno. Por ejemplo el ataque volt turn, obliga al jugador que lo ejecuta a tener que seleccionar un nuevo pokemon para salir al campo a la mitad del turno"
- Assumption (A1): catalog data is generated from Pokémon Showdown data (MIT) for Gen 6; proposed to the user, not objected.

## Tasks

- [ ] T1 (S1) Solution scaffold: engine, actors, tests projects under `backend/` — inline
- [ ] T2 (S4, S5) Engine core: singles/doubles slots, action queue, resumable turn with mid-turn decision requests (self-switch moves) and end-of-turn faint replacements — inline
- [ ] T3 (S1, S5) `BattleActor` (Akka.NET) orchestrating decision requests per player — inline
- [ ] T4 (S2, S3) Catalog importer: Gen 6 data from Showdown, Gen 1 species + megas, learnsets, moves, abilities, items, type chart — inline
- [ ] T5 (S2) Full Gen 6 damage calc (type chart, crits, modifiers) wired to catalog — inline
- [ ] T6 (S2) Mechanics coverage: status, stat stages, weather, abilities, items, mega evolution — multiple slices
- [ ] T7 (S1) HTTP/SignalR API hosting battles — inline

## Log

- L1 (2026-11-13): "En este proyecto me gustaría crear un simulador de batallas pokemon online. La estructura del repositorio va a ser monorepo. Ya están creadas las carpetas del backend y el frontend. Quiero empezar trabajando en el backend. El backend debe estar hecho en .net 10 y usando akka.net para gestionar las batallas. Quiero que las reglas y el catalogo sigan el de la sexta generación con todos los items, movesets, pokemon, mega evoluciones, habilidades, etc de esa generación. Quiero en una primer etapa tener disponibles solamente los pokemon de la primera generación"
- L2 (2026-11-13): "Quiero que pueda haber singles y dobles. Desde el principio me gustaría que haya algo que se tenga en cuenta. Un turno no se resuelve completamente a partir del input inicial de los players. Hay eventos que obligan a que un jugudor tenga que cargar un nuevo input a la mitad del turno. Por ejemplo el ataque volt turn, obliga al jugador que lo ejecuta a tener que seleccionar un nuevo pokemon para salir al campo a la mitad del turno"
- L3: Design decision: engine is a pure domain library (no Akka) modelled as a resumable state machine. A turn is an action queue; when an action needs input (self-switch, faint replacement) the engine pauses with the queue intact, emits a `DecisionRequest` only to the affected player, and resumes on submission. `BattleActor` only orchestrates messages.
