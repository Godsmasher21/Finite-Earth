// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";

contract GlobalCounters is Ownable {
    int256 public forestTotal;
    int256 public carbonTotal;
    address public updater;
    uint64 public lastCycleId;

    event UpdaterChanged(address indexed previousUpdater, address indexed newUpdater);

    event CycleCommitted(
        uint64 indexed cycleId,
        int256 forestDelta,
        int256 carbonDelta,
        int256 forestTotal,
        int256 carbonTotal,
        bytes32 actionBatchHash,
        uint32 actionCount
    );

    error UnauthorizedUpdater();
    error CycleOutOfOrder(uint64 providedCycle, uint64 expectedCycle);

    constructor(address initialOwner, address initialUpdater) Ownable(initialOwner) {
        updater = initialUpdater;
        emit UpdaterChanged(address(0), initialUpdater);
    }

    modifier onlyUpdater() {
        if (msg.sender != updater) {
            revert UnauthorizedUpdater();
        }
        _;
    }

    function setUpdater(address newUpdater) external onlyOwner {
        address previous = updater;
        updater = newUpdater;
        emit UpdaterChanged(previous, newUpdater);
    }

    function commitCycle(
        uint64 cycleId,
        int256 forestDelta,
        int256 carbonDelta,
        bytes32 actionBatchHash,
        uint32 actionCount
    ) external onlyUpdater {
        if (cycleId <= lastCycleId) {
            revert CycleOutOfOrder(cycleId, lastCycleId + 1);
        }

        lastCycleId = cycleId;
        forestTotal += forestDelta;
        carbonTotal += carbonDelta;

        emit CycleCommitted(
            cycleId,
            forestDelta,
            carbonDelta,
            forestTotal,
            carbonTotal,
            actionBatchHash,
            actionCount
        );
    }
}
