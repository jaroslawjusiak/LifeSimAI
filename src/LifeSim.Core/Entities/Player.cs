using LifeSim.Core.Stats;

namespace LifeSim.Core.Entities;

/// <summary>
/// The player character: needs as a <see cref="StatSet"/>, a <see cref="SkillSet"/>, traits,
/// inventory, money and current location. Money and location mutate as actions resolve;
/// needs and skills mutate through their own aggregates.
/// </summary>
public sealed class Player
{
    public Player(
        string name,
        StatSet stats,
        SkillSet skills,
        decimal money,
        string locationId,
        IEnumerable<string>? traits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

        Name = name;
        Stats = stats;
        Skills = skills;
        Money = money;
        LocationId = locationId;
        Traits = traits?.ToList() ?? [];
        Inventory = [];
    }

    public string Name { get; }

    public StatSet Stats { get; }

    public SkillSet Skills { get; }

    /// <summary>Trait ids the player possesses.</summary>
    public List<string> Traits { get; }

    /// <summary>Held item stacks.</summary>
    public List<ItemStack> Inventory { get; }

    public decimal Money { get; set; }

    public string LocationId { get; set; }

    public bool HasItem(string itemId) => Inventory.Any(i => i.ItemId == itemId && i.Quantity > 0);

    public void AddItem(string itemId, int quantity = 1)
    {
        var existing = Inventory.FirstOrDefault(i => i.ItemId == itemId);
        if (existing is null)
        {
            Inventory.Add(new ItemStack(itemId, quantity));
            return;
        }

        var index = Inventory.IndexOf(existing);
        Inventory[index] = existing with { Quantity = existing.Quantity + quantity };
    }
}
