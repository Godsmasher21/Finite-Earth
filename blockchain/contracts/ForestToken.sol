// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/ERC20.sol";
import "@openzeppelin/contracts/access/Ownable.sol";

/**
 * @title ForestToken (FRT)
 * @notice On-chain representation of the global forest health in Finite Earth.
 *
 * Total supply mirrors the ForestTotal value tracked inside SpacetimeDB.
 * Every AdvanceCycle, the chain relay calls syncForest() to mint or burn tokens
 * so that totalSupply() always equals the current forest tile count on MegaETH.
 *
 * Economic ideas:
 *  - Players earn FRT pro-rata based on their owned forest tiles.
 *  - FRT can be staked for passive income ("Forest Bond").
 *  - Governance: FRT holders vote on game parameters via a DAO.
 *  - Carbon market: FRT is paired with CarbonToken on a DEX so eco-friendly
 *    play has real monetary value.
 */
contract ForestToken is ERC20, Ownable {

    // ── State ─────────────────────────────────────────────────────────────────

    /// Authorised relay — set to gateway relayer address on MegaETH.
    address public relay;

    /// Last synced cycle id, used to guard against replay.
    uint64 public lastSyncedCycle;

    // ── Events ────────────────────────────────────────────────────────────────
    event ForestSynced(uint64 cycleId, int256 delta, uint256 newSupply);
    event PlayerRewarded(address indexed wallet, uint256 amount, uint64 cycleId);

    // ── Constructor ───────────────────────────────────────────────────────────

    constructor(address _relay) ERC20("Finite Earth Forest Token", "FRT") Ownable(msg.sender) {
        relay = _relay;
    }

    // ── Relay API ─────────────────────────────────────────────────────────────

    /**
     * @notice Sync total supply to the authoritative ForestTotal from SpacetimeDB.
     * @param cycleId    The cycle that produced this update (monotone guard).
     * @param forestDelta Net change in forest tiles this cycle (positive = grew).
     * @param relayAddr   Burn-to / mint-from address for supply adjustment.
     */
    function syncForest(uint64 cycleId, int256 forestDelta, address relayAddr) external {
        require(msg.sender == relay, "FRT: not relay");
        require(cycleId > lastSyncedCycle, "FRT: stale cycle");
        lastSyncedCycle = cycleId;

        if (forestDelta > 0) {
            _mint(relayAddr, uint256(forestDelta) * 1e18);
        } else if (forestDelta < 0) {
            uint256 toBurn = uint256(-forestDelta) * 1e18;
            uint256 available = balanceOf(relayAddr);
            _burn(relayAddr, toBurn > available ? available : toBurn);
        }

        emit ForestSynced(cycleId, forestDelta, totalSupply());
    }

    /**
     * @notice Distribute FRT rewards to a player based on their forest tile count.
     * Called by the relay at cycle end for each player.
     * @param wallet       Player's MegaETH wallet.
     * @param forestTiles  Number of forest tiles the player owns this cycle.
     * @param cycleId      Cycle that generated this reward.
     */
    function rewardPlayer(address wallet, uint256 forestTiles, uint64 cycleId) external {
        require(msg.sender == relay, "FRT: not relay");
        require(wallet != address(0), "FRT: zero address");
        if (forestTiles == 0) return;

        uint256 amount = forestTiles * 1e18;
        _mint(wallet, amount);
        emit PlayerRewarded(wallet, amount, cycleId);
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

    function setRelay(address _relay) external onlyOwner {
        relay = _relay;
    }
}
