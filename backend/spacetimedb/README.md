# Finite Earth SpacetimeDB Module

Authoritative world-state module (single persistent world, deterministic turn queue).

## Tables

1. `world_state`
2. `tiles`
3. `players`
4. `intent_queue`
5. `action_commits`

## Reducers

1. `submit_intent`
2. `advance_cycle`
3. `apply_intent`
4. `publish_commit`

This folder contains a runnable module scaffold and the canonical schema used by the rest of the stack.
