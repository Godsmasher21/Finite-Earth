# Finite Earth Server (Gateway)

This is the single backend server for the runtime architecture:

1. Browser Unity WebGL client
2. One server (`backend/gateway`)
3. MegaETH testnet contract

## Built-in responsibilities

1. SIWE auth (`/auth/siwe/nonce`, `/auth/siwe/verify`, `/auth/refresh`)
2. Optional editor-only dev auth (`/auth/dev-login` when `DEV_AUTH_ENABLED=true`)
3. Authoritative simulation loop (30s cycle, deterministic queue)
4. WebSocket realtime stream (`/realtime`)
5. Chain relay (`commitCycle`) using server-held private key
6. Local analytics storage (SQLite)
7. Leaderboard + time-series + CSV export APIs

## Environment

Use `.env.example`:

1. `PORT`
2. `JWT_SECRET`
3. `MAX_INTENTS_PER_MINUTE`
4. `DEV_AUTH_ENABLED`
5. `GATEWAY_DB_PATH`
6. `MEGAETH_RPC_URL`
7. `RELAYER_PRIVATE_KEY`
8. `GLOBAL_COUNTERS_ADDRESS`
9. `CHAIN_RELAY_POLL_MS`

## Run

```bash
npm install
npm run dev
```

## Core endpoints

1. `POST /auth/siwe/nonce`
2. `POST /auth/siwe/verify`
3. `GET /world/snapshot`
4. `WS /realtime?token=...`
5. `GET /leaderboard`
   - supports `limit` and `offset` query params
   - returns ranked players with `rank`, `wallet_address`, `sustainability_score`, `actions_taken`, `owned_tiles_count`
6. `GET /metrics/timeseries`
7. `GET /export/csv`
