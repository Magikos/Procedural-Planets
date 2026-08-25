using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Append-only log of every persistent world change, with size-triggered compaction.
//
// The world is a seed plus an exception log: a tree is derived until someone chops it, at which point one
// record says so. That keeps the save proportional to what players changed rather than to the world, and it
// is why the same records serve the join snapshot and the replication stream.
//
// Replaces hand-rolled JSON stores that each did File.WriteAllText on every mutation - a synchronous
// whole-file rewrite per chop.
//
// Durability, in the order that matters. Each of these is a way to lose the whole save rather than one
// action, so none is optional:
//  - Every record carries a trailing checksum. A crash mid-append leaves a torn tail, and the reader stops at
//    the first record that fails to validate rather than trusting the rest of the file.
//  - Compaction writes a new file and renames it over the target BEFORE the log is deleted. Rename is atomic
//    on Windows and POSIX, so the rename is the commit point and there is never an instant at which neither
//    file is complete.
//  - Appends reach the OS immediately; Flush forces them to the platter.
public sealed class WorldDeltaLog : IWorldDeltaLog, System.IDisposable
{
    // Bumped only on a change the reader cannot interpret. A file whose version is newer, or older than the
    // oldest version this reader still understands, is refused rather than half-read - a half-read save is
    // worse than an absent one.
    public const int SchemaVersion = 2;

    // v1's fixed part is a strict prefix of v2's: the scale field was appended at the end, so every earlier
    // field sits at the same offset and a v1 record reads as a v2 record with scale 1.
    const int OldestReadableVersion = 1;
    const int FixedBytesV1 = 48;

    const int HeaderSize = 8;               // magic 4 + version 4
    const uint Magic = 0x504C4457;          // WDLP
    const long DefaultCompactThreshold = 1 << 20;   // 1 MB of appends, about 20k transform-only records

    readonly ILogger _log;
    readonly long _compactThreshold;
    readonly List<WorldDelta> _live = new();
    readonly Dictionary<(byte Space, ulong Key), int> _byKey = new();   // (space, key) -> index into _live
    readonly byte[] _scratch = new byte[WorldDelta.FixedBytes];

    string _basePath;
    string _logPath;
    FileStream _stream;
    uint _nextSequence = 1;
    long _appendedBytes;
    bool _readAnOlderSchema;

    public WorldDeltaLog(ILogger log = null, long compactThreshold = DefaultCompactThreshold)
    {
        _log = log;
        _compactThreshold = compactThreshold > 0 ? compactThreshold : DefaultCompactThreshold;
    }

    public bool IsOpen => _stream != null;
    public int Count => _live.Count;
    public uint NextSequence => _nextSequence;

    /// <summary>Records discarded on load because they failed to validate - a torn tail from a crash.</summary>
    public int TornRecordsDropped { get; private set; }

    public void Open(string directory, string worldKey)
    {
        Close();
        Directory.CreateDirectory(directory);
        _basePath = Path.Combine(directory, worldKey + ".sav");
        _logPath = Path.Combine(directory, worldKey + ".log");

        _live.Clear();
        _byKey.Clear();
        _nextSequence = 1;
        TornRecordsDropped = 0;
        _readAnOlderSchema = false;

        Replay(_basePath);
        Replay(_logPath);
        OpenLogStream();

        // An older file cannot be appended to: the next record would be written in the new framing behind a
        // header that promises the old one. Compaction already rewrites everything live under the current
        // header and drops the log, so the upgrade is the existing path rather than a second one.
        if (_readAnOlderSchema) Compact();
    }

    // FileMode.Append on a missing file gives an empty file, and an empty file carries no magic - a reader
    // would refuse it and silently drop every append made since. The header goes down before the first record.
    void OpenLogStream()
    {
        _stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        if (_stream.Length == 0) { WriteHeader(_stream); _stream.Flush(); }
        _appendedBytes = _stream.Length - HeaderSize;
    }

    public WorldDelta Append(in WorldDelta delta)
    {
        WorldDelta stored = delta.WithSequence(_nextSequence++);
        Remember(stored);

        if (_stream != null)
        {
            WriteRecord(_stream, stored, _scratch);
            _stream.Flush();     // to the OS, not the platter: survives a process crash with no fsync stall per chop
            _appendedBytes += stored.SerializedSize;
            if (_appendedBytes >= _compactThreshold) Compact();
        }
        return stored;
    }

    public bool TryGet(DeltaKind kind, ulong key, out WorldDelta delta)
    {
        if (_byKey.TryGetValue((SpaceOf(kind), key), out int index)) { delta = _live[index]; return true; }
        delta = default;
        return false;
    }

