using UnityEngine;

// Per-cell material data for voxel meteors. Each material kind (Dirt, Stone,
// Core, Gold, Explosive, …) is a single asset under Assets/Data/Materials/.
// Behavior dispatch lives on the asset, not in switch statements throughout
// Meteor.cs. Adding a new inert material is "create asset, register". Adding
// a new behavior is "new MaterialBehavior enum value + handler in Meteor.Update".
[CreateAssetMenu(menuName = "Meteor Idle/Voxel Material", fileName = "VoxelMaterial")]
public class VoxelMaterial : ScriptableObject
{
    [Tooltip("Debug-only display name. Inspector and logs use this.")]
    public string displayName = "Unnamed";

    [Header("Visuals")]
    [Tooltip("Top edge color of the 15x15 voxel block.")]
    public Color topColor = Color.white;
    [Tooltip("Bottom edge color of the 15x15 voxel block.")]
    public Color bottomColor = Color.gray;

    [Header("Mechanics")]
    [Tooltip("HP this material starts with when placed. Cores override this by size.")]
    public int baseHp = 1;

    [Tooltip("Money paid when one cell of this material is destroyed (HP hits 0).")]
    public int payoutPerCell = 0;

    [Tooltip("If true, destroying a cell of this material pays out immediately via GameManager.AddMoney. If false, the caller is responsible for an alternate payout path (e.g. Iter 3 cores spawning CoreDrops).")]
    public bool paysOnBreak = true;

    [Tooltip("Behavior verb. Inert = passive filler. Explosive = enqueues neighbor damage on death.")]
    public MaterialBehavior behavior = MaterialBehavior.Inert;

    [Header("Targeting")]
    [Tooltip("Turret targeting priority. 0 = never targeted. Lower positive = higher priority. Gold=1, Explosive=2, Core=3.")]
    public int targetingTier = 0;

    [Header("Placement")]
    [Tooltip("Independent rarity dial. Higher = more common. Generator interprets per-material; see VoxelMeteorGenerator.")]
    public float spawnWeight = 0f;
}

// Behavior verb for a material. Iter 2 introduces the first non-inert kind
// (Explosive). Future iterations can add Magnetic, Frozen, Reactive, etc. by
// extending this enum and adding a handler in Meteor.Update's pending-action
// loop. Each new behavior is a bounded ~30-50 LOC extension.
public enum MaterialBehavior : byte
{
    Inert = 0,
    Explosive = 1,
}
