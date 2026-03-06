# Backend Layout

## Primary runtime service

1. `gateway` - single server for auth, authoritative simulation, chain relay, and analytics APIs.

## Optional split-service scaffolds

1. `spacetimedb/module`
2. `relayer`
3. `indexer`

These are retained for future decomposition but are not required for the current browser client + server + chain deployment.
