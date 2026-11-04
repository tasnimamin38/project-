# ANOMALY Godot Client

Engine: Godot 4.7.2 Standard  
Language: GDScript  
Renderer: Compatibility (required for Godot 4 web exports)  
Initial target: Windows/Linux desktop; browser export is a separate optimized build target.

## Current playable vertical slice
- Procedurally assembled 3D arena with collision.
- Mouse-look and momentum-based zero-G thrust movement.
- Configurable radial anti-gravity field.
- Hostile drone AI with health and contact damage.
- Hitscan weapon with magazine and reload.
- HUD for health, ammo, enemy count, and connection state.
- Versioned local profile/save service.
- Sandbox wallet service that is deliberately unable to process real purchases.
- ENet host/join transport foundation.

## Run
Open this folder as a Godot project and run the main scene. No Unity editor, Unity package, C# runtime, or Unity asset pipeline is required.

## Controls
| Input | Action |
|---|---|
| W / A / S / D | Thrust relative to camera |
| Space | Upward thrust |
| Ctrl | Downward thrust |
| Mouse | Aim |
| Left click | Fire |
| R | Reload |
| Esc | Release/capture mouse |

The mouse is captured after the first click. On browser builds, this follows the browser's user-gesture requirement.

## Network prototype
Open the game with the command-line argument --server to start an ENet server on UDP port 7000. A development client can join using NetworkService.join_server(address, port). The current network service confirms peer connectivity only; authoritative player replication, lag compensation, matchmaking, reconnect policy, and anti-cheat are later milestones.

## Save data
Local profile data is stored under Godot's user:// directory through ConfigFile. It is not presented as encrypted or cheat-proof. Competitive progression and currency must be validated by a trusted backend before launch.

## Web export
Use the Compatibility renderer and Godot's Web export template. Godot 4 web exports target WebAssembly and WebGL 2.0. Browser builds need separate profiling and feature restrictions; a high-end native 3D build should remain the primary quality target.

Do not commit .godot/, exported binaries, API credentials, signing keys, or payment secrets.
