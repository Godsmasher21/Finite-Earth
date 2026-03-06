using System;

public static class ResolutionApplier
{
    public static void Apply(ActionResolution resolution, WorldState worldState, PlayerState playerState, IResolverWorldMutation worldMutation)
    {
        if (!resolution.accepted || worldState == null || playerState == null)
        {
            return;
        }

        if (resolution.tileDeltas != null && worldMutation != null)
        {
            for (int i = 0; i < resolution.tileDeltas.Length; i++)
            {
                TileDelta delta = resolution.tileDeltas[i];
                HexCoord coord = delta.Coord;

                if (delta.nextTerrain != delta.previousTerrain)
                {
                    worldMutation.TrySetTileType(coord, delta.nextTerrain);
                }

                if (delta.nextBuilding != delta.previousBuilding)
                {
                    worldMutation.TrySetBuildingType(coord, delta.nextBuilding);
                }

                if (delta.ownerChanged)
                {
                    worldMutation.SetOwned(coord, delta.ownerWallet, true);
                }
            }
        }

        bool isCurrentPlayerDelta = string.IsNullOrWhiteSpace(resolution.playerDelta.walletAddress)
            || string.Equals(resolution.playerDelta.walletAddress, playerState.walletAddress, StringComparison.OrdinalIgnoreCase);

        if (isCurrentPlayerDelta)
        {
            playerState.ownedTilesCount = Math.Max(0, playerState.ownedTilesCount + resolution.playerDelta.ownedTilesDelta);
            playerState.sustainabilityScore += resolution.playerDelta.sustainabilityScoreDelta;
            playerState.actionsTaken += resolution.playerDelta.actionsTakenDelta;
            playerState.actionsRemaining = Math.Max(0, playerState.actionsRemaining + resolution.playerDelta.actionsRemainingDelta);
            playerState.resources.Add(resolution.playerDelta.resourceDelta);
        }

        worldState.globalForestToken += resolution.globalDelta.forestDelta;
        worldState.globalCarbonToken += resolution.globalDelta.carbonDelta;
    }
}
