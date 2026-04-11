using System.Collections.Generic;
using UnityEngine;

// Single registry asset that holds the list of all VoxelMaterial assets used
// by the game. VoxelMeteorGenerator and Meteor both reference this so they
// can resolve material asset references and enumerate materials by tier or
// behavior. One asset, lives at Assets/Data/MaterialRegistry.asset.
//
// Why: avoids passing 5+ individual material refs into every consumer, and
// gives us a single place to add a new material without touching call sites.
[CreateAssetMenu(menuName = "Meteor Idle/Material Registry", fileName = "MaterialRegistry")]
public class MaterialRegistry : ScriptableObject
{
    [Tooltip("All VoxelMaterial assets used by the generator and Meteor.")]
    public VoxelMaterial[] materials;

    // Convenience accessors used by tests and by code that needs to look up
    // a material by displayName. Linear scan is fine — the list has <10
    // entries and lookups are not on the per-frame hot path.

    public VoxelMaterial GetByName(string displayName)
    {
        if (materials == null) return null;
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] != null && materials[i].displayName == displayName)
                return materials[i];
        return null;
    }

    public int IndexOf(VoxelMaterial material)
    {
        if (materials == null || material == null) return -1;
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] == material) return i;
        return -1;
    }

    // Materials with targetingTier > 0 in priority order (lowest tier number
    // first = highest priority). Used by Meteor.PickPriorityVoxel.
    public IEnumerable<VoxelMaterial> TargetableInPriorityOrder()
    {
        if (materials == null) yield break;
        var sorted = new List<VoxelMaterial>();
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] != null && materials[i].targetingTier > 0)
                sorted.Add(materials[i]);
        sorted.Sort((a, b) => a.targetingTier.CompareTo(b.targetingTier));
        foreach (var m in sorted) yield return m;
    }
}
