using System;

[Serializable]
public sealed class LeaderboardResponseMessage
{
    public string worldId;
    public int total;
    public int limit;
    public int offset;
    public LeaderboardEntryMessage[] players;
}

[Serializable]
public sealed class LeaderboardEntryMessage
{
    public int rank;
    public string wallet_address;
    public int sustainability_score;
    public int actions_taken;
    public int owned_tiles_count;
    public long updated_at_ms;
    public string username;
    public string displayName;
}
