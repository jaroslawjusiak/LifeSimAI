---
name: save-game-persistence
description: |
  Use when implementing or reviewing LifeSim.Persistence: versioned JSON save snapshots,
  atomic tmp->fsync->rename writes, SHA-256 integrity, last-good backups, autosave rotation,
  slot headers, and the save/engine/world compatibility gate chain with actionable refusals.
  Covers M6 stories and risk R-08.
---

# Save Game Persistence in LifeSim Engine

Saves are the player's life. They must be **human-readable, diffable, crash-safe and
version-gated** ([ADR-007](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1358),
[M6](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L851)).

---

## 1. Principles

1. **One snapshot holds everything** — clock, player, NPCs (incl. memory summaries), arcs,
   flags, `rngSeed`, and a bounded `journalTail` ([M6-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L860)).
2. **Indented, ordered, camelCase JSON** so saves diff cleanly in git and by hand.
3. **Validate before deserialise** against a JSON schema, then verify the checksum.
4. **Write atomically**: temp file → flush to disk → rename over the target. A crash can
   never leave a half-written save.
5. **Keep a last-good backup** per slot; restore it automatically on corruption, with a notice.
6. **All-or-nothing gates** with named, actionable messages — never a cryptic crash.

---

## 2. Snapshot Model

```csharp
public sealed record SaveGame(
    int FormatVersion,
    string EngineVersion,
    string WorldId,
    string WorldVersion,
    GameClock Clock,
    PlayerSnapshot Player,
    IReadOnlyList<NpcSnapshot> Npcs,
    IReadOnlyList<ArcSnapshot> Arcs,
    IReadOnlyDictionary<string, string> Flags,
    int RngSeed,
    IReadOnlyList<JournalEntry> JournalTail); // last ~2000 events, < 1 MB
```

```csharp
public static class CanonicalJson
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // Byte-identical payload encoding for hashing: no indentation, fixed policy.
    public static readonly JsonSerializerOptions Payload = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
```

### Integrity envelope (checksum "footer")

JSON cannot carry a comment, so wrap the payload and record its SHA-256. On load, the
checksum is recomputed from the **payload only** and compared with a fixed-time equality.

```csharp
public sealed record SaveEnvelope(int FormatVersion, string Sha256, SaveGame Save);

public static class SaveCodec
{
    public static string Encode(SaveGame save)
    {
        var payload = JsonSerializer.Serialize(save, CanonicalJson.Payload);
        var hash = Hash(payload);
        var envelope = new SaveEnvelope(save.FormatVersion, hash, save);
        return JsonSerializer.Serialize(envelope, CanonicalJson.Indented);
    }

    public static SaveGame Decode(string json)
    {
        var env = JsonSerializer.Deserialize<SaveEnvelope>(json, CanonicalJson.Indented)
                  ?? throw new SaveCorruptException("Empty or null save document.");

        var expected = Hash(JsonSerializer.Serialize(env.Save, CanonicalJson.Payload));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(env.Sha256)))
        {
            throw new SaveCorruptException(
                $"Checksum mismatch (expected {expected[..12]}…, got {env.Sha256[..12]}…).");
        }

        return env.Save;
    }

    private static string Hash(string payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
```

Validate against a schema **before** `Decode` so malformed structure fails fast
([M6-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L866)).

```csharp
public static void ValidateSchema(string json, JsonSchema schema)
{
    var node = JsonNode.Parse(json) ?? throw new SaveCorruptException("Not JSON.");
    var result = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
    if (!result.IsValid)
        throw new SaveCorruptException($"Schema: {result.Details}");
}
```

---

## 3. Atomic Store ([M6-02](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L878))

```csharp
public sealed class SaveStore(string root)
{
    public async Task WriteAsync(string slot, SaveGame save, CancellationToken ct)
    {
        var dir = Path.Combine(root, slot);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "save.json");
        var backup = Path.Combine(dir, "save.prev.json");

        if (File.Exists(target))
            File.Copy(target, backup, overwrite: true); // last-good backup

        await AtomicWriteAsync(target, SaveCodec.Encode(save), ct);
    }

    public static async Task AtomicWriteAsync(string path, string content, CancellationToken ct)
    {
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(
            tmp, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, FileOptions.WriteThrough))
        await using (var writer = new StreamWriter(fs))
        {
            await writer.WriteAsync(content.AsMemory(), ct);
            await writer.FlushAsync(ct);
            fs.Flush(flushToDisk: true); // fsync
        }

        File.Move(tmp, path, overwrite: true); // atomic rename on the same volume
    }
}
```

