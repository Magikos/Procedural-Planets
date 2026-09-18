using System;
using System.Collections.Generic;
using System.IO;

/// <summary>Food exceptions keyed by scatter identity. Rendering and harvesting do not own this stock.</summary>
public sealed class FoliageFoodStore
{
    sealed class Entry
    {
        public double Remaining;
        public long Updated;
        public bool Dirty;
    }
    readonly IWorldDeltaLog _delta;
    readonly Dictionary<ulong, Entry> _entries = new();
    public FoliageFoodStore(IWorldDeltaLog delta) => _delta = delta;

    Entry Find(ulong id)
    {
        if (_entries.TryGetValue(id, out var entry)) return entry;
        if (_delta == null || !_delta.TryGet(DeltaKind.FoliageFood, id, out var saved)) return null;
        if (saved.Payload == null || saved.Payload.Length != 17 || saved.Payload[0] != 1)
            throw new InvalidDataException("Unsupported foliage food record.");
        using var reader = new BinaryReader(new MemoryStream(saved.Payload));
        reader.ReadByte();
        entry = new Entry { Remaining = reader.ReadDouble(), Updated = reader.ReadInt64() };
        if (!double.IsFinite(entry.Remaining) || entry.Remaining < 0 || entry.Updated < 0)
            throw new InvalidDataException("Invalid foliage food record.");
        _entries.Add(id, entry);
        return entry;
    }

    public double Remaining(ulong id, ScatterPrototypeDto prototype, long now)
    {
        if (now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        var entry = Find(id);
        if (entry == null) return prototype.FoodUnits;
        double renewal = prototype.FoodRegrowSeconds > 0 ? prototype.FoodUnits / prototype.FoodRegrowSeconds : 0;
        return Math.Min(prototype.FoodUnits, entry.Remaining + Math.Max(0, now - entry.Updated) * renewal);
    }

    public double Consume(ulong id, ScatterPrototypeDto prototype, long now, ref ActorNeeds needs,
        ResourceKind diet, double requested)
    {
        var stock = new ActorResourceSource(ResourceKind.Plants, Remaining(id, prototype, now), 1, 0);
        double consumed = stock.Consume(ref needs, diet, requested);
        if (consumed <= 0) return 0;
        var entry = Find(id);
        if (entry == null) _entries.Add(id, entry = new Entry());
        entry.Remaining = stock.Remaining;
        entry.Updated = Math.Max(now, entry.Updated);
        entry.Dirty = true;
        return consumed;
    }

    public void Flush()
    {
        if (_delta == null) return;
        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            if (!entry.Dirty) continue;
            using var stream = new MemoryStream(17);
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)1); writer.Write(entry.Remaining); writer.Write(entry.Updated);
            _delta.Append(new WorldDelta(0, DeltaKind.FoliageFood, pair.Key, payload: stream.ToArray()));
            entry.Dirty = false;
        }
    }
}
