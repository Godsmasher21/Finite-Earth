// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";
import "@openzeppelin/contracts/token/ERC20/ERC20.sol";

contract TilesOwnedSBT is ERC20, Ownable {
    address public operator;

    event OperatorChanged(address indexed previousOperator, address indexed newOperator);

    error UnauthorizedOperator();
    error Soulbound();

    constructor(address initialOwner, address initialOperator)
        ERC20("Tiles Owned SBT", "TILES")
        Ownable(initialOwner)
    {
        operator = initialOperator;
        emit OperatorChanged(address(0), initialOperator);
    }

    modifier onlyOperator() {
        if (msg.sender != operator) {
            revert UnauthorizedOperator();
        }
        _;
    }

    function decimals() public pure override returns (uint8) {
        return 0;
    }

    function setOperator(address newOperator) external onlyOwner {
        address previous = operator;
        operator = newOperator;
        emit OperatorChanged(previous, newOperator);
    }

    function setBalance(address wallet, uint256 amount) external onlyOperator {
        uint256 current = balanceOf(wallet);
        if (amount > current) {
            _mint(wallet, amount - current);
        } else if (amount < current) {
            _burn(wallet, current - amount);
        }
    }

    function transfer(address, uint256) public pure override returns (bool) {
        revert Soulbound();
    }

    function transferFrom(address, address, uint256) public pure override returns (bool) {
        revert Soulbound();
    }

    function approve(address, uint256) public pure override returns (bool) {
        revert Soulbound();
    }

    function allowance(address, address) public pure override returns (uint256) {
        return 0;
    }
}
