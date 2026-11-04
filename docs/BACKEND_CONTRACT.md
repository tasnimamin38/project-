# ANOMALY backend contract (implementation plan)

No production backend is connected yet. This contract is the boundary the Godot client and dedicated game server will use.

## Services
- Identity API: account creation, sign-in, session refresh/revoke, provider OAuth callback.
- Player API: profile, settings, cloud saves, inventory, progression.
- Economy API: catalogue, quote, purchase intent, verified provider webhook, refund, transaction history.
- Game Session API: region discovery, lobby, matchmaking ticket, join token.
- Leaderboard API: server-signed match result submission and ranked read APIs.
- Admin API: role-gated player moderation, economy operations, server operations, audit logs.

## Suggested endpoints
| Method | Route | Responsibility |
|---|---|---|
| POST | /v1/auth/sign-in | Create a short-lived session |
| POST | /v1/auth/refresh | Rotate a refresh session |
| POST | /v1/auth/sign-out | Revoke a session |
| GET | /v1/player/me | Read caller profile |
| GET/PUT | /v1/player/save | Read/write versioned cloud save |
| GET | /v1/inventory | Read verified inventory |
| GET | /v1/store/catalogue | Read server-owned catalogue |
| POST | /v1/store/purchase-intents | Start a provider checkout |
| GET | /v1/store/transactions | Read verified transactions |
| POST | /v1/matchmaking/tickets | Request a match |
| POST | /v1/matches/results | Accept server-authenticated match results |
| GET | /v1/leaderboards/{season} | Read a seasonal leaderboard |
| GET | /v1/admin/audit | Read audit events (admin only) |

## Required security rules
1. TLS for every public API endpoint.
2. Short-lived access tokens; refresh-token rotation and revocation.
3. Server-side role checks for every admin operation.
4. Never ship provider client secrets, payment secrets, database service keys, or signing keys in the game.
5. The game client cannot award premium currency or declare a payment complete.
6. Payment webhooks must be signature-verified and idempotent.
7. Game results and ranked scores must be produced/validated by trusted server logic.
8. Apply rate limits, request validation, audit logging, and abuse detection.
9. Back up player records and test restore procedures.
