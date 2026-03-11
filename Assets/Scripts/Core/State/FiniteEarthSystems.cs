using System;

public enum ClimateEventType
{
    Heatwave = 0,
    Wildfire = 1,
    Flood = 2,
    IceMelt = 3,
    DesertSpread = 4
}

public sealed class ClimateEventInstance
{
    public ClimateEventType type;
    public int startTick;
    public int endTick;
}

public sealed class ClimateTileHighlight
{
    public ClimateEventType type;
    public HexCoord coord;
    public int endTick;
}

public enum DiplomacyPactType
{
    NonAggression = 0,
    ResourceShare = 1
}

public enum DiplomacyPactStatus
{
    Pending = 0,
    Active = 1,
    Canceled = 2
}

public sealed class DiplomacyPact
{
    public string id;
    public string walletA;
    public string walletB;
    public DiplomacyPactType type;
    public DiplomacyPactStatus status;
    public int createdTick;
}

public enum TradeOfferStatus
{
    Open = 0,
    Accepted = 1,
    Canceled = 2,
    Expired = 3
}

public sealed class TradeOffer
{
    public string id;
    public string ownerWallet;
    public FiniteEarthResourcePool give;
    public FiniteEarthResourcePool want;
    public TradeOfferStatus status;
    public int createdTick;
    public int expiresTick;
    public string acceptedBy;
}

public sealed class ArmyUnit
{
    public string id;
    public string ownerWallet;
    public HexCoord coord;
    public int strength;
    public float lastMoveAt;
}
