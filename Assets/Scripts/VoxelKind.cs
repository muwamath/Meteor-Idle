// One of three states per voxel cell on a Meteor's 10x10 grid.
// - Empty: no voxel at this cell (outside the shape or destroyed)
// - Dirt: filler material, 1 HP, pays 0 on destruction
// - Core: the prize, multi-HP, pays CoreBaseValue per voxel destroyed
// Backed by byte so the parallel hp[,] array and the kind[,] array pack
// tightly into CPU cache lines during the inner ApplyBlast/ApplyTunnel loops.
public enum VoxelKind : byte
{
    Empty = 0,
    Dirt  = 1,
    Core  = 2,
}
