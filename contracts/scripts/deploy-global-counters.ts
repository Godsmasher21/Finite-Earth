import * as dotenv from "dotenv";
import fs from "node:fs";
import path from "node:path";
import { ethers } from "hardhat";

dotenv.config();

async function main(): Promise<void> {
  const [deployer] = await ethers.getSigners();
  const owner = process.env.GLOBAL_COUNTERS_OWNER ?? deployer.address;
  const updater = process.env.GLOBAL_COUNTERS_UPDATER ?? deployer.address;

  console.log(`[deploy] deployer=${deployer.address}`);
  console.log(`[deploy] owner=${owner}`);
  console.log(`[deploy] updater=${updater}`);

  const factory = await ethers.getContractFactory("GlobalCounters");
  const contract = await factory.deploy(owner, updater);
  await contract.waitForDeployment();

  const address = await contract.getAddress();
  const deployTx = contract.deploymentTransaction();
  const receipt = deployTx ? await deployTx.wait(1) : null;

  const network = await ethers.provider.getNetwork();
  const metadata = {
    name: "GlobalCounters",
    address,
    chainId: Number(network.chainId),
    deployBlock: receipt?.blockNumber ?? 0,
    deployTxHash: receipt?.hash ?? "",
    abiVersion: "1.0.0",
    deployedAtIso: new Date().toISOString()
  };

  const outputDir = path.join(process.cwd(), "deployments");
  fs.mkdirSync(outputDir, { recursive: true });
  fs.writeFileSync(
    path.join(outputDir, `global-counters-${metadata.chainId}.json`),
    JSON.stringify(metadata, null, 2),
    "utf-8"
  );

  console.log(`[deploy] contract=${address}`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
