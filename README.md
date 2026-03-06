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
