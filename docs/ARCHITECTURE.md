# ANOMALY technical architecture

## Engine decision
The active game client is Godot 4.7.2 Standard with GDScript. Godot is the 3D engine; Unity is not required. The old Unity C# prototype is archived and not referenced by the Godot project.

## Runtime layers
1. Core — global match state, boot configuration, world selection, lifecycle.
2. World — arena composition, level streaming, gravity volumes, spawn points.
3. Player — CharacterBody3D movement, camera, input, health, inventory.
4. Physics — custom vector-field acceleration layered over Godot physics.
5. Combat — weapon definitions, hitscan/projectile weapons, damage authority.
6. AI — enemy state machines, perception, navigation, combat.
7. UI — main menu, HUD, pause, inventory, settings, accessibility.
8. Progression — missions, tiers, XP, unlocks, achievements.
9. Economy — item catalogue and client presentation; valuable grants and purchases must be server-authoritative.
10. Networking — ENet transport, lobby, peer lifecycle, replication, server authority.
11. Persistence — local profile and later cloud profile synchronization.
12. Platform — Windows/Linux first; Android and Web require separate input/performance validation.

## Current implementation boundary
The first Godot vertical slice builds a procedural arena, a local player, hostile drones, a hitscan weapon, HUD, an anti-gravity field, local save, sandbox wallet, and ENet host/join transport. ENet peer connectivity does not yet equal a production multiplayer game: remote player replication, authoritative combat, lobby/matchmaking, reconnect, server-side validation, and cheat mitigation are still required.

The API client and auth service define a safe integration point but no live backend URL or provider credentials are included. The client refuses non-HTTPS production endpoints.

## Backend trust boundary
Never trust client-reported XP, currency, ownership, payment status, ban state, leaderboard score, or match outcome. The dedicated server validates gameplay; a trusted account/economy API validates identity and purchases. Provider secrets, payment secrets, signing keys, and database service-role keys must stay server-side.

## Performance strategy
- Keep the Compatibility renderer available for Web export.
- Profile native desktop separately; quality presets can be raised where GPU support allows.
- Use object pooling for projectiles, VFX, and repeated AI actors.
- Use LOD, occlusion, culling, bounded particle budgets, and spatial partitioning as content grows.
- Dedicated servers run headless and must not instantiate client-only HUD, camera, or rendering assets.
