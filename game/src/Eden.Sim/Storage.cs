using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Shared settlement stock. Without a storehouse a settlement keeps a small pile that spoils fast; each
/// finished storehouse adds capacity and slows spoilage. Members drop surplus food off when they pass the
/// store and fetch from it when hungry (a Gather whose target is the store).
/// </summary>
public sealed partial class World
{
    private const float StoreReach = 25f;

    private int StorehouseCount(int set) => _storeCount[set];

    public float FoodCapacity(int set) => Config.BasePileCapacity + StorehouseCount(set) * BuildingKinds.All[BuildingKinds.Storehouse].Capacity;

    private float MaterialCapacity(int set) => Config.BasePileCapacity + StorehouseCount(set) * 60f;

    /// <summary>Adds food up to capacity; the rest is lost. Returns what was stored.</summary>
    private float AddFood(int set, float amount)
    {
        if (amount <= 0) return 0;
        float room = Math.Max(0f, FoodCapacity(set) - Settlements.Food[set]);
        float stored = Math.Min(room, amount);
        Settlements.Food[set] += stored;
        return stored;
    }

    /// <summary>Daily spoilage and capacity clamp.</summary>
    private void SpoilStock(int set)
    {
        var st = Settlements;
        int stores = StorehouseCount(set);
        st.Food[set] *= 1f - (stores > 0 ? Config.StoredFoodSpoilage : Config.PiledFoodSpoilage);
        st.Food[set] = Math.Min(st.Food[set], FoodCapacity(set));
        float mat = MaterialCapacity(set);
        st.Wood[set] = Math.Min(st.Wood[set], mat);
        st.Stone[set] = Math.Min(st.Stone[set], mat);
    }

    /// <summary>At decision time: a member near the store drops off surplus food (keeping a few units).</summary>
    private void DepositAtStore(int s, int set, float storeDist)
    {
        var n = Npcs;
        if (storeDist > StoreReach || n.Food[s] <= 5f || n.Need(s, Need.Food) < 0.5f) return;
        n.Food[s] -= AddFood(set, n.Food[s] - 3f);
    }

    /// <summary>Gather at the store: take what one person can carry, up to four units.</summary>
    private void WithdrawFromStore(int s, int set)
    {
        var n = Npcs;
        if (!Settlements.IsAlive(set)) return;
        var (x, z) = StorePoint(set);
        if (Dist(n.X[s], n.Z[s], x, z) > StoreReach) return;
        float take = MathF.Floor(Math.Min(Math.Min(4f, Settlements.Food[set]), Config.MaxCarriedFood - n.Food[s]));
        if (take <= 0) return;
        Settlements.Food[set] -= take;
        n.Food[s] += take;
    }
}
