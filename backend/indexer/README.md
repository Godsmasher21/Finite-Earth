# Finite Earth Indexer

Indexes on-chain `CycleCommitted` events and authoritative action commit feed.

## Outputs

1. `GET /leaderboard` (supports `limit` and `offset`)
2. `GET /metrics/timeseries`
3. `GET /export/csv`

Uses local SQLite for MVP analytics and research export workflows.
