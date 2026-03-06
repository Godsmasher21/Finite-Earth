/* eslint-disable no-console */

type EthereumProvider = {
  request(args: { method: string; params?: unknown[] }): Promise<unknown>;
};

type LoginStrategy = "injected" | "google" | "email";

type FiniteEarthBridgeApi = {
  connectWallet(strategy?: string): Promise<string>;
  requestSiweMessage(walletAddress: string, nonce: string, domain: string): Promise<string>;
  signMessage(message: string): Promise<string>;
  getActiveAddress(): Promise<string>;
};

type ThirdwebInAppAccount = {
  address?: string;
  signMessage?: (payload: { message: string } | string) => Promise<string>;
};

type ThirdwebInAppWallet = {
  connect(args: Record<string, unknown>): Promise<ThirdwebInAppAccount | undefined>;
  getAccount?: () => ThirdwebInAppAccount | undefined;
};

declare global {
  interface Window {
    ethereum?: EthereumProvider;
    FiniteEarthBridge?: FiniteEarthBridgeApi;
    __finiteEarthThirdwebClient?: unknown;
  }
}

let activeAddress = "";
let activeSigner: ((message: string) => Promise<string>) | null = null;

function resolveChainId(): number {
  const raw = (globalThis as any).FINITE_EARTH_CHAIN_ID;
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed > 0 ? Math.trunc(parsed) : 6342;
}

function normalizeStrategy(strategy: string | undefined): LoginStrategy {
  const normalized = (strategy ?? "google").trim().toLowerCase();
  if (normalized === "google" || normalized === "email" || normalized === "injected") {
    return normalized;
  }

  return "google";
}

async function ensureThirdwebClient(): Promise<unknown> {
  if (window.__finiteEarthThirdwebClient) {
    return window.__finiteEarthThirdwebClient;
  }

  const thirdweb: any = await import("thirdweb");
  const clientId = (globalThis as any).FINITE_EARTH_THIRDWEB_CLIENT_ID ?? "";
  if (!clientId) {
    throw new Error("FINITE_EARTH_THIRDWEB_CLIENT_ID is missing.");
  }

  window.__finiteEarthThirdwebClient = thirdweb.createThirdwebClient({ clientId });
  return window.__finiteEarthThirdwebClient;
}

function getEthereumProvider(): EthereumProvider {
  if (!window.ethereum) {
    throw new Error("No injected wallet provider found.");
  }

  return window.ethereum;
}

async function connectInjectedWallet(): Promise<string> {
  const provider = getEthereumProvider();
  const accounts = await provider.request({ method: "eth_requestAccounts" }) as string[];

  if (!accounts || accounts.length === 0) {
    throw new Error("Wallet did not provide any accounts.");
  }

  activeAddress = accounts[0];
  activeSigner = null;
  return activeAddress;
}

function buildInAppSigner(wallet: ThirdwebInAppWallet, account: ThirdwebInAppAccount | undefined): (message: string) => Promise<string> {
  return async (message: string) => {
    const active = account ?? wallet.getAccount?.();
    if (!active || typeof active.signMessage !== "function") {
      throw new Error("Connected in-app wallet account cannot sign messages.");
    }

    try {
      return await active.signMessage({ message });
    } catch {
      return await active.signMessage(message);
    }
  };
}

async function connectInAppWallet(strategy: Exclude<LoginStrategy, "injected">): Promise<string> {
  const client = await ensureThirdwebClient();
  const wallets: any = await import("thirdweb/wallets");
  if (typeof wallets.inAppWallet !== "function") {
    throw new Error("thirdweb/wallets.inAppWallet is unavailable in this bundle.");
  }

  const wallet = wallets.inAppWallet({
    auth: {
      options: ["google", "email"]
    }
  }) as ThirdwebInAppWallet;

  const account = await wallet.connect({
    client,
    strategy
  });

  const resolvedAccount = account ?? wallet.getAccount?.();
  const address = resolvedAccount?.address;
  if (!address) {
    throw new Error("In-app wallet did not return an address.");
  }

  activeAddress = address;
  activeSigner = buildInAppSigner(wallet, resolvedAccount);
  return address;
}

async function connectWallet(strategy?: string): Promise<string> {
  const mode = normalizeStrategy(strategy);
  if (mode === "injected") {
    return connectInjectedWallet();
  }

  return connectInAppWallet(mode);
}

async function getActiveAddress(): Promise<string> {
  if (activeAddress) {
    return activeAddress;
  }

  const provider = getEthereumProvider();
  const accounts = await provider.request({ method: "eth_accounts" }) as string[];
  if (!accounts || accounts.length === 0) {
    throw new Error("No active wallet account.");
  }

  activeAddress = accounts[0];
  return activeAddress;
}

async function requestSiweMessage(walletAddress: string, nonce: string, domain: string): Promise<string> {
  await ensureThirdwebClient();
  const now = new Date().toISOString();
  const chainId = resolveChainId();

  return [
    `${domain} wants you to sign in with your Ethereum account:`,
    walletAddress,
    "",
    "Finite Earth wallet authentication",
    "",
    `URI: https://${domain}`,
    "Version: 1",
    `Chain ID: ${chainId}`,
    `Nonce: ${nonce}`,
    `Issued At: ${now}`
  ].join("\n");
}

async function signMessage(message: string): Promise<string> {
  if (activeSigner) {
    return activeSigner(message);
  }

  const provider = getEthereumProvider();
  const address = await getActiveAddress();
  const signature = await provider.request({
    method: "personal_sign",
    params: [message, address]
  }) as string;

  if (!signature) {
    throw new Error("Wallet signature failed.");
  }

  return signature;
}

window.FiniteEarthBridge = {
  connectWallet,
  requestSiweMessage,
  signMessage,
  getActiveAddress
};

console.info("[FiniteEarthBridge] Thirdweb bridge initialized (google/email/injected).");

export {};
