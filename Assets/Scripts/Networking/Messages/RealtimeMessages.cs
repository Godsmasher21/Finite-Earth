using System;

[Serializable]
public sealed class AuthNonceRequest
{
    public string walletAddress;
    public long chainId;
}

[Serializable]
public sealed class CredentialLoginRequest
{
    public string username;
    public string password;
}

[Serializable]
public sealed class CredentialSignupRequest
{
    public string username;
    public string password;
    public string confirmPassword;
}

[Serializable]
public sealed class AuthNonceResponse
{
    public string nonce;
    public long expiresAtUnixMs;
}

[Serializable]
public sealed class AuthVerifyRequest
{
    public string message;
    public string signature;
    public string nonce;
}

[Serializable]
public sealed class AuthVerifyResponse
{
    public string accessToken;
    public long expiresAtUnixMs;
    public string walletAddress;
    public string username;
    public string displayName;
    public string authMode;
}

[Serializable]
public sealed class GatewayErrorResponse
{
    public string error;
}

[Serializable]
public sealed class GatewayHealthResponse
{
    public bool ok;
    public bool spacetimeReady;
}

[Serializable]
public sealed class ActionIntentSubmitMessage
{
    public string type = "ActionIntentSubmit";
    public ActionIntent intent;
}

[Serializable]
public sealed class ActionIntentBatchSubmitMessage
{
    public string type = "ActionIntentBatchSubmit";
    public ActionIntent[] intents;
}

[Serializable]
public sealed class ActionCommittedMessage
{
    public string type = "ActionCommitted";
    public string commitId;
    public int tick;
    public string intentId;
    public bool accepted;
    public string reason;
    public string walletAddress;
    public int actionType;
    public int q;
    public int r;
    public TileDelta[] tileDeltas;
    public PlayerDelta playerDelta;
    public GlobalDelta globalDelta;
    public string batchHash;
}

[Serializable]
public sealed class WorldSnapshotMessage
{
    public string type = "WorldSnapshot";
    public string worldId;
    public int tick;
    public int globalForestToken;
    public int globalCarbonToken;
    public int cycleSeconds;
    public int actionsPerCycle;
    public WorldTileSnapshotMessage[] tiles;
    public WorldPlayerSnapshotMessage[] players;
    public ClimateEventSnapshotMessage[] climateEvents;
}

[Serializable]
public sealed class WorldTileSnapshotMessage
{
    public int q;
    public int r;
    public string currentState;
    public string ownerWallet;
    public string buildingType;
    public int lastUpdatedTick;
}

[Serializable]
public sealed class WorldPlayerSnapshotMessage
{
    public string walletAddress;
    public string username;
    public string displayName;
    public int ownedTilesCount;
    public int sustainabilityScore;
    public int actionsTaken;
    public int actionsRemaining;
    public long lastClientSeq;
    public int wood;
    public int food;
    public int minerals;
    public int researchPoints;
    public bool techBasicForestry;
    public bool techRenewableEnergy;
    public bool techCarbonCapture;
    public int ecoActions;
    public int industrialActions;
    public int agricultureActions;
    public string reputation;
}

[Serializable]
public sealed class ClimateEventSnapshotMessage
{
    public long id;
    public int type;
    public int startTick;
    public int endTick;
}

[Serializable]
public sealed class CycleStartedMessage
{
    public string type = "CycleStarted";
    public int tick;
    public long startedAtUnixMs;
    public int globalForestToken;
    public int globalCarbonToken;
    public WorldPlayerSnapshotMessage player;
    public ClimateEventSnapshotMessage[] climateEvents;
}

[Serializable]
public sealed class CycleCommittedToChainMessage
{
    public string type = "CycleCommittedToChain";
    public int tick;
    public long cycleId;
    public int forestDelta;
    public int carbonDelta;
    public string transactionHash;
}

// Broadcast to all clients when any player's action changes tiles.
[Serializable]
public sealed class RemoteTileChangedMessage
{
    public string type = "RemoteTileChanged";
    public string walletAddress;
    public int tick;
    public TileDelta[] tileDeltas;
}

// Sent when a new player connects to the session.
[Serializable]
public sealed class PlayerJoinedMessage
{
    public string type = "PlayerJoined";
    public string walletAddress;
    public string username;
    public string displayName;
    public int tick;
}

// Broadcast when a TileNFT batch is minted on-chain.
[Serializable]
public sealed class TileNFTMintedMessage
{
    public string type = "TileNFTMinted";
    public string transactionHash;
    public int tileCount;
}

// Sent when a player disconnects from the session.
[Serializable]
public sealed class PlayerLeftMessage
{
    public string type = "PlayerLeft";
    public string walletAddress;
    public string username;
    public string displayName;
}
