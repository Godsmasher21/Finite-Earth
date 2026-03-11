using System;
using SpacetimeDB;

public static partial class Module
{
    [Table(Name = "world_state", Public = true)]
    public partial struct WorldStateRow
    {
        [PrimaryKey]
        public string world_id;
        public long tick;
        public long cycle;
        public long rng_state;
        public int forest_total;
        public int carbon_total;
        public int initial_forest;
        public int carbon_cap;
        public string active_events;
    }

    [Table(Name = "tiles", Public = true)]
    public partial struct TileRow
    {
        [PrimaryKey]
        public long id;
        public int q;
        public int r;
        public int terrain;
        public int building;
        public string owner;
        public int mining_count;
        public long last_update;
    }

    [Table(Name = "players", Public = true)]
    public partial struct PlayerRow
    {
        [PrimaryKey]
        public string wallet;
        public int wood;
        public int food;
        public int minerals;
        public int research_points;
        public int owned_tiles;
        public int actions_taken;
        public int eco_actions;
        public int industrial_actions;
        public int agriculture_actions;
        public int tech_basic_forestry;
        public int tech_renewable_energy;
        public int tech_carbon_capture;
        public string reputation;
    }

    [Table(Name = "armies", Public = true)]
    public partial struct ArmyRow
    {
        [PrimaryKey]
        [AutoInc]
        public ulong id;
        public string owner;
        public int q;
        public int r;
        public long last_move_ms;
    }

    [Table(Name = "pressure", Public = true)]
    public partial struct PressureRow
    {
        [PrimaryKey]
        [AutoInc]
        public ulong id;
        public long tile_id;
        public string wallet;
        public int pressure_value;
    }

    [Table(Name = "trade_offers", Public = true)]
    public partial struct TradeOfferRow
    {
        [PrimaryKey]
        [AutoInc]
        public ulong id;
        public string owner;
        public int give_wood;
        public int give_food;
        public int give_minerals;
        public int want_wood;
        public int want_food;
        public int want_minerals;
        public int status;
        public long expires_tick;
        public string accepted_by;
    }

    [Table(Name = "pacts", Public = true)]
    public partial struct PactRow
    {
        [PrimaryKey]
        [AutoInc]
        public ulong id;
        public int type;
        public string wallet_a;
        public string wallet_b;
        public int status;
        public long created_at;
    }

    [Table(Name = "climate_events", Public = true)]
    public partial struct ClimateEventRow
    {
        [PrimaryKey]
        [AutoInc]
        public ulong id;
        public int type;
        public long start_tick;
        public long end_tick;
    }

    [Reducer]
    public static void submit_intent(ReducerContext ctx, string world_id, string wallet, int action_type, int q, int r)
    {
        // TODO: Authoritative rule validation + tile/resource updates.
        // This reducer is the single entry point for gameplay actions.
        Log.Info($"submit_intent received {action_type} at {q},{r} for {wallet}.");
    }

    [Reducer]
    public static void advance_cycle(ReducerContext ctx, string world_id)
    {
        // TODO: Apply passive income, tech bonuses, climate events, pressure decay/capture, and pact sharing.
        Log.Info($"advance_cycle tick for {world_id}.");
    }

    [Reducer]
    public static void trade_create(ReducerContext ctx, string owner, int give_wood, int give_food, int give_minerals, int want_wood, int want_food, int want_minerals, long expires_tick)
    {
        ctx.Db.trade_offers.Insert(new TradeOfferRow
        {
            owner = owner,
            give_wood = give_wood,
            give_food = give_food,
            give_minerals = give_minerals,
            want_wood = want_wood,
            want_food = want_food,
            want_minerals = want_minerals,
            status = 0,
            expires_tick = expires_tick,
            accepted_by = string.Empty
        });
    }

    [Reducer]
    public static void trade_accept(ReducerContext ctx, ulong offer_id, string accepter)
    {
        // TODO: Apply resource exchange + set status.
        Log.Info($"trade_accept {offer_id} by {accepter}");
    }

    [Reducer]
    public static void trade_cancel(ReducerContext ctx, ulong offer_id, string owner)
    {
        // TODO: Refund escrow + set status.
        Log.Info($"trade_cancel {offer_id} by {owner}");
    }

    [Reducer]
    public static void pact_create(ReducerContext ctx, int type, string wallet_a, string wallet_b)
    {
        ctx.Db.pacts.Insert(new PactRow
        {
            type = type,
            wallet_a = wallet_a,
            wallet_b = wallet_b,
            status = 0,
            created_at = ctx.Timestamp.Millis
        });
    }

    [Reducer]
    public static void pact_accept(ReducerContext ctx, ulong pact_id, string accepter)
    {
        Log.Info($"pact_accept {pact_id} by {accepter}");
    }

    [Reducer]
    public static void pact_cancel(ReducerContext ctx, ulong pact_id, string canceler)
    {
        Log.Info($"pact_cancel {pact_id} by {canceler}");
    }

    [Reducer]
    public static void army_spawn(ReducerContext ctx, string owner, int q, int r)
    {
        ctx.Db.armies.Insert(new ArmyRow
        {
            owner = owner,
            q = q,
            r = r,
            last_move_ms = ctx.Timestamp.Millis
        });
    }

    [Reducer]
    public static void army_move(ReducerContext ctx, ulong army_id, int q, int r)
    {
        Log.Info($"army_move {army_id} -> {q},{r}");
    }
}
