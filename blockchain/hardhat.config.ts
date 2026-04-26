import { HardhatUserConfig } from "hardhat/config";
import "@nomicfoundation/hardhat-toolbox";
import "dotenv/config";

const RELAYER_KEY  = process.env.RELAYER_PRIVATE_KEY  ?? "0x0000000000000000000000000000000000000000000000000000000000000001";
const MEGAETH_RPC  = process.env.MEGAETH_RPC_URL       ?? "https://6342.rpc.thirdweb.com";

const config: HardhatUserConfig = {
  solidity: {
    compilers: [
      {
        version: "0.8.24",
        settings: {
          optimizer: { enabled: true, runs: 200 },
          evmVersion: "cancun",
        },
      },
    ],
  },
  networks: {
    megaeth: {
      url: MEGAETH_RPC,
      chainId: 6342,
      accounts: [RELAYER_KEY],
    },
    // Local fork for testing without spending gas
    hardhat: {
      forking: {
        url: MEGAETH_RPC,
        enabled: process.env.FORK === "true",
      },
    },
  },
  paths: {
    sources:   "./contracts",
    tests:     "./test",
    cache:     "./cache",
    artifacts: "./artifacts",
  },
};

export default config;
