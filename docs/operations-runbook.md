# Finite Earth Operations Runbook (Single Server Mode)

## Runtime Topology

1. Unity WebGL client in browser
2. `backend/gateway` server
3. MegaETH testnet `GlobalCounters` contract

## Gateway Environment Variables

1. `PORT`
2. `JWT_SECRET`
3. `MAX_INTENTS_PER_MINUTE`
4. `DEV_AUTH_ENABLED`
5. `GATEWAY_DB_PATH`
6. `MEGAETH_RPC_URL`
7. `RELAYER_PRIVATE_KEY`
8. `GLOBAL_COUNTERS_ADDRESS`
9. `CHAIN_RELAY_POLL_MS`

## Startup Order

1. Deploy `GlobalCounters`.
2. Configure gateway `.env`.
3. Start gateway.
4. Build/host Unity WebGL client.

## Unity Editor Dev Mode

1. Keep `DEV_AUTH_ENABLED=true` on gateway.
2. Unity uses `/auth/dev-login` in editor via `WalletSessionController`.
3. WebGL production builds still use full SIWE nonce/signature flow.

## Incident: Chain Commit Lag

1. Check `/health` for `pendingChainBatches`.
2. Verify RPC endpoint and gateway relay wallet balance.
3. Confirm `GLOBAL_COUNTERS_ADDRESS` is correct.
4. Inspect gateway logs for retry backoff.

## Incident: SIWE Login Failures

1. Verify browser wallet is on expected chain context.
2. Ensure nonce has not expired.
3. Verify `JWT_SECRET` stability after restarts.

## Updater Key Rotation

1. Call `setUpdater(newUpdater)` from contract owner.
2. Update gateway `RELAYER_PRIVATE_KEY`.
3. Restart gateway.
4. Confirm new `CycleCommittedToChain` messages in realtime feed.
