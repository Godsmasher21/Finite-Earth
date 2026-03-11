# Finite Earth MVP Stack

Runtime architecture is:

1. Unity WebGL browser client
2. One backend server (`backend/gateway`)
3. MegaETH testnet contract (`contracts/GlobalCounters`)

## What is implemented

### Unity client

1. Deterministic core rules and state types in `Assets/Scripts/Core`.
2. Client orchestration in `Assets/Scripts/Client`.
3. Networking + realtime message contracts in `Assets/Scripts/Networking`.
4. Wallet + realtime WebGL bridges in `Assets/Scripts/Web3` and `Assets/Plugins/WebGL/*.jslib`.
5. Compatibility bootstrap in `TileClaimController` automatically instantiates the new stack unless `useLegacyLocalController` is enabled.
6. Runtime action UI auto-generates if no prebuilt UI exists (`ActionPanelPresenter`).
7. Command-table HUD with planet health, climate alerts, action console, field log, and overlay toggles.
8. ASCII `Market + Diplomacy` panel with trade offers, pact actions, and tech research.
9. Local universal-cycle gameplay loop for offline/editor testing when the gateway is unavailable.

### Gameplay systems currently exposed in the client

1. Persistent-world style cycle simulation: the world does not reset per match in the current client loop.
2. Market board:
   - press `M` to toggle `Market + Diplomacy`
   - post preset trade offers
   - accept or cancel open offers
3. Diplomacy:
   - propose `Non-Aggression`
   - propose `Resource Pact`
   - accept or cancel pacts from the same panel
4. Tech tree:
   - research points increase by `+1 RP` per cycle
   - current MVP nodes are `Basic Forestry`, `Renewable Energy`, and `Carbon Capture`
   - research is performed from the `Market + Diplomacy` panel
5. Overlay modes:
   - `[INF]` for influence / pressure visibility
   - `[RES]` for resource-value visibility
   - `[ECO]` for ecosystem / carbon pressure visibility
6. Climate event tile overlays:
   - affected tiles flash with tint + alpha using `Assets/Resources/Tiles/Tile_Overlay.asset`
   - wildfire, flood, ice melt, desert spread, and heatwave tiles are highlighted in-world

### HUD notes

1. `Field Log` is the runtime notification feed for actions and climate events.
2. `World Node` is currently a lightweight strategic hint panel, not a full minimap yet.
3. The market / diplomacy / tech UI is a separate runtime panel, not embedded into the command console yet.

`ThirdwebBridge.jslib` includes an injected-wallet fallback bridge, so WebGL wallet auth can run even if the optional `web/bridge` bundle is not injected in the page template.

If the gateway is unreachable in editor, `WalletSessionController` can auto-bypass auth and continue in offline/local mode so gameplay remains testable.

### Single backend server

`backend/gateway` now includes:

1. SIWE authentication.
2. Authoritative cycle/tick simulation.
3. Realtime WebSocket broadcast.
4. On-chain cycle relay (`commitCycle`) with retry backoff.
5. SQLite-backed leaderboard and metrics export APIs.

### Chain layer

`contracts/` contains:

1. `GlobalCounters.sol`
2. Hardhat config and deployment script.

## Bring-up order

1. Deploy `GlobalCounters` to MegaETH testnet.
2. Configure `backend/gateway/.env`.
3. Start `backend/gateway`.
4. Build and host Unity WebGL client.

## Notes

`backend/relayer` and `backend/indexer` remain in repo as optional split-service scaffolds, but are not required for the single-server runtime.

## Maincloud + Thirdweb guide

See [docs/maincloud-thirdweb-deployment.md](docs/maincloud-thirdweb-deployment.md) for:

1. `.env` setup
2. Where to upload/deploy each service
3. Thirdweb Google/email in-app wallet login setup

For full online bring-up (Maincloud optional) + MegaETH contract + leaderboard verification, see:

1. [docs/full-online-setup-maincloud-megaeth.md](docs/full-online-setup-maincloud-megaeth.md)

## Itch Demo Build

See [docs/itch-demo-publish.md](docs/itch-demo-publish.md) for:

1. offline demo mode behavior
2. Itch upload steps
3. switching demo `<->` multiplayer via URL or config
