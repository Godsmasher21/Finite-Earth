using System;

[Serializable]
public sealed class AuthNonceRequest
{
    public string walletAddress;
    public long chainId;
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
}

[Serializable]
public sealed class ActionIntentSubmitMessage
{
    public string type = "ActionIntentSubmit";
    public ActionIntent intent;
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
    public int ownedTilesCount;
    public int sustainabilityScore;
    public int actionsTaken;
    public int actionsRemaining;
    public long lastClientSeq;
}

[Serializable]
public sealed class CycleStartedMessage
{
    public string type = "CycleStarted";
    public int tick;
    public long startedAtUnixMs;
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
