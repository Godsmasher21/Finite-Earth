// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

interface IForestToken {
    function syncForest(uint64 cycleId, int256 forestDelta, address relayAddr) external;
    function lastSyncedCycle() external view returns (uint64);
}

interface ICarbonToken {
    function syncCarbon(uint64 cycleId, int256 carbonDelta, address relayAddr) external;
    function lastSyncedCycle() external view returns (uint64);
}

/**
 * @title TokenSyncBatcher
 * @notice Batches ForestToken.syncForest + CarbonToken.syncCarbon into a single
 *         transaction so the chain relay only needs one RPC call per cycle,
 *         avoiding Thirdweb rate limits.
 *
 * Deploy this contract, then set it as the relay on both FRT and CRT.
 * The gateway calls syncBoth() each cycle and mintInitial() after world resets.
 */
contract TokenSyncBatcher {
    address public owner;
    IForestToken public forestToken;
    ICarbonToken public carbonToken;

    event BatchSynced(uint64 cycleId, int256 forestDelta, int256 carbonDelta);
    event InitialMinted(int256 forestTotal, int256 carbonTotal);

    constructor(address _forestToken, address _carbonToken) {
        owner        = msg.sender;
        forestToken  = IForestToken(_forestToken);
        carbonToken  = ICarbonToken(_carbonToken);
    }

    modifier onlyOwner() {
        require(msg.sender == owner, "Batcher: not owner");
        _;
    }

    /**
     * @notice Sync both tokens in one transaction. Called every game cycle.
     */
    function syncBoth(
        uint64 cycleId,
        int256 forestDelta,
        int256 carbonDelta,
        address relayAddr
    ) external onlyOwner {
        if (forestDelta != 0) forestToken.syncForest(cycleId, forestDelta, relayAddr);
        if (carbonDelta != 0) carbonToken.syncCarbon(cycleId, carbonDelta, relayAddr);
        emit BatchSynced(cycleId, forestDelta, carbonDelta);
    }

    /**
     * @notice Bootstrap initial supply after a world reset. Uses cycle IDs just
     *         above the current lastSyncedCycle on each token.
     */
    function mintInitial(
        int256 forestTotal,
        int256 carbonTotal,
        address relayAddr
    ) external onlyOwner {
        uint64 frtNext = forestToken.lastSyncedCycle() + 1;
        uint64 crtNext = carbonToken.lastSyncedCycle() + 1;
        if (forestTotal != 0) forestToken.syncForest(frtNext, forestTotal, relayAddr);
        if (carbonTotal != 0) carbonToken.syncCarbon(crtNext, carbonTotal, relayAddr);
        emit InitialMinted(forestTotal, carbonTotal);
    }

    function setTokens(address _forestToken, address _carbonToken) external onlyOwner {
        forestToken = IForestToken(_forestToken);
        carbonToken = ICarbonToken(_carbonToken);
    }

    function transferOwnership(address newOwner) external onlyOwner {
        owner = newOwner;
    }
}