    // A ScatterId and an EntityId are different numbers for different things, and nothing stops them being
    // the SAME number - a ScatterId packs a cell address that can be small, and EntityId counters start at 1.
    // Keying the live set on the raw ulong would let a dropped log overwrite a chopped tree. The space is the
    // disambiguator, so lookups take the kind rather than the bare key.
    //
    // Kinds that describe one thing share a space on purpose: a tree's ScatterRemoved and ScatterState
    // collapse to one live record, as do an entity's Spawned, Moved, Removed and State.
    internal static byte SpaceOf(DeltaKind kind) => kind switch
    {
        DeltaKind.ScatterRemoved or DeltaKind.ScatterState => 1,
        DeltaKind.SurfaceStamp => 2,
        DeltaKind.PlayerState => 3,
        DeltaKind.TerrainDeform => 4,
        DeltaKind.EntitySpawned or DeltaKind.EntityMoved
            or DeltaKind.EntityRemoved or DeltaKind.EntityState => 5,
        _ => 0,
    };

    public IReadOnlyList<WorldDelta> Snapshot() => _live;

    /// <summary>Force appends all the way to the platter. Call on world unload and quit; appends already reach the OS.</summary>
    public void Flush() => _stream?.Flush(flushToDisk: true);

    // Rewrite the whole live set as a new base and drop the log. Ordering is the correctness-critical part:
    // the replacement is committed by rename BEFORE the log is deleted, so a crash at any instant leaves
    // either the old base plus its log, or the new base - never a gap.
    public void Compact()
    {
        if (_basePath == null) return;

        string temp = _basePath + ".tmp";
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            WriteHeader(fs);
            foreach (WorldDelta d in _live) WriteRecord(fs, d, _scratch);
            fs.Flush(flushToDisk: true);
        }

        _stream?.Dispose();
        _stream = null;

        if (File.Exists(_basePath)) File.Replace(temp, _basePath, null);
        else File.Move(temp, _basePath);

