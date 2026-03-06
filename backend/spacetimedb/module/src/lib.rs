use serde::{Deserialize, Serialize};
use spacetimedb::{reducer, table, Identity, ReducerContext, ScheduleToken, Timestamp};

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq, Eq)]
pub enum IntentStatus {
    Queued,
    Committed,
    Rejected,
}

#[table(name = world_state, public)]
pub struct WorldStateRow {
    #[primary_key]
    pub world_id: String,
    pub tick: u64,
    pub cycle_seconds: u32,
    pub actions_per_cycle: u32,
    pub forest_total: i64,
    pub carbon_total: i64,
    pub last_chain_cycle: u64,
    pub rng_seed: u64,
}

#[table(name = tiles, public)]
pub struct TileRow {
    #[primary_key]
    pub id: String,
    pub world_id: String,
    pub q: i32,
    pub r: i32,
    pub base_type: String,
    pub current_state: String,
    pub owner_wallet: String,
    pub building_type: String,
    pub fertility_bp: i32,
    pub pollution_bp: i32,
    pub biodiversity_bp: i32,
    pub last_updated_tick: u64,
}

#[table(name = players, public)]
pub struct PlayerRow {
    #[primary_key]
    pub id: String,
    pub world_id: String,
    pub wallet: String,
    pub sustainability_score: i64,
    pub actions_taken: u64,
    pub owned_tiles_count: u32,
    pub last_client_seq: u64,
    pub actions_remaining: u32,
}

#[table(name = intent_queue, public)]
pub struct IntentQueueRow {
    #[primary_key]
    pub intent_id: String,
    pub world_id: String,
    pub wallet: String,
    pub client_seq: u64,
    pub action_type: String,
    pub q: i32,
    pub r: i32,
    pub building_type: String,
    pub submitted_at_ms: i64,
    pub status: String,
}

#[table(name = action_commits, public)]
pub struct ActionCommitRow {
    #[primary_key]
    pub commit_id: String,
    pub world_id: String,
    pub tick: u64,
    pub intent_id: String,
    pub accepted: bool,
    pub reason: String,
    pub global_forest_delta: i64,
    pub global_carbon_delta: i64,
    pub batch_hash: String,
    pub committed_at_ms: i64,
}

#[table(name = schedule, public)]
pub struct ScheduleRow {
    #[primary_key]
    pub id: u64,
    pub world_id: String,
    pub next_tick_at: Timestamp,
    pub schedule_token: ScheduleToken,
}

#[reducer(init)]
pub fn init(ctx: &ReducerContext) {
    let world_id = "finite-earth-alpha".to_string();
    if ctx.db.world_state().world_id().find(&world_id).is_some() {
        return;
    }

    ctx.db.world_state().insert(WorldStateRow {
        world_id: world_id.clone(),
        tick: 1,
        cycle_seconds: 30,
        actions_per_cycle: 3,
        forest_total: 0,
        carbon_total: 0,
        last_chain_cycle: 0,
        rng_seed: 1,
    });

    let token = ctx.scheduler.schedule_reducer("advance_cycle", Timestamp::from_micros(30_000_000));
    ctx.db.schedule().insert(ScheduleRow {
        id: 1,
        world_id,
        next_tick_at: Timestamp::from_micros(30_000_000),
        schedule_token: token,
    });
}

#[reducer]
pub fn submit_intent(
    ctx: &ReducerContext,
    world_id: String,
    intent_id: String,
    wallet: String,
    client_seq: u64,
    action_type: String,
    q: i32,
    r: i32,
    building_type: String,
) {
    let Some(_world) = ctx.db.world_state().world_id().find(&world_id) else {
        return;
    };

    if ctx.db.intent_queue().intent_id().find(&intent_id).is_some() {
        return;
    }

    let submitted_at_ms = ctx.timestamp.to_micros_since_unix_epoch() as i64 / 1000;

    let player_id = format!("{world_id}:{wallet}");
    if ctx.db.players().id().find(&player_id).is_none() {
        ctx.db.players().insert(PlayerRow {
            id: player_id.clone(),
            world_id: world_id.clone(),
            wallet: wallet.clone(),
            sustainability_score: 0,
            actions_taken: 0,
            owned_tiles_count: 0,
            last_client_seq: 0,
            actions_remaining: 3,
        });
    }

    let Some(player) = ctx.db.players().id().find(&player_id) else {
        return;
    };

    if client_seq <= player.last_client_seq {
        return;
    }

    ctx.db.players().id().update(player_id, PlayerRow { last_client_seq: client_seq, ..player });

    ctx.db.intent_queue().insert(IntentQueueRow {
        intent_id,
        world_id,
        wallet,
        client_seq,
        action_type,
        q,
        r,
        building_type,
        submitted_at_ms,
        status: "Queued".to_string(),
    });
}

