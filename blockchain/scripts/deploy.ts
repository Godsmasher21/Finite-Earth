import { ethers } from "hardhat";
import * as fs from "fs";
import * as path from "path";

/**
 * Deploys all four Finite Earth contracts to MegaETH (chain 6342).
 *
 * Usage:
 *   npx hardhat run scripts/deploy.ts --network megaeth
 *
 * Reads RELAYER_PRIVATE_KEY from process.env (set in .env via dotenv).
 * After deployment, addresses are written back to backend/gateway/.env
 * so the gateway chain relay activates automatically on next restart.
 */
async function main() {
  const [deployer] = await ethers.getSigners();
  const network = await ethers.provider.getNetwork();
  const balance = await ethers.provider.getBalance(deployer.address);

  console.log(`\nDeploying on MegaETH (chain ${network.chainId})`);
  console.log(`Deployer: ${deployer.address}`);
  console.log(`Balance:  ${ethers.formatEther(balance)} ETH\n`);

  if (balance === 0n) {
    console.error("ERROR: Relayer wallet has 0 ETH. Fund it first:");
    console.error(`  Address: ${deployer.address}`);
    console.error("  Faucet:  https://faucet.megaeth.com\n");
    process.exit(1);
  }

  const relayerAddress = deployer.address;

  // ── GlobalCounters ────────────────────────────────────────────────────────
  const GlobalCounters = await ethers.getContractFactory("GlobalCounters");
  const globalCounters = await GlobalCounters.deploy(relayerAddress);
  await globalCounters.waitForDeployment();
  const gcAddr = await globalCounters.getAddress();
  console.log(`GlobalCounters deployed: ${gcAddr}`);

  // ── TileNFT ───────────────────────────────────────────────────────────────
  const tileBaseURI = process.env.TILE_BASE_URI ?? "https://api.finitearth.xyz/tile/";
  const TileNFT = await ethers.getContractFactory("TileNFT");
  const tileNft = await TileNFT.deploy(relayerAddress, tileBaseURI);
  await tileNft.waitForDeployment();
  const tileAddr = await tileNft.getAddress();
  console.log(`TileNFT deployed:        ${tileAddr}`);

  // ── ForestToken ───────────────────────────────────────────────────────────
  const ForestToken = await ethers.getContractFactory("ForestToken");
  const forestToken = await ForestToken.deploy(relayerAddress);
  await forestToken.waitForDeployment();
  const frtAddr = await forestToken.getAddress();
  console.log(`ForestToken deployed:    ${frtAddr}`);

  // ── CarbonToken ───────────────────────────────────────────────────────────
  const CarbonToken = await ethers.getContractFactory("CarbonToken");
  const carbonToken = await CarbonToken.deploy(relayerAddress);
  await carbonToken.waitForDeployment();
  const crtAddr = await carbonToken.getAddress();
  console.log(`CarbonToken deployed:    ${crtAddr}\n`);

  // ── Patch backend/gateway/.env ────────────────────────────────────────────
  const envPath = path.join(__dirname, "../../backend/gateway/.env");
  if (fs.existsSync(envPath)) {
    let env = fs.readFileSync(envPath, "utf8");
    env = env.replace(/^MEGAETH_RPC_URL=.*/m, `MEGAETH_RPC_URL=https://6342.rpc.thirdweb.com`);
    env = env.replace(/^GLOBAL_COUNTERS_ADDRESS=.*/m, `GLOBAL_COUNTERS_ADDRESS=${gcAddr}`);
    env = env.replace(/^TILE_NFT_ADDRESS=.*/m,     `TILE_NFT_ADDRESS=${tileAddr}`);
    env = env.replace(/^FOREST_TOKEN_ADDRESS=.*/m, `FOREST_TOKEN_ADDRESS=${frtAddr}`);
    env = env.replace(/^CARBON_TOKEN_ADDRESS=.*/m, `CARBON_TOKEN_ADDRESS=${crtAddr}`);
    fs.writeFileSync(envPath, env);
    console.log("✓ backend/gateway/.env updated with contract addresses.");
  }

  console.log("\nAll contracts deployed. Restart the gateway to activate the chain relay.");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
