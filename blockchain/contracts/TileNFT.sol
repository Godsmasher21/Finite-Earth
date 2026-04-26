// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import "@openzeppelin/contracts/token/ERC721/ERC721.sol";
import "@openzeppelin/contracts/access/Ownable.sol";
import "@openzeppelin/contracts/utils/Strings.sol";

/**
 * @title TileNFT
 * @notice Soulbound ERC-721 (EIP-5192) representing claimed tiles in Finite Earth.
 *
 * Each token encodes the tile's (q, r) hex-grid coordinate.
 * Tokens are non-transferable ("soulbound") — the owner is the MegaETH wallet
 * that claimed the tile on-chain, providing transparent proof of territory.
 *
 * The chain relay in backend/gateway mints a token for every accepted Claim
 * action that sets ownerChanged == true.
 *
 * Leaderboard derivation: sort wallets by balanceOf(wallet) descending.
 */
contract TileNFT is ERC721, Ownable {
    using Strings for uint256;

    // ── EIP-5192 Minimal Soulbound Token ─────────────────────────────────────
    event Locked(uint256 tokenId);
    event Unlocked(uint256 tokenId);

    function locked(uint256 /*tokenId*/) external pure returns (bool) {
        return true; // all tokens are permanently locked
    }

    // ── State ─────────────────────────────────────────────────────────────────

    // Authorized minter — set to the gateway relayer address.
    address public minter;

    // token id = packCoord(q, r) using the same packing as Lib.cs: (q << 20) | r
    // Stored for reverse-lookup convenience.
    mapping(uint256 => int32) public tokenQ;
    mapping(uint256 => int32) public tokenR;

    // Track which wallet owns which tile (mirrors SpacetimeDB).
    mapping(uint256 => address) public tileOwner;

    string private _baseTokenURI;

    // ── Events ────────────────────────────────────────────────────────────────
    event TileClaimed(address indexed wallet, int32 q, int32 r, uint256 tokenId);
    event TileTransferred(address indexed from, address indexed to, int32 q, int32 r, uint256 tokenId);

    // ── Constructor ───────────────────────────────────────────────────────────

    constructor(address _minter, string memory baseURI)
        ERC721("Finite Earth Tile", "TILE")
        Ownable(msg.sender)
    {
        minter = _minter;
        _baseTokenURI = baseURI;
    }

    // ── Minting ───────────────────────────────────────────────────────────────

    /**
     * @notice Claim a tile for a wallet, or transfer it if already minted.
     * Called by the chain relay for every accepted Claim action.
     * @param wallet  The MegaETH wallet that claimed the tile.
     * @param q       Hex column (offset coordinate, signed).
     * @param r       Hex row (offset coordinate, signed).
     */
    function claimTile(address wallet, int32 q, int32 r) external {
        require(msg.sender == minter, "TileNFT: not minter");
        require(wallet != address(0), "TileNFT: zero address");

        uint256 tokenId = _packCoord(q, r);

        if (_ownerOf(tokenId) == address(0)) {
            // First claim — mint.
            _safeMint(wallet, tokenId);
            tokenQ[tokenId] = q;
            tokenR[tokenId] = r;
            tileOwner[tokenId] = wallet;
            emit TileClaimed(wallet, q, r, tokenId);
            emit Locked(tokenId);
        } else {
            // Tile changed hands — record new owner (tokens stay non-transferable
            // by users, but the relay can update ownership to reflect game state).
            address prev = tileOwner[tokenId];
            tileOwner[tokenId] = wallet;
            emit TileTransferred(prev, wallet, q, r, tokenId);
        }
    }

    // ── Soulbound — block all user-initiated transfers ────────────────────────

    function transferFrom(address, address, uint256) public pure override {
        revert("TileNFT: soulbound");
    }

    function safeTransferFrom(address, address, uint256, bytes memory) public pure override {
        revert("TileNFT: soulbound");
    }

    // ── Metadata ──────────────────────────────────────────────────────────────

    function tokenURI(uint256 tokenId) public view override returns (string memory) {
        _requireOwned(tokenId);
        return string(abi.encodePacked(_baseTokenURI, tokenId.toString()));
    }

    function setBaseURI(string memory baseURI) external onlyOwner {
        _baseTokenURI = baseURI;
    }

    function setMinter(address _minter) external onlyOwner {
        minter = _minter;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// @dev Mirrors Lib.cs PackTileId: (q << 20) | r, with sign extension handled
    ///      by casting to uint256 after the shift.
    function _packCoord(int32 q, int32 r) internal pure returns (uint256) {
        return (uint256(uint32(q)) << 20) | uint256(uint32(r));
    }

    function supportsInterface(bytes4 interfaceId) public view override returns (bool) {
        // EIP-5192 interface id
        return interfaceId == 0xb45a3c0e || super.supportsInterface(interfaceId);
    }
}
