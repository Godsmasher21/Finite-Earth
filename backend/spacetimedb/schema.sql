CREATE TABLE world_state (
  world_id TEXT PRIMARY KEY,
  tick BIGINT NOT NULL,
  cycle_seconds INTEGER NOT NULL,
  actions_per_cycle INTEGER NOT NULL,
  forest_total BIGINT NOT NULL,
  carbon_total BIGINT NOT NULL,
  last_chain_cycle BIGINT NOT NULL,
  rng_seed BIGINT NOT NULL
);

CREATE TABLE tiles (
  id TEXT PRIMARY KEY,
  world_id TEXT NOT NULL,
  q INTEGER NOT NULL,
  r INTEGER NOT NULL,
  base_type TEXT NOT NULL,
  current_state TEXT NOT NULL,
  owner_wallet TEXT NOT NULL,
  building_type TEXT NOT NULL,
  fertility_bp INTEGER NOT NULL,
  pollution_bp INTEGER NOT NULL,
  biodiversity_bp INTEGER NOT NULL,
  last_updated_tick BIGINT NOT NULL
);

CREATE TABLE players (
  id TEXT PRIMARY KEY,
  world_id TEXT NOT NULL,
  wallet TEXT NOT NULL,
  sustainability_score BIGINT NOT NULL,
  actions_taken BIGINT NOT NULL,
  owned_tiles_count INTEGER NOT NULL,
  last_client_seq BIGINT NOT NULL,
  actions_remaining INTEGER NOT NULL
);

CREATE TABLE intent_queue (
  intent_id TEXT PRIMARY KEY,
  world_id TEXT NOT NULL,
  wallet TEXT NOT NULL,
  client_seq BIGINT NOT NULL,
  action_type TEXT NOT NULL,
  q INTEGER NOT NULL,
  r INTEGER NOT NULL,
  building_type TEXT NOT NULL,
  submitted_at_ms BIGINT NOT NULL,
  status TEXT NOT NULL
);

CREATE TABLE action_commits (
  commit_id TEXT PRIMARY KEY,
  world_id TEXT NOT NULL,
  tick BIGINT NOT NULL,
  intent_id TEXT NOT NULL,
  accepted BOOLEAN NOT NULL,
  reason TEXT NOT NULL,
  global_forest_delta BIGINT NOT NULL,
  global_carbon_delta BIGINT NOT NULL,
  batch_hash TEXT NOT NULL
);
