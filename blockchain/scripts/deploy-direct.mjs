// Direct ethers deployment — bypasses Hardhat's chain-ID lookup issue with MegaETH.
import { ethers } from "ethers";
import { readFileSync, writeFileSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const artifactsDir = join(__dirname, "../artifacts/contracts");

const RPC = process.env.MEGAETH_RPC_URL ?? "https://6342.rpc.thirdweb.com";
const KEY = process.env.RELAYER_PRIVATE_KEY;
if (!KEY || KEY === "0x") { console.error("Set RELAYER_PRIVATE_KEY"); process.exit(1); }

function loadArtifact(contractName) {
  const p = join(artifactsDir, `${contractName}.sol`, `${contractName}.json`);
  const a = JSON.parse(readFileSync(p, "utf8"));
  return { abi: a.abi, bytecode: a.bytecode };
}

async function deploy(provider, signer, name, args) {
  const { abi, bytecode } = loadArtifact(name);
  const factory = new ethers.ContractFactory(abi, bytecode, signer);
  process.stdout.write(`Deploying ${name}... `);
  const contract = await factory.deploy(...args);
  await contract.waitForDeployment();
  const addr = await contract.getAddress();
  console.log(addr);
  return addr;
}

async function main() {
  // MegaETH quirk: eth_chainId returns 6342 but EIP-155 signing uses 6343.
  // staticNetwork disables auto-detection so ethers signs with our hardcoded chainId.
  const SIGNING_CHAIN_ID = 6343;
  const provider = new ethers.JsonRpcProvider(
    RPC,
    { chainId: SIGNING_CHAIN_ID, name: "megaeth" },
    { staticNetwork: true }
  );
  // Force legacy type-0 transactions — MegaETH testnet rejects EIP-1559 type-2.
  provider.getFeeData = async () => {
    const gasPrice = await provider.send("eth_gasPrice", []);
    return new ethers.FeeData(BigInt(gasPrice), null, null);
  };
  const signer = new ethers.Wallet(KEY, provider);
  const balance  = await provider.getBalance(signer.address);

  console.log(`\nMegaETH (chain 6342) @ ${RPC}`);
  console.log(`Deployer: ${signer.address}`);
  console.log(`Balance:  ${ethers.formatEther(balance)} ETH\n`);

  if (balance === 0n) {
    console.error("No ETH — fund the wallet first."); process.exit(1);
  }

  const gcAddr   = await deploy(provider, signer, "GlobalCounters", [signer.address]);
  const tileAddr = await deploy(provider, signer, "TileNFT", [signer.address, "https://api.finitearth.xyz/tile/"]);
  const frtAddr  = await deploy(provider, signer, "ForestToken",    [signer.address]);
  const crtAddr  = await deploy(provider, signer, "CarbonToken",    [signer.address]);

  // Patch backend/gateway/.env
  const envPath = join(__dirname, "../../backend/gateway/.env");
  let env = readFileSync(envPath, "utf8");
  env = env.replace(/^MEGAETH_RPC_URL=.*/m,         `MEGAETH_RPC_URL=${RPC}`);
  env = env.replace(/^GLOBAL_COUNTERS_ADDRESS=.*/m, `GLOBAL_COUNTERS_ADDRESS=${gcAddr}`);
  env = env.replace(/^TILE_NFT_ADDRESS=.*/m,        `TILE_NFT_ADDRESS=${tileAddr}`);
  env = env.replace(/^FOREST_TOKEN_ADDRESS=.*/m,    `FOREST_TOKEN_ADDRESS=${frtAddr}`);
  env = env.replace(/^CARBON_TOKEN_ADDRESS=.*/m,    `CARBON_TOKEN_ADDRESS=${crtAddr}`);
  writeFileSync(envPath, env);

  console.log("\n✓ backend/gateway/.env updated.");
  console.log("Restart the gateway to activate the chain relay.");
}

main().catch(e => { console.error(e); process.exit(1); });
