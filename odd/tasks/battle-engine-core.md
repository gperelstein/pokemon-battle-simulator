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

- [x] T1 (S1) Solution scaffold: engine, actors, tests projects under `backend/` — inline — 02783ef
- [x] T2 (S4, S5) Engine core: singles/doubles slots, action queue, resumable turn with mid-turn decision requests (self-switch moves) and end-of-turn faint replacements — inline — 5f9c7f4 (20 tests green)
- [x] T3 (S1, S5) `BattleActor` (Akka.NET) orchestrating decision requests per player — inline — f832ba8 (RED: compile failure; GREEN: 3 tests)
- [x] T4 (S2, S3) Catalog importer: Gen 6 data from Showdown, Gen 1 species + megas, learnsets, moves, abilities, items, type chart — inline — 5294088 (export + data), f9de58c (Hidden Power ids), bb796e1 (catalog, stats, type chart), 9be5c2a (engine targets), 671d83f (team builder). RED compile failures observed before catalog, targeting and builder; GREEN 70 tests.
- [ ] T5 (S2) Full Gen 6 damage calc (type chart, crits, modifiers) wired to catalog — inline
- [ ] T6 (S2) Mechanics coverage: status, stat stages, weather, abilities, items, mega evolution — multiple slices
- [ ] T7 (S1) HTTP/SignalR API hosting battles — inline

## Log

- L1 (2026-11-13): "En este proyecto me gustaría crear un simulador de batallas pokemon online. La estructura del repositorio va a ser monorepo. Ya están creadas las carpetas del backend y el frontend. Quiero empezar trabajando en el backend. El backend debe estar hecho en .net 10 y usando akka.net para gestionar las batallas. Quiero que las reglas y el catalogo sigan el de la sexta generación con todos los items, movesets, pokemon, mega evoluciones, habilidades, etc de esa generación. Quiero en una primer etapa tener disponibles solamente los pokemon de la primera generación"
- L2 (2026-11-13): "Quiero que pueda haber singles y dobles. Desde el principio me gustaría que haya algo que se tenga en cuenta. Un turno no se resuelve completamente a partir del input inicial de los players. Hay eventos que obligan a que un jugudor tenga que cargar un nuevo input a la mitad del turno. Por ejemplo el ataque volt turn, obliga al jugador que lo ejecuta a tener que seleccionar un nuevo pokemon para salir al campo a la mitad del turno"
- L3: Design decision: engine is a pure domain library (no Akka) modelled as a resumable state machine. A turn is an action queue; when an action needs input (self-switch, faint replacement) the engine pauses with the queue intact, emits a `DecisionRequest` only to the affected player, and resumes on submission. `BattleActor` only orchestrates messages.
- L4: T2 tests were written in the same batch as the engine, so RED was not observed separately for T2 (honest gap). Suite: 23/23 green. Review assess (base 9f025a8): medium, `under_budget`, no review due.
- L5: Open points for later tasks: `SubmitDecision` trusts its `Side` field, so T7 must bind each connection to its side; damage still lacks type chart, crits and Gen 6 rounding (T5); Gen 6 speed ties and switch-in order of faint replacements are simplified; Pursuit, Baton Pass, Parting Shot, Eject Button reuse the `MidTurnSwitch` request when implemented (T6).
- L6 (2026-11-13): User authorized the dependency: "Dale, te autorizo instalalo" (@pkmn/dex@0.11.0, @pkmn/data@0.11.0, MIT, dev tool only in backend/tools/catalog-export).
- L7: T4 evidence. Gen 6 values checked against other gens (Butterfree SpA 80 in Gen 5 → 90 in Gen 6; Sucker Punch 80 in Gen 6 vs 70 in Gen 7; Steel no longer resists Dark). Hidden Power variants shared one id in @pkmn; the export now derives ids from names and legality maps every variant to "hiddenpower". Engine `MoveTarget` now mirrors every Showdown target; choosing an ally as target is not supported yet (Normal behaves like AdjacentFoe). Format clauses (Species/Item Clause) are not applied by TeamBuilder.
- L8: Risk: item 5 (new npm devDependencies). Native review consented ("Revisar este cambio") but START stopped with `lens_context_budget_exceeded` (39k lines of generated JSON); no authority created. Independent verifier: PASS on all 7 checks (exact pins, 4 registry packages with integrity and no install scripts, node_modules ignored, no network in export.mjs, byte-identical regeneration via npm ci, 70/70 tests, Gen 6 spot checks). `.atl/` excluded locally via .git/info/exclude.
- Next step: T5 full Gen 6 damage calc (type chart, crits, modifiers, Gen 6 rounding) wired to the catalog.
