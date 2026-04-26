// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";

/**
 * @title GlobalCounters
 * @notice On-chain record of every game cycle's environmental footprint.
 * Called once per cycle by the gateway chain relay after AdvanceCycle settles.
 *
 * On MegaETH (chain 6342) this contract is the canonical source of truth for:
 *  - Per-cycle forest and carbon deltas
 *  - Cumulative totals (forestTotal, carbonTotal)
 *  - Action batch hashes (provable audit trail of every player action)
 */
contract GlobalCounters is Ownable {

    address public relay;

    struct CycleRecord {
        uint64  cycleId;
        int256  forestDelta;
        int256  carbonDelta;
        int256  forestTotal;
        int256  carbonTotal;
        bytes32 actionBatchHash;
        uint32  actionCount;
        uint256 committedAt;
    }

    mapping(uint64 => CycleRecord) public cycles;
    uint64 public lastCycleId;

    event CycleCommitted(
        uint64  indexed cycleId,
        int256  forestDelta,
        int256  carbonDelta,
        int256  forestTotal,
        int256  carbonTotal,
        bytes32 actionBatchHash,
        uint32  actionCount
    );

    constructor(address _relay) Ownable(msg.sender) {
        relay = _relay;
    }

    function commitCycle(
        uint64  cycleId,
        int256  forestDelta,
        int256  carbonDelta,
        bytes32 actionBatchHash,
        uint32  actionCount
    ) external {
        require(msg.sender == relay, "GlobalCounters: not relay");
        require(cycleId > lastCycleId,  "GlobalCounters: stale cycle");

        int256 forestTotal = cycles[lastCycleId].forestTotal + forestDelta;
        int256 carbonTotal = cycles[lastCycleId].carbonTotal + carbonDelta;
        if (carbonTotal < 0) carbonTotal = 0;

        cycles[cycleId] = CycleRecord({
            cycleId:         cycleId,
            forestDelta:     forestDelta,
            carbonDelta:     carbonDelta,
            forestTotal:     forestTotal,
            carbonTotal:     carbonTotal,
            actionBatchHash: actionBatchHash,
            actionCount:     actionCount,
            committedAt:     block.timestamp
        });

        lastCycleId = cycleId;

        emit CycleCommitted(
            cycleId, forestDelta, carbonDelta,
            forestTotal, carbonTotal, actionBatchHash, actionCount
        );
    }

    function setRelay(address _relay) external onlyOwner {
        relay = _relay;
    }
}