        File.Delete(_logPath);                                     // only now, after the rename committed
        OpenLogStream();
    }

    public void Close()
    {
        if (_stream == null) return;
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        _stream = null;
    }

    public void Dispose() => Close();

    void Remember(in WorldDelta d)
    {
        // Last write per key wins, so a stump that is later dug leaves one record rather than two, and the
        // live set stays proportional to changed things rather than to changes.
        (byte, ulong) slot = (SpaceOf(d.Kind), d.Key);
        if (_byKey.TryGetValue(slot, out int index)) _live[index] = d;
        else { _byKey[slot] = _live.Count; _live.Add(d); }
        if (d.Sequence >= _nextSequence) _nextSequence = d.Sequence + 1;
    }

    void Replay(string path)
    {
        if (path == null || !File.Exists(path)) return;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < HeaderSize) return;

            var header = new byte[HeaderSize];
            if (!ReadExactly(fs, header, HeaderSize)) return;
            if (System.BitConverter.ToUInt32(header, 0) != Magic)
            {
                Warn(Path.GetFileName(path) + ": not a delta log");
                return;
            }
            int version = System.BitConverter.ToInt32(header, 4);
            if (version < OldestReadableVersion || version > SchemaVersion)
            {
                Warn($"{Path.GetFileName(path)}: schema v{version}, expected v{OldestReadableVersion}..v{SchemaVersion} - refused");
                return;
            }
            if (version < SchemaVersion) _readAnOlderSchema = true;

            int fixedBytes = version == 1 ? FixedBytesV1 : WorldDelta.FixedBytes;
            var scratch = new byte[WorldDelta.FixedBytes];
            while (true)
            {
                RecordRead status = TryReadRecord(fs, scratch, fixedBytes, out WorldDelta d);
                if (status == RecordRead.EndOfFile) break;
                if (status == RecordRead.Damaged)
                {
                    // A torn tail is the expected outcome of a crash mid-append. Everything before it is
                    // intact, so keep that and discard from here.
                    TornRecordsDropped++;
                    break;
                }
                Remember(d);
            }
        }
        catch (System.Exception e)
        {
            LoggerProvider.LogException("WorldDeltaLog", e);
        }
    }

    void WriteHeader(FileStream fs)
    {
        var header = new byte[HeaderSize];
        System.BitConverter.GetBytes(Magic).CopyTo(header, 0);
        System.BitConverter.GetBytes(SchemaVersion).CopyTo(header, 4);
        fs.Write(header, 0, HeaderSize);
    }

    // --- record framing ---
    //
    // sequence 4 | kind 1 | key 8 | payloadLength 2 | position 12 | rotation 16 | typeIndex 4 | state 1 | scale 4
    //   then payloadLength bytes, then a 4-byte checksum over everything above.
    //
    // Scale sits last so v1 (which ended at state) is a strict prefix and reads with no offset table.

    enum RecordRead { Ok, EndOfFile, Damaged }

    internal static void WriteRecord(Stream s, in WorldDelta d, byte[] scratch)
    {
        int n = d.PayloadLength;
        System.BitConverter.GetBytes(d.Sequence).CopyTo(scratch, 0);
        scratch[4] = (byte)d.Kind;
        System.BitConverter.GetBytes(d.Key).CopyTo(scratch, 5);
        System.BitConverter.GetBytes((ushort)n).CopyTo(scratch, 13);
        System.BitConverter.GetBytes(d.Position.x).CopyTo(scratch, 15);
        System.BitConverter.GetBytes(d.Position.y).CopyTo(scratch, 19);
        System.BitConverter.GetBytes(d.Position.z).CopyTo(scratch, 23);
        System.BitConverter.GetBytes(d.Rotation.x).CopyTo(scratch, 27);
        System.BitConverter.GetBytes(d.Rotation.y).CopyTo(scratch, 31);
        System.BitConverter.GetBytes(d.Rotation.z).CopyTo(scratch, 35);
        System.BitConverter.GetBytes(d.Rotation.w).CopyTo(scratch, 39);
        System.BitConverter.GetBytes(d.TypeIndex).CopyTo(scratch, 43);
        scratch[47] = d.State;
        System.BitConverter.GetBytes(d.Scale).CopyTo(scratch, 48);

        uint hash = Checksum(scratch, WorldDelta.FixedBytes, Fnv1aSeed);
        if (n > 0) hash = Checksum(d.Payload, n, hash);

        s.Write(scratch, 0, WorldDelta.FixedBytes);
        if (n > 0) s.Write(d.Payload, 0, n);

        byte[] tail = System.BitConverter.GetBytes(hash);
        s.Write(tail, 0, WorldDelta.ChecksumBytes);
    }

    static RecordRead TryReadRecord(Stream s, byte[] scratch, int fixedBytes, out WorldDelta d)
    {
        d = default;
        int first = s.Read(scratch, 0, fixedBytes);
        if (first == 0) return RecordRead.EndOfFile;
        if (first < fixedBytes && !ReadExactly(s, scratch, fixedBytes, first))
            return RecordRead.Damaged;

        int n = System.BitConverter.ToUInt16(scratch, 13);
        byte[] payload = null;
        if (n > 0)
        {
            payload = new byte[n];
            if (!ReadExactly(s, payload, n)) return RecordRead.Damaged;
        }

        var tail = new byte[WorldDelta.ChecksumBytes];
        if (!ReadExactly(s, tail, WorldDelta.ChecksumBytes)) return RecordRead.Damaged;

        uint hash = Checksum(scratch, fixedBytes, Fnv1aSeed);
        if (n > 0) hash = Checksum(payload, n, hash);
        if (System.BitConverter.ToUInt32(tail, 0) != hash) return RecordRead.Damaged;

        d = new WorldDelta(
            System.BitConverter.ToUInt32(scratch, 0),
            (DeltaKind)scratch[4],
            System.BitConverter.ToUInt64(scratch, 5),
            new Vector3(System.BitConverter.ToSingle(scratch, 15), System.BitConverter.ToSingle(scratch, 19), System.BitConverter.ToSingle(scratch, 23)),
            new Quaternion(System.BitConverter.ToSingle(scratch, 27), System.BitConverter.ToSingle(scratch, 31), System.BitConverter.ToSingle(scratch, 35), System.BitConverter.ToSingle(scratch, 39)),
            System.BitConverter.ToInt32(scratch, 43),
            scratch[47],
            fixedBytes > FixedBytesV1 ? System.BitConverter.ToSingle(scratch, 48) : 1f,
            payload);
        return RecordRead.Ok;
    }

    // A single Read may return fewer bytes than asked for, so a short read is not by itself damage.
    static bool ReadExactly(Stream s, byte[] buffer, int count, int have = 0)
    {
        while (have < count)
        {
            int read = s.Read(buffer, have, count - have);
            if (read <= 0) return false;
            have += read;
        }
        return true;
    }

    const uint Fnv1aSeed = 2166136261u;

    // FNV-1a, resumable across the fixed part and the payload. Catches a torn tail and bit rot; it is not a
    // security check.
    static uint Checksum(byte[] b, int length, uint hash)
    {
        for (int i = 0; i < length; i++) { hash ^= b[i]; hash *= 16777619u; }
        return hash;
    }

    void Warn(string message) =>
        (_log ?? LoggerProvider.Get())?.Log(LogLevel.Warning, "WorldDeltaLog", message);
}
