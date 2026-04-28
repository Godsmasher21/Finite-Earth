// Redeploy only TileNFT (adds claimTileBatch). Other contracts unchanged.
import { ethers } from "ethers";
import { readFileSync, writeFileSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const artifactsDir = join(__dirname, "../artifacts/contracts");

const RPC = process.env.MEGAETH_RPC_URL ?? "https://6342.rpc.thirdweb.com";
const KEY = process.env.RELAYER_PRIVATE_KEY;
if (!KEY) { console.error("Set RELAYER_PRIVATE_KEY"); process.exit(1); }

async function main() {
  const provider = new ethers.JsonRpcProvider(
    RPC,
    { chainId: 6343, name: "megaeth" },
    { staticNetwork: true }
  );
  provider.getFeeData = async () => {
    const gasPrice = await provider.send("eth_gasPrice", []);
    return new ethers.FeeData(BigInt(gasPrice), null, null);
  };
  const signer = new ethers.Wallet(KEY, provider);
  console.log(`\nDeployer: ${signer.address}`);
  console.log(`Balance:  ${ethers.formatEther(await provider.getBalance(signer.address))} ETH\n`);

  const artifact = JSON.parse(readFileSync(
    join(artifactsDir, "TileNFT.sol", "TileNFT.json"), "utf8"
  ));
  const factory = new ethers.ContractFactory(artifact.abi, artifact.bytecode, signer);
  process.stdout.write("Deploying TileNFT... ");
  const contract = await factory.deploy(signer.address, "https://api.finitearth.xyz/tile/");
  await contract.waitForDeployment();
  const addr = await contract.getAddress();
  console.log(addr);

  // Update backend/gateway/.env
  const envPath = join(__dirname, "../../backend/gateway/.env");
  let env = readFileSync(envPath, "utf8");
  env = env.replace(/^TILE_NFT_ADDRESS=.*/m, `TILE_NFT_ADDRESS=${addr}`);
  writeFileSync(envPath, env);

  console.log("\n✓ backend/gateway/.env updated with new TileNFT address.");
  console.log(`Update TILE_NFT_ADDRESS in Railway to: ${addr}`);
}

main().catch(e => { console.error(e); process.exit(1); });
