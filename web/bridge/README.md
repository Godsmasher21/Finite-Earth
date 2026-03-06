# Finite Earth Web Bridge

Browser-side wallet and SIWE bridge used by Unity WebGL through `ThirdwebBridge.jslib`.

## Responsibilities

1. Initialize Thirdweb client.
2. Connect injected wallet.
3. Login strategies:
   - Google (Thirdweb in-app wallet, auto wallet creation)
   - Email account (Thirdweb in-app wallet, auto wallet creation/linking)
   - Injected wallet (MetaMask/WalletConnect extension path)
4. Build SIWE message.
5. Sign SIWE message.
6. Expose `window.FiniteEarthBridge` API expected by Unity.

## Commands

```bash
npm install
npm run build
```

## Environment

1. `FINITE_EARTH_THIRDWEB_CLIENT_ID`
2. `FINITE_EARTH_CHAIN_ID` (MegaETH testnet chain id)

## WebGL template integration

Bundle or include `web/bridge/dist/index.js` in your Unity WebGL template before Unity boots so `window.FiniteEarthBridge` is available and social login works.
