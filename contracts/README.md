# Finite Earth Contracts

## Contracts

1. `GlobalForestToken.sol` (ERC20, totalSupply == global forest)
2. `GlobalCarbonToken.sol` (ERC20, totalSupply == global carbon)
3. `TilesOwnedSBT.sol` (soulbound ERC20, balanceOf == tiles owned)

Only the operator wallet can mint/burn or set balances.

## Deploy

```bash
npm install
cp .env.example .env
npm run deploy:megaeth-testnet
```

Deployment metadata is saved under `deployments/`.

## MegaETH setup checklist

1. Fill `.env`:
   - `MEGAETH_RPC_URL`
   - `DEPLOYER_PRIVATE_KEY`
   - `GLOBAL_TOKENS_OWNER`
   - `GLOBAL_TOKENS_OPERATOR`
2. Fund deployer account on MegaETH testnet for gas.
3. Run `npm run build`.
4. Deploy with `npm run deploy:megaeth-testnet`.
5. Copy deployed addresses from script output or `deployments/global-tokens-<chainId>.json`.
6. Set backend env (relayer):
   - `GLOBAL_FOREST_TOKEN_ADDRESS=<deployed address>`
   - `GLOBAL_CARBON_TOKEN_ADDRESS=<deployed address>`
   - `TILES_OWNED_SBT_ADDRESS=<deployed address>`
   - `MEGAETH_RPC_URL=<same rpc>`
   - `RELAYER_PRIVATE_KEY=<operator key>`

## Post-deploy verification

1. Start gateway with chain env configured.
2. Trigger at least one committed cycle.
3. Confirm gateway `/health` shows `chainEnabled=true`.
4. Confirm relayer updates token totals and leaderboard balances.
