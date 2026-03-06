# Finite Earth Relayer

Consumes committed cycle deltas and submits batched updates to `GlobalCounters` on MegaETH testnet.

## Behavior

1. Polls `GET /internal/commits/pending`.
2. Batches up to 200 commits.
3. Calls `commitCycle`.
4. Acks successful commits back to gateway.
5. Retries failed commits with exponential backoff without dropping queued records.
