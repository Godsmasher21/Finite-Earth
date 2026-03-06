# Finite Earth Maincloud + Thirdweb Deployment

## What you actually need

For `browser client + server + chain`, run all three:

1. Unity WebGL client (hosted static site)
2. Backend gateway server (`backend/gateway`)
3. MegaETH contract (`contracts/GlobalCounters.sol`)

SpacetimeDB Maincloud is for authoritative multiplayer state if you choose to run that module separately. In the current repo runtime, gateway is the active authoritative service.

## Files you configure

Created for you:

1. `backend/gateway/.env`
2. `web/bridge/.env`
3. `backend/relayer/.env` (optional split deployment)
4. `backend/indexer/.env` (optional split deployment)

## Where to upload what

1. **SpacetimeDB Maincloud**
   - Upload/publish: `backend/spacetimedb/module`
   - Keep: project/database URL, identity, and API token from Maincloud dashboard
2. **MegaETH contract**
   - Deploy from: `contracts/`
   - Keep: deployed `GLOBAL_COUNTERS_ADDRESS`
3. **Gateway server**
   - Deploy folder: `backend/gateway`
   - Good targets: Railway / Render / Fly.io
4. **Unity WebGL client**
   - Upload Unity WebGL build output to: Netlify / Vercel / Cloudflare Pages / S3+CloudFront
5. **Web bridge bundle**
   - Build in: `web/bridge`
   - Include `web/bridge/dist/index.js` in your Unity WebGL template page before Unity boot

## Thirdweb setup for Google + account creation

In Thirdweb dashboard:

1. Create/select project and get **Client ID**
2. Enable **In-App Wallet / Embedded Wallet**
3. Enable auth providers:
   - Google
   - Email
4. Add allowed domains:
   - your local dev domain
   - your production WebGL domain

Then set:

1. `web/bridge/.env`:
   - `FINITE_EARTH_THIRDWEB_CLIENT_ID=<your_client_id>`
   - `FINITE_EARTH_CHAIN_ID=6342` (or your MegaETH testnet chain id)

This gives:

1. Google login -> auto wallet generated + linked to account
2. Email account creation/login -> auto wallet generated + linked
3. Injected wallet path still available (MetaMask/etc)

## Gateway env minimum

Set these in `backend/gateway/.env`:

1. `JWT_SECRET` (strong random value)
2. `MEGAETH_RPC_URL`
3. `RELAYER_PRIVATE_KEY`
4. `GLOBAL_COUNTERS_ADDRESS`
5. `DEV_AUTH_ENABLED=false` for production

## Run order

1. Deploy contract (`contracts/`)
2. Deploy/start gateway (`backend/gateway`)
3. Build web bridge (`web/bridge`) and include script in WebGL template
4. Build Unity WebGL and upload client
5. (Optional) publish SpacetimeDB module to Maincloud
