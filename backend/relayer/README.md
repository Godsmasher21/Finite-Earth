# Finite Earth Relayer

Consumes committed cycle deltas and submits updates to the global token contracts on MegaETH.

## Behavior

1. Polls SpacetimeDB (preferred) or gateway (fallback).
2. Aggregates cycle deltas.
3. Mints/burns `GlobalForestToken` and `GlobalCarbonToken`.
4. Updates `TilesOwnedSBT` balances.
5. Retries failed commits with exponential backoff.
