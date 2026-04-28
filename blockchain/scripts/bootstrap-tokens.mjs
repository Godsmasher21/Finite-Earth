// One-time bootstrap: mint the current forest/carbon totals to match STDB world state.
// Run once after deploying FRT/CRT to sync initial supply.
import { ethers } from "ethers";

const RPC  = process.env.MEGAETH_RPC_URL ?? "https://6342.rpc.thirdweb.com";
const KEY  = process.env.RELAYER_PRIVATE_KEY;
const FRT  = process.env.FOREST_TOKEN_ADDRESS;
const CRT  = process.env.CARBON_TOKEN_ADDRESS;

// Pull these from the game HUD / health endpoint.
const FOREST_TOTAL  = BigInt(process.env.FOREST_TOTAL  ?? "4363");
const CARBON_TOTAL  = BigInt(process.env.CARBON_TOTAL  ?? "22281");
// Use cycle 0 so it's always before any real cycle the relay will send.
const BOOTSTRAP_CYCLE = 0n;

if (!KEY || !FRT || !CRT) {
  console.error("Set RELAYER_PRIVATE_KEY, FOREST_TOKEN_ADDRESS, CARBON_TOKEN_ADDRESS");
  process.exit(1);
}

const ABI_FRT = ["function syncForest(uint64 cycleId, int256 forestDelta, address relayAddr) external",
                 "function lastSyncedCycle() view returns (uint64)"];
const ABI_CRT = ["function syncCarbon(uint64 cycleId, int256 carbonDelta, address relayAddr) external",
                 "function lastSyncedCycle() view returns (uint64)"];

async function main() {
  const provider = new ethers.JsonRpcProvider(RPC, { chainId: 6343, name: "megaeth" }, { staticNetwork: true });
  provider.getFeeData = async () => {
    const gp = await provider.send("eth_gasPrice", []);
    return new ethers.FeeData(BigInt(gp), null, null);
  };
  const signer  = new ethers.Wallet(KEY, provider);
  const frt = new ethers.Contract(FRT, ABI_FRT, signer);
  const crt = new ethers.Contract(CRT, ABI_CRT, signer);

  const frtLastCycle = await frt.lastSyncedCycle();
  const crtLastCycle = await crt.lastSyncedCycle();
  console.log(`FRT lastSyncedCycle: ${frtLastCycle}`);
  console.log(`CRT lastSyncedCycle: ${crtLastCycle}`);

  // Use a cycle ID that is higher than whatever was already synced.
  const frtCycle = frtLastCycle > BOOTSTRAP_CYCLE ? frtLastCycle + 1n : BOOTSTRAP_CYCLE + 1n;
  const crtCycle = crtLastCycle > BOOTSTRAP_CYCLE ? crtLastCycle + 1n : BOOTSTRAP_CYCLE + 1n;

  console.log(`\nMinting ${FOREST_TOTAL} FRT to relayer (cycle ${frtCycle})...`);
  const tx1 = await frt.syncForest(frtCycle, FOREST_TOTAL, signer.address);
  console.log(`tx: ${tx1.hash}`);
  await tx1.wait();

  console.log(`Minting ${CARBON_TOTAL} CRT to relayer (cycle ${crtCycle})...`);
  const tx2 = await crt.syncCarbon(crtCycle, CARBON_TOTAL, signer.address);
  console.log(`tx: ${tx2.hash}`);
  await tx2.wait();

  console.log("\n✓ Bootstrap complete. FRT and CRT supply now mirrors the game world.");
}

main().catch(e => { console.error(e); process.exit(1); });
