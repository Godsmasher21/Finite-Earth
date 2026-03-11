using System;
using UnityEngine;

public class GameStateViewModel : MonoBehaviour
{
    [SerializeField] private string worldId = "finite-earth-alpha";
    [SerializeField, Min(1)] private int cycleSeconds = 30;
    [SerializeField, Min(1)] private int actionsPerCycle = 3;
    [SerializeField, Min(1)] private int settlementRadius = 3;
    [SerializeField] private bool requireAdjacency = true;
    [SerializeField] private bool requireSettlementRadius = true;
    [SerializeField] private string walletAddress = "local-player";

    private long nextClientSeq = 1;

    public WorldState WorldState { get; private set; }
    public PlayerState PlayerState { get; private set; }

    public event Action<ActionResolution> ResolutionApplied;

    public void Initialize(IResolverWorldQuery worldQuery, FiniteEarthResourcePool startingResources)
    {
        WorldState = new WorldState
        {
            worldId = worldId,
            tick = 0,
            cycleSeconds = cycleSeconds,
            actionsPerCycle = actionsPerCycle,
            settlementRadius = settlementRadius,
            requireAdjacency = requireAdjacency,
            requireSettlementRadius = requireSettlementRadius,
            query = worldQuery,
            globalForestToken = worldQuery != null ? worldQuery.CountTilesOfType(TileType.Forest) : 0,
            globalCarbonToken = worldQuery != null ? worldQuery.CalculateCarbonScore() : 0
        };

        WorldState.initialForest = WorldState.globalForestToken;
        int initialCarbon = WorldState.globalCarbonToken;
        WorldState.carbonCap = Mathf.Max(1, Mathf.RoundToInt(initialCarbon * 1.25f));
        WorldState.ecosystemScore = ComputeEcosystemScore(WorldState.globalForestToken, WorldState.globalCarbonToken, WorldState.initialForest, WorldState.carbonCap);

        PlayerState = new PlayerState
        {
            walletAddress = walletAddress,
            actionsRemaining = actionsPerCycle,
            ownedTilesCount = worldQuery != null ? worldQuery.GetOwnedCount(walletAddress) : 0,
            resources = startingResources
        };
    }

    public static int ComputeEcosystemScore(int forest, int carbon, int forestMax, int carbonCap)
    {
        float safeForestMax = Mathf.Max(1f, forestMax);
        float safeCarbonCap = Mathf.Max(1f, carbonCap);
        float f = Mathf.Clamp01(forest / safeForestMax);
        float c = Mathf.Clamp01(carbon / safeCarbonCap);
        float score = 100f * (0.65f * f + 0.35f * (1f - c));
        return Mathf.RoundToInt(Mathf.Clamp(score, 0f, 100f));
    }

    public void SetWalletAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        walletAddress = address;

        if (PlayerState != null)
        {
            PlayerState.walletAddress = address;
        }
    }

    public ActionIntent BuildIntent(FiniteEarthActionType actionType, HexCoord coord, BuildingType buildingType = BuildingType.None)
    {
        long issuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string intentId = $"{walletAddress}-{nextClientSeq}-{issuedAtMs}";

        ActionIntent intent = new ActionIntent(
            intentId,
            worldId,
            walletAddress,
            nextClientSeq,
            actionType,
            coord.q,
            coord.r,
            buildingType,
            issuedAtMs);

        nextClientSeq++;
        return intent;
    }

    public void ApplyResolution(ActionResolution resolution, IResolverWorldMutation mutation)
    {
        ResolutionApplier.Apply(resolution, WorldState, PlayerState, mutation);
        ResolutionApplied?.Invoke(resolution);
    }

    public void StartNewCycle(int authoritativeTick = -1)
    {
        if (WorldState == null || PlayerState == null)
        {
            return;
        }

        if (authoritativeTick >= 0)
        {
            WorldState.tick = authoritativeTick;
        }
        else
        {
            WorldState.tick++;
        }

        PlayerState.actionsRemaining = WorldState.actionsPerCycle;
    }
}
