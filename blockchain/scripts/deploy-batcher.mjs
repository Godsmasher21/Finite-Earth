// Deploy TokenSyncBatcher, then set it as relay on FRT and CRT.
import { ethers } from "ethers";
import { readFileSync, writeFileSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const KEY = process.env.RELAYER_PRIVATE_KEY;
const RPC = process.env.MEGAETH_RPC_URL ?? "https://6342.rpc.thirdweb.com";
const FRT = process.env.FOREST_TOKEN_ADDRESS;
const CRT = process.env.CARBON_TOKEN_ADDRESS;

if (!KEY || !FRT || !CRT) { console.error("Set RELAYER_PRIVATE_KEY, FOREST_TOKEN_ADDRESS, CARBON_TOKEN_ADDRESS"); process.exit(1); }

async function main() {
  const provider = new ethers.JsonRpcProvider(RPC, { chainId: 6343, name: "megaeth" }, { staticNetwork: true });
  provider.getFeeData = async () => {
    const gp = await provider.send("eth_gasPrice", []);
    return new ethers.FeeData(BigInt(gp), null, null);
  };
  const signer = new ethers.Wallet(KEY, provider);
  console.log(`Deployer: ${signer.address}\n`);

  const artifact = JSON.parse(readFileSync(
    join(__dirname, "../artifacts/contracts/TokenSyncBatcher.sol/TokenSyncBatcher.json"), "utf8"
  ));
  const factory = new ethers.ContractFactory(artifact.abi, artifact.bytecode, signer);
  process.stdout.write("Deploying TokenSyncBatcher... ");
  const batcher = await factory.deploy(FRT, CRT);
  await batcher.waitForDeployment();
  const batcherAddr = await batcher.getAddress();
  console.log(batcherAddr);

  // Set batcher as relay on both tokens.
  const setRelayAbi = ["function setRelay(address _relay) external"];
  const frtContract = new ethers.Contract(FRT, setRelayAbi, signer);
  const crtContract = new ethers.Contract(CRT, setRelayAbi, signer);

  process.stdout.write("Setting batcher as FRT relay... ");
  const tx1 = await frtContract.setRelay(batcherAddr);
  await tx1.wait();
  console.log("done");

  process.stdout.write("Setting batcher as CRT relay... ");
  const tx2 = await crtContract.setRelay(batcherAddr);
  await tx2.wait();
  console.log("done");

  // Update .env
  const envPath = join(__dirname, "../../backend/gateway/.env");
  let env = readFileSync(envPath, "utf8");
  // Add or update TOKEN_SYNC_BATCHER_ADDRESS
  if (env.includes("TOKEN_SYNC_BATCHER_ADDRESS=")) {
    env = env.replace(/^TOKEN_SYNC_BATCHER_ADDRESS=.*/m, `TOKEN_SYNC_BATCHER_ADDRESS=${batcherAddr}`);
  } else {
    env += `\nTOKEN_SYNC_BATCHER_ADDRESS=${batcherAddr}\n`;
  }
  writeFileSync(envPath, env);

  console.log(`\n✓ TokenSyncBatcher deployed at ${batcherAddr}`);
  console.log("Add TOKEN_SYNC_BATCHER_ADDRESS to Railway Variables.");
}

main().catch(e => { console.error(e); process.exit(1); });
