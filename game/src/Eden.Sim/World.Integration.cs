namespace Eden.Sim;

/// <summary>
/// Where M3 society and M4 knowledge meet. Kept in one file so the seams are easy to find:
/// knowledge unlocks buildings through <see cref="BuildingKinds"/>, farming yields scale with
/// knowledge, and written records live in archive buildings.
/// </summary>
public sealed partial class World
{
    private void InitIntegration()
    {
        BuildingUnlocks = BuildingKinds;
        YieldFactor = (slot, resourceId) => EffectApplier.YieldFactor(this, slot, resourceId);
        BuildingDestroyed += DestroyRecordSitesOf;
    }

    /// <summary>An archive that burns or is torn down takes its records with it.</summary>
    private void DestroyRecordSitesOf(int buildingId)
    {
        var all = Records.All;
        for (int i = all.Count - 1; i >= 0; i--)
            if (all[i].BuildingId == buildingId) DestroyRecordSite(all[i].Id);
    }
}
