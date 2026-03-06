# Finite Earth MVP Testing Matrix

## Unit

1. `ActionResolver` rejects illegal transitions.
2. `ActionResolver` applies expected forest/carbon deltas.
3. `ActionOrdering` deterministic sort stability.

## Integration

1. Two wallets conflict on same tile in same cycle.
2. Auth rejects stale or mismatched nonce.
3. Gateway relay aggregates <=200 commits and sends one cycle tx.

## End-to-End

1. WebGL wallet connect -> SIWE -> realtime join.
2. Submit action -> receive `ActionCommitted`.
3. On-chain `CycleCommitted` appears and gateway `/export/csv` includes the cycle record.

## Performance

1. Simulate 20 clients for 30 minutes.
2. Verify no dropped commits and stable cycle cadence.
