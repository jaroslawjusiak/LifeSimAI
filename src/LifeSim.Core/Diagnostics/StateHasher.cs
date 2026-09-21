using System.Security.Cryptography;
using System.Text;
using LifeSim.Core.Entities;

namespace LifeSim.Core.Diagnostics;

/// <summary>
/// Produces a stable SHA-256 fingerprint of a world's live state, for determinism and
/// snapshot-equality checks (replay, save round-trips, invariants).
/// </summary>
public static class StateHasher
{
    public static string Compute(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(world.CreateSnapshot())));
    }
}
