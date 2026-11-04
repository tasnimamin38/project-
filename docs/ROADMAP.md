# ANOMALY ecosystem roadmap

Status labels describe work in the repository, not marketing claims.

## Stage 1 — Godot foundation (in this branch)
- [x] Godot 4.7.2 project and GDScript-first folder layout
- [x] Procedural 3D arena and local player controller
- [x] Zero-G thrust movement and radial anti-gravity field
- [x] Drone enemy prototype and hitscan weapon
- [x] Basic HUD and local profile save
- [x] Sandbox wallet that blocks real purchases
- [x] ENet host/join transport foundation
- [x] Headless smoke-test workflow

## Stage 2 — Complete offline game loop
- [ ] Main menu and world/mission selection
- [ ] Full player animation, weapons, reload/equip, damage feedback
- [ ] Enemy archetypes, behaviors, boss encounter, navigation
- [ ] Four authored worlds: Nexus-0, Ember Drift, Shatterfield, Glass Orbit
- [ ] Tier campaign, 10 sub-levels per tier, mission rewards
- [ ] Inventory, skins, unlocks, achievements, settings, accessibility
- [ ] Audio, VFX, high-quality 3D assets, save migration and QA

## Stage 3 — Online multiplayer
- [ ] Dedicated server export and deployment
- [ ] Server-authoritative player movement and combat
- [ ] Replication, snapshots, interpolation, prediction, lag handling
- [ ] Lobby, invite codes, matchmaking, regions, reconnect
- [ ] Session authentication and RPC permission checks
- [ ] Load, abuse, latency, and disconnect testing

## Stage 4 — Full web/account ecosystem
- [ ] Production account service and email/social login
- [ ] Cloud saves, profiles, inventory, progression, leaderboard
- [ ] Admin RBAC, audit logs, moderation, player support
- [ ] Server-authoritative economy and transaction ledger
- [ ] Payment provider integration, webhooks, refunds, fraud review
- [ ] Public website, player portal, patch notes, support, status page

## Stage 5 — Release engineering
- [ ] Windows/Linux release builds and signed artifacts
- [ ] Android export and device matrix
- [ ] Web export and GitHub Pages deployment
- [ ] Crash reporting, telemetry consent, privacy/terms, backups
- [ ] Closed alpha, balance testing, security review, release checklist
