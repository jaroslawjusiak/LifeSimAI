namespace LifeSim.Core.Entities;

/// <summary>A catalog item. Effects are applied by the action resolver (M1-04).</summary>
public sealed record Item(string Id, string Name) : IEntityWithId;

/// <summary>A stack of an item held in inventory.</summary>
public sealed record ItemStack(string ItemId, int Quantity);
