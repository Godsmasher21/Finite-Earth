// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC20/ERC20.sol";
import "@openzeppelin/contracts/access/Ownable.sol";

/**
 * @title CarbonToken (CRT)
 * @notice On-chain carbon ledger for Finite Earth on MegaETH.
 *
 * Total supply mirrors the CarbonTotal inside SpacetimeDB — higher carbon in
 * the game world means more CRT in circulation. Players who perform eco-actions
 * (reforest, restore, recovery projects) reduce CarbonTotal, which burns CRT.
 *
 * Economic ideas:
 *  - "Polluter pays": Industry buildings mint CRT to the player's wallet.
 *    They can sell it, but it represents environmental debt visible on-chain.
 *  - "Carbon Credit": Eco-players earn the right to burn CRT, earning FRT.
 *  - DEX pair CRT/FRT creates a real market for in-game environmental choices.
 *  - Companies can buy and burn CRT as real-world carbon offsetting.
 */
contract CarbonToken is ERC20, Ownable {

    // ── State ─────────────────────────────────────────────────────────────────

    /// Authorised relay — gateway relayer on MegaETH (chain ID 6342).
    address public relay;

    uint64 public lastSyncedCycle;

    // ── Events ────────────────────────────────────────────────────────────────
    event CarbonSynced(uint64 cycleId, int256 delta, uint256 newSupply);
    event CarbonEmitted(address indexed wallet, uint256 amount, uint64 cycleId);
    event CarbonOffset(address indexed wallet, uint256 amount, uint64 cycleId);

    // ── Constructor ───────────────────────────────────────────────────────────

    constructor(address _relay) ERC20("Finite Earth Carbon Token", "CRT") Ownable(msg.sender) {
        relay = _relay;
    }

    // ── Relay API ─────────────────────────────────────────────────────────────

    /**
     * @notice Sync total CRT supply to SpacetimeDB's authoritative CarbonTotal.
     * @param cycleId     Cycle that produced this update.
     * @param carbonDelta Net change in carbon this cycle (positive = more pollution).
     * @param relayAddr   Address used for supply-side adjustments.
     */
    function syncCarbon(uint64 cycleId, int256 carbonDelta, address relayAddr) external {
        require(msg.sender == relay, "CRT: not relay");
        require(cycleId > lastSyncedCycle, "CRT: stale cycle");
        lastSyncedCycle = cycleId;

        if (carbonDelta > 0) {
            _mint(relayAddr, uint256(carbonDelta) * 1e18);
        } else if (carbonDelta < 0) {
            uint256 toBurn = uint256(-carbonDelta) * 1e18;
            uint256 available = balanceOf(relayAddr);
            _burn(relayAddr, toBurn > available ? available : toBurn);
        }

        emit CarbonSynced(cycleId, carbonDelta, totalSupply());
    }

    /**
     * @notice Emit CRT to a player who performed a polluting action (industry, harvest).
     * Called per-commit by the relay for industrial actions.
     */
    function emitCarbon(address wallet, uint256 carbonUnits, uint64 cycleId) external {
        require(msg.sender == relay, "CRT: not relay");
        require(wallet != address(0), "CRT: zero address");
        if (carbonUnits == 0) return;

        _mint(wallet, carbonUnits * 1e18);
        emit CarbonEmitted(wallet, carbonUnits * 1e18, cycleId);
    }

    /**
     * @notice Burn CRT on behalf of a player who performed an eco-action (reforest, restore).
     * Emitting less CRT = visible proof of environmental contribution on-chain.
     */
    function offsetCarbon(address wallet, uint256 carbonUnits, uint64 cycleId) external {
        require(msg.sender == relay, "CRT: not relay");
        require(wallet != address(0), "CRT: zero address");
        if (carbonUnits == 0) return;

        uint256 amount = carbonUnits * 1e18;
        uint256 available = balanceOf(wallet);
        uint256 toBurn = amount > available ? available : amount;
        if (toBurn == 0) return;

        _burn(wallet, toBurn);
        emit CarbonOffset(wallet, toBurn, cycleId);
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

    function setRelay(address _relay) external onlyOwner {
        relay = _relay;
    }
}
