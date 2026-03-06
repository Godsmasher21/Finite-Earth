# Finite Earth Protocol Specification (MVP)

Single-server mode: all auth, authoritative simulation, relay, and analytics endpoints are served by `backend/gateway`.

## Auth

1. `POST /auth/siwe/nonce`
2. `POST /auth/siwe/verify`
3. `POST /auth/refresh`

## Realtime

Endpoint: `WS /realtime?token=<jwt>`

### Client -> Server

`ActionIntentSubmit`

```json
{
  "type": "ActionIntentSubmit",
  "intent": {
    "intentId": "wallet-seq-ts",
    "worldId": "finite-earth-alpha",
    "walletAddress": "0x...",
    "clientSeq": 42,
    "actionType": "Reforest",
    "q": 12,
    "r": 7,
    "buildingType": "None",
    "clientIssuedAtMs": 1762080000000
  }
}
```

### Server -> Client

1. `WorldSnapshot`
2. `CycleStarted`
3. `ActionCommitted`
4. `CycleCommittedToChain` (emitted by the same gateway process after successful on-chain relay)

## Deterministic Ordering

For each cycle:

1. sort by `submitted_at_ms`
2. then `walletAddress`
3. then `intentId`

## Relayer Commit Rule

1. Maximum 200 action commits per on-chain batch.
2. One `commitCycle` transaction per processed cycle.
3. Retry with exponential backoff on failure.
