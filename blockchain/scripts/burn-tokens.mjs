// Burn all FRT and CRT from the relayer wallet to reset on-chain supply to 0.
import { ethers } from "ethers";

const RPC = process.env.MEGAETH_RPC_URL ?? "https://6342.rpc.thirdweb.com";
const KEY = process.env.RELAYER_PRIVATE_KEY;
const FRT = process.env.FOREST_TOKEN_ADDRESS;
const CRT = process.env.CARBON_TOKEN_ADDRESS;

const ABI = [
  "function syncForest(uint64 cycleId, int256 forestDelta, address relayAddr) external",
  "function syncCarbon(uint64 cycleId, int256 carbonDelta, address relayAddr) external",
  "function lastSyncedCycle() view returns (uint64)",
  "function totalSupply() view returns (uint256)",
  "function decimals() view returns (uint8)"
];

async function main() {
  const provider = new ethers.JsonRpcProvider(RPC, { chainId: 6343, name: "megaeth" }, { staticNetwork: true });
  provider.getFeeData = async () => {
    const gp = await provider.send("eth_gasPrice", []);
    return new ethers.FeeData(BigInt(gp), null, null);
  };
  const signer = new ethers.Wallet(KEY, provider);
  const frt = new ethers.Contract(FRT, ABI, signer);
  const crt = new ethers.Contract(CRT, ABI, signer);

  const frtSupply = await frt.totalSupply();
  const crtSupply = await crt.totalSupply();
  const frtCycle  = await frt.lastSyncedCycle();
  const crtCycle  = await crt.lastSyncedCycle();

  console.log(`FRT supply: ${ethers.formatEther(frtSupply)} (cycle ${frtCycle})`);
  console.log(`CRT supply: ${ethers.formatEther(crtSupply)} (cycle ${crtCycle})`);

  if (frtSupply > 0n) {
    const frtBurnDelta = -(frtSupply / BigInt(1e18));
    console.log(`\nBurning ${frtBurnDelta * -1n} FRT (cycle ${frtCycle + 1n})...`);
    const tx1 = await frt.syncForest(frtCycle + 1n, frtBurnDelta, signer.address);
    console.log(`tx: ${tx1.hash}`);
    await tx1.wait();
  }

  if (crtSupply > 0n) {
    const crtBurnDelta = -(crtSupply / BigInt(1e18));
    console.log(`Burning ${crtBurnDelta * -1n} CRT (cycle ${crtCycle + 1n})...`);
    const tx2 = await crt.syncCarbon(crtCycle + 1n, crtBurnDelta, signer.address);
    console.log(`tx: ${tx2.hash}`);
    await tx2.wait();
  }

  console.log("\n✓ Token supply reset to 0.");
}

main().catch(e => { console.error(e); process.exit(1); });