**Load with recovery**: on checksum failure, restore `save.prev.json` and surface a notice;
if both are unusable, fail with a typed error and **never touch the files**.

```csharp
public SavedGame Load(string slot)
{
    var dir = Path.Combine(root, slot);
    var target = Path.Combine(dir, "save.json");
    try
    {
        return new SavedGame(SaveCodec.Decode(File.ReadAllText(target)));
    }
    catch (SaveCorruptException ex)
    {
        var backup = Path.Combine(dir, "save.prev.json");
        if (!File.Exists(backup)) throw;

        var restored = SaveCodec.Decode(File.ReadAllText(backup));
        return new SavedGame(restored, Notices.RestoredFromBackup(ex.Message));
    }
}
```

**Autosave rotation** uses dedicated slots (`autosave-0…N`) so manual slots are never
clobbered. Hook it to `DayStarted` and to quit
([M6-02](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L882)).

---

## 4. Compatibility Gates ([M6-03](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L896))

Evaluate in this exact order; each failure returns a distinct, actionable reason.

```
formatVersion (migrations; 1.0 = v1 identity)
  → engineVersion range
  → worldId present in the library
  → worldVersion same major
```

```csharp
public abstract record LoadDecision
{
    public sealed record Load(SaveGame Save, string? MigrationNote) : LoadDecision;
    public sealed record Refuse(string Reason) : LoadDecision;
}

public static class LoadGates
{
    public static LoadDecision Evaluate(SaveEnvelope env, EngineVersion engine, WorldLibrary library)
    {
        if (env.FormatVersion > SaveGame.CurrentFormatVersion)
            return new LoadDecision.Refuse(
                $"This save was written by a newer format ({env.FormatVersion}). Update the engine.");

        if (!SaveMigration.TryMigrate(env.FormatVersion, out var save, out var note))
            return new LoadDecision.Refuse($"Unsupported save format {env.FormatVersion}.");

        if (!engine.Satisfies(save.EngineVersion))
            return new LoadDecision.Refuse(
                $"This save needs engine {save.EngineVersion}, but you have {engine}.");

        if (!library.TryGetWorld(save.WorldId, out var world))
            return new LoadDecision.Refuse(
                $"This save needs world '{save.WorldId}' — install it to continue.");

        if (!world.IsSameMajor(save.WorldVersion))
            return new LoadDecision.Refuse(
                $"This save needs {save.WorldId} {save.WorldVersion.Split('.')[0]}.x; " +
                $"found {world.Version}.");

        return new LoadDecision.Load(save, note);
    }
}
```

Missing prompt templates from a world fall back to engine defaults **silently**; only the
gates above are hard refusals.

---

## 5. Slot Headers ([M6-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L914))

The slot list must render without deserialising full saves. Persist a tiny header so the
scanner reads only the first few KB: `{ worldId, worldName, dayIndex, hour, locationId,
money, playtimeMinutes, savedAtUtc, isAutosave, checksumPrefix }`. A corrupt slot still
appears in the list, marked **corrupt** (never invisible).

---

## 6. Tests to Write

- Roundtrip: `save → load → save` byte-equal under normalized ordering.
- Kill mid-write: start a write, abandon the temp file, assert the previous save loads.
- Corruption: flip a payload byte → checksum mismatch → backup restored.
- Autosave rotation leaves manual slots untouched.
- Each gate failure has a distinct message (assert the reason text).
- Determinism: same `WorldState` + seed twice → identical encoded payload hash.

---

## 7. Related Skills

- Snapshot equality & canonical hashing: `dotnet-testing-standards`.
- World version / library: `markdown-ast-validator`.
- UI slot cards: `spectre-console-tui`.
