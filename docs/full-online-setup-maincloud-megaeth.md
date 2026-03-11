# Finite Earth Full Online Setup (Maincloud + MegaETH + Leaderboard)

This is the non-demo deployment path:

1. Browser Unity WebGL client
2. Gateway backend (authoritative runtime + auth + realtime + leaderboard)
3. MegaETH `GlobalCounters` contract
4. Optional SpacetimeDB Maincloud module

## 1) Deploy contracts on MegaETH testnet

From `contracts/`:

```bash
npm install
cp .env.example .env
```

Fill `.env`:

1. `MEGAETH_RPC_URL`
2. `DEPLOYER_PRIVATE_KEY`
3. `GLOBAL_COUNTERS_OWNER`
4. `GLOBAL_COUNTERS_UPDATER`

Deploy:

```bash
npm run build
npm run deploy:megaeth-testnet
```

After deploy, capture:

1. Deployed address from console output
2. Metadata file: `contracts/deployments/global-counters-<chainId>.json`

## 2) Configure and run gateway

From `backend/gateway/`:

```bash
npm install
cp .env.example .env
```

Fill `.env`:

1. `PORT=8080`
2. `JWT_SECRET=<strong random secret>`
3. `DEV_AUTH_ENABLED=false` (production)
4. `MEGAETH_RPC_URL=<same as contracts>`
5. `RELAYER_PRIVATE_KEY=<updater private key>`
6. `GLOBAL_COUNTERS_ADDRESS=<deployed contract address>`
7. `GATEWAY_DB_PATH=./gateway.db`
8. `MAX_INTENTS_PER_MINUTE=120`
9. `CHAIN_RELAY_POLL_MS=3000`

Run:

```bash
npm run dev
```

Quick checks:

1. `GET /health`
2. `GET /leaderboard`
3. `GET /metrics/timeseries`

## 3) Leaderboard (already implemented)

Gateway endpoint:

1. `GET /leaderboard`
2. `GET /leaderboard?limit=50&offset=0`

Response fields:

1. `total`
2. `limit`
3. `offset`
4. `players[]` with `rank`, `wallet_address`, `sustainability_score`, `actions_taken`, `owned_tiles_count`, `updated_at_ms`

## 4) Optional indexer service

From `backend/indexer/`:

```bash
npm install
cp .env.example .env
```

Set:

1. `GATEWAY_URL=http://localhost:8080`
2. `MEGAETH_RPC_URL=<rpc>`
3. `GLOBAL_COUNTERS_ADDRESS=<contract>`

Run:

```bash
npm run dev
```

Indexer also exposes:

1. `/leaderboard`
2. `/metrics/timeseries`
3. `/export/csv`

## 5) Optional Maincloud setup (SpacetimeDB module path)

Use `backend/spacetimedb/module` and `backend/spacetimedb/schema.sql`.

High-level flow:

1. Create Maincloud project/workspace.
2. Publish module source from `backend/spacetimedb/module`.
3. Apply schema from `backend/spacetimedb/schema.sql` if needed by your Maincloud flow.
4. Save endpoint + credentials in your secret manager.
5. Point realtime clients/services to Maincloud endpoint.

Note: Gateway is the active authoritative runtime in this repo. Maincloud path is scaffolded for split-service adoption.

## 6) Web bridge (Thirdweb social/in-app wallets)

From `web/bridge/`:

```bash
npm install
cp .env.example .env
npm run build
```

Set:

1. `FINITE_EARTH_THIRDWEB_CLIENT_ID`
2. `FINITE_EARTH_CHAIN_ID=<MegaETH testnet chain id>`

Include `web/bridge/dist/index.js` in WebGL template before Unity boot.

## 7) Unity full-version behavior

`WalletSessionController` defaults are already set for full version:

1. `webGlDemoMode=false`
2. `allowOfflineFallbackWhenGatewayUnavailable=false`
3. WebGL builds default online; demo requires explicit `?mode=demo`

Required inspector values:

1. `WalletSessionController.gatewayBaseUrl=<your gateway url>`
2. `SpacetimeRealtimeClient.realtimeEndpoint=ws(s)://<gateway>/realtime`
3. `WalletSessionController.chainId=<MegaETH chain id>`

## 8) End-to-end verification

1. Open WebGL client.
2. Log in (Google/email/injected).
3. Submit actions from two different wallets.
4. Verify realtime world updates for both.
5. Verify `/leaderboard` updates.
6. After cycle commit, verify `/metrics/timeseries` and chain tx hash propagation.
