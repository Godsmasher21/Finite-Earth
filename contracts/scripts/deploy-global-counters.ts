import * as dotenv from "dotenv";
import fs from "node:fs";
import path from "node:path";
import { ethers } from "hardhat";

dotenv.config();

async function main(): Promise<void> {
  const [deployer] = await ethers.getSigners();
  const owner = process.env.GLOBAL_TOKENS_OWNER ?? deployer.address;
  const operator = process.env.GLOBAL_TOKENS_OPERATOR ?? deployer.address;

  console.log(`[deploy] deployer=${deployer.address}`);
  console.log(`[deploy] owner=${owner}`);
  console.log(`[deploy] operator=${operator}`);

  const forestFactory = await ethers.getContractFactory("GlobalForestToken");
  const forest = await forestFactory.deploy(owner, operator);
  await forest.waitForDeployment();

  const carbonFactory = await ethers.getContractFactory("GlobalCarbonToken");
  const carbon = await carbonFactory.deploy(owner, operator);
  await carbon.waitForDeployment();

  const tilesFactory = await ethers.getContractFactory("TilesOwnedSBT");
  const tiles = await tilesFactory.deploy(owner, operator);
  await tiles.waitForDeployment();

  const forestAddress = await forest.getAddress();
  const carbonAddress = await carbon.getAddress();
  const tilesAddress = await tiles.getAddress();

  const forestDeployTx = forest.deploymentTransaction();
  const forestReceipt = forestDeployTx ? await forestDeployTx.wait(1) : null;

  const network = await ethers.provider.getNetwork();
  const metadata = {
    name: "FiniteEarthGlobalTokens",
    forestToken: forestAddress,
    carbonToken: carbonAddress,
    tilesOwnedSbt: tilesAddress,
    chainId: Number(network.chainId),
    deployBlock: forestReceipt?.blockNumber ?? 0,
    deployTxHash: forestReceipt?.hash ?? "",
    abiVersion: "1.0.0",
    deployedAtIso: new Date().toISOString()
  };

  const outputDir = path.join(process.cwd(), "deployments");
  fs.mkdirSync(outputDir, { recursive: true });
  fs.writeFileSync(
    path.join(outputDir, `global-tokens-${metadata.chainId}.json`),
    JSON.stringify(metadata, null, 2),
    "utf-8"
  );

  console.log(`[deploy] forest=${forestAddress}`);
  console.log(`[deploy] carbon=${carbonAddress}`);
  console.log(`[deploy] tiles=${tilesAddress}`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
