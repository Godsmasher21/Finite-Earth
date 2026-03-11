// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/access/Ownable.sol";
import "@openzeppelin/contracts/token/ERC20/ERC20.sol";

contract GlobalCarbonToken is ERC20, Ownable {
    address public operator;

    event OperatorChanged(address indexed previousOperator, address indexed newOperator);

    error UnauthorizedOperator();

    constructor(address initialOwner, address initialOperator)
        ERC20("Global Carbon Token", "GCARBON")
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

    function mint(address to, uint256 amount) external onlyOperator {
        _mint(to, amount);
    }

    function burn(address from, uint256 amount) external onlyOperator {
        _burn(from, amount);
    }
}
