# Finite Earth Contracts

## Contract

`GlobalCounters.sol` maintains:

1. `forestTotal`
2. `carbonTotal`
3. `lastCycleId`
4. `updater` role

The relayer calls:

```solidity
commitCycle(uint64 cycleId, int256 forestDelta, int256 carbonDelta, bytes32 actionBatchHash, uint32 actionCount)
```

## Deploy

```bash
npm install
cp .env.example .env
npm run deploy:megaeth-testnet
```

Deployment metadata is saved under `deployments/`.
