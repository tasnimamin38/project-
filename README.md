# ANOMALY — Anti-Gravity Multiverse

ANOMALY is being developed as a Godot 4.7.2 3D action-game ecosystem. The game client is GDScript-first. Unity-specific C# is not part of the active architecture.

## Repository map
- index.html, assets/: existing public-facing game portal prototype.
- game/: native Godot 4.7.2 project and playable 3D vertical slice.
- docs/: architecture, roadmap, security boundaries, and backend contracts.
- .github/workflows/: automated Godot project validation.

## Open the game
1. Install Godot 4.7.2 Standard (not the .NET build).
2. In Godot Project Manager, import game/project.godot.
3. Open the project and run the main scene.
4. The first playable slice contains a procedural arena, zero-gravity movement, an anti-gravity field, hostile drones, a hitscan weapon, HUD, local progression, and an ENet host/join foundation.

Controls: WASD to thrust, Space/Ctrl to rise/drop, mouse to aim, left click to fire, R to reload, Esc to release the mouse.

## Important status
The active development branch is Godot-first. The old Unity prototype pull request was closed and is retained only as historical reference. It is not used by the active Godot project.

The playable slice is a development foundation, not a finished multiplayer product. Real account authentication, authoritative online economy, payment processing, cloud saves, matchmaking, dedicated server hosting, final art/audio, anti-cheat, and production operations still require implementation and testing. The store must not accept real payments until server-side validation and a payment provider are integrated.

See docs/ARCHITECTURE.md, docs/ROADMAP.md, and docs/BACKEND_CONTRACT.md.
