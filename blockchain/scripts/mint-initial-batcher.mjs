import { ethers } from "ethers";

const provider = new ethers.JsonRpcProvider(
  "https://6342.rpc.thirdweb.com",
  { chainId: 6343, name: "megaeth" },
  { staticNetwork: true }
);
provider.getFeeData = async () => {
  const gp = await provider.send("eth_gasPrice", []);
  return new ethers.FeeData(BigInt(gp), null, null);
};
const signer = new ethers.Wallet(process.env.RELAYER_PRIVATE_KEY, provider);
const batcher = new ethers.Contract(
  process.env.TOKEN_SYNC_BATCHER_ADDRESS,
  ["function mintInitial(int256 forestTotal, int256 carbonTotal, address relayAddr) external"],
  signer
);

const FOREST = BigInt(process.env.FOREST_TOTAL ?? "4363");
const CARBON = BigInt(process.env.CARBON_TOTAL ?? "22281");

console.log(`Minting FRT=${FOREST} CRT=${CARBON} via batcher (1 tx)...`);
const tx = await batcher.mintInitial(FOREST, CARBON, signer.address);
console.log("tx:", tx.hash);
await tx.wait();
console.log("✓ Initial supply bootstrapped.");