#[reducer]
pub fn advance_cycle(ctx: &ReducerContext) {
    let world_id = "finite-earth-alpha".to_string();
    let Some(mut world) = ctx.db.world_state().world_id().find(&world_id) else {
        return;
    };

    let mut intents = ctx
        .db
        .intent_queue()
        .iter()
        .filter(|intent| intent.world_id == world_id && intent.status == "Queued")
        .collect::<Vec<_>>();

    intents.sort_by(|left, right| {
        left.submitted_at_ms
            .cmp(&right.submitted_at_ms)
            .then(left.wallet.cmp(&right.wallet))
            .then(left.intent_id.cmp(&right.intent_id))
    });

    for intent in intents {
        apply_intent(
            ctx,
            world.world_id.clone(),
            world.tick,
            intent.intent_id.clone(),
            intent.wallet.clone(),
            intent.action_type.clone(),
        );
    }

    world.tick += 1;
    ctx.db.world_state().world_id().update(world_id.clone(), world);

    let token = ctx.scheduler.schedule_reducer("advance_cycle", Timestamp::from_micros(30_000_000));
    let Some(schedule) = ctx.db.schedule().id().find(&1) else {
        return;
    };
    ctx.db.schedule().id().update(1, ScheduleRow { schedule_token: token, ..schedule });
}

#[reducer]
pub fn apply_intent(
    ctx: &ReducerContext,
    world_id: String,
    tick: u64,
    intent_id: String,
    wallet: String,
    action_type: String,
) {
    let accepted = action_type != "Unsupported";
    let reason = if accepted {
        "Accepted".to_string()
    } else {
        "Unsupported action.".to_string()
    };

    let commit_id = format!("{world_id}:{tick}:{intent_id}");
    let committed_at_ms = ctx.timestamp.to_micros_since_unix_epoch() as i64 / 1000;

    let global_forest_delta = if action_type == "Reforest" { 1 } else { 0 };
    let global_carbon_delta = if action_type == "BuildIndustry" { -3 } else { 0 };

    let batch_hash = format!("0x{}", intent_id);

    ctx.db.action_commits().insert(ActionCommitRow {
        commit_id: commit_id.clone(),
        world_id: world_id.clone(),
        tick,
        intent_id: intent_id.clone(),
        accepted,
        reason,
        global_forest_delta,
        global_carbon_delta,
        batch_hash,
        committed_at_ms,
    });

    if let Some(intent) = ctx.db.intent_queue().intent_id().find(&intent_id) {
        ctx.db.intent_queue().intent_id().update(
            intent_id,
            IntentQueueRow {
                status: if accepted {
                    "Committed".to_string()
                } else {
                    "Rejected".to_string()
                },
                ..intent
            },
        );
    }

    if let Some(mut world) = ctx.db.world_state().world_id().find(&world_id) {
        world.forest_total += global_forest_delta;
        world.carbon_total += global_carbon_delta;
        ctx.db.world_state().world_id().update(world_id.clone(), world);
    }

    if accepted {
        let player_id = format!("{world_id}:{wallet}");
        if let Some(mut player) = ctx.db.players().id().find(&player_id) {
            player.actions_taken += 1;
            player.actions_remaining = player.actions_remaining.saturating_sub(1);
            player.sustainability_score += global_forest_delta - global_carbon_delta.abs();
            ctx.db.players().id().update(player_id, player);
        }
    }
}

#[reducer]
pub fn publish_commit(_ctx: &ReducerContext, _commit_id: String, _tx_hash: String, _publisher: Identity) {
    // Reserved reducer for relayer ack updates and external publish bookkeeping.
}
