using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Birth, and nothing else. A territory is one cube-face cell of the spawn lattice; it declares how many of a
/// species the ground there supports and where each of them starts. Every answer is a pure function of the
/// world seed, so two runs of the same seed produce the same creatures in the same places and none of it costs
/// storage.
/// </summary>
/// <remarks>
/// Once a creature exists the territory has no further say. Whether it is simulated is decided by observation
/// (see <see cref="CreatureResidencyService"/>), and whether it exists at all is decided by its slot's death
/// record - never by how far it has wandered from here. That separation is what lets you chase a deer two
/// kilometres from its valley without it evaporating.
/// </remarks>
public static class CreatureTerritory
{
    /// <summary>
    /// Lattice level. Level 4 is 16x16 cells per cube face - 1,536 territories, about 490 m across on a
    /// 5 km planet, which is roughly a valley.
    /// </summary>
    // ponytail: a fixed lattice, because a uniform grid is the cheapest thing that proves the spine. Biome-
    // suitability placement (design doc section 13 question 1) replaces HomeDirection's cell centre with a
    // scatter-style suitability draw; the id packing already carries the cell, so nothing else moves.
    public const int Level = 4;

    /// <summary>Cells along one edge of a cube face at <see cref="Level"/>.</summary>
    public const int CellsPerFace = 1 << Level;

    public const float CellUvWidth = 1f / CellsPerFace;

    // Homes are drawn inside a slightly inset cell so a float wobble at a face seam cannot flip a home onto
    // the neighbouring face, where it would decode as a different territory.
    const float CellInset = 0.05f;

    /// <summary>
    /// How many places inside its territory a slot will try before giving up. A single draw leaves a slot
    /// empty whenever the one point it picked happens to be sea, rock or the wrong biome - which reads to a
    /// player as "this world has no animals" rather than "this spot is unsuitable". Measured on the first
    /// slice: one draw left the nearest creature 729 m away on a coastal shelf.
    /// </summary>
    public const int HomeAttempts = 5;

    /// <summary>
    /// Planet-LOCAL unit direction of one candidate home for a slot. Stable for the life of the world, and
    /// <paramref name="attempt"/> 0 is the slot's preferred spot - the caller walks the attempts in order and
    /// keeps the first the ground and biome accept, so a species' homes are as clustered as suitability
    /// allows rather than scattered by the retry.
    /// </summary>
    public static Vector3 HomeCandidate(ISeedProvider seeds, EntityId slotKey, int attempt = 0)
    {
        CreatureKey.Unpack(slotKey, out int face, out int level, out int x, out int y, out _, out _);
        uint h = ScatterHash.Mix(unchecked((uint)seeds.GetSeedForEntity(slotKey.Value)));
        float cell = 1f / (1 << level);
        // Slots 0 and 1 for attempt 0 keep it identical to the single-draw version this replaced.
        float u = (x + Mathf.Lerp(CellInset, 1f - CellInset, ScatterHash.To01(ScatterHash.Slot(h, attempt * 2)))) * cell;
        float v = (y + Mathf.Lerp(CellInset, 1f - CellInset, ScatterHash.To01(ScatterHash.Slot(h, attempt * 2 + 1)))) * cell;
        return FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, new Vector2(u, v));
    }

    /// <summary>
    /// Whether a slot is filled right now, and by which generation. With no death record the slot holds its
    /// original occupant; while a death still suppresses it the slot is empty; once the death lapses the slot
    /// repopulates with the NEXT generation - a different animal from the one that was killed.
    /// </summary>
    public static bool TryResolveOccupant(bool hasRecord, in CreatureRecord record, long nowUnixSeconds,
        out int generation)
    {
        if (!hasRecord)
        {
            generation = 0;   // nothing written down: the slot holds exactly what the seed predicts
            return true;
        }
        if (record.Suppresses(nowUnixSeconds))
        {
            generation = record.Generation;
            return false;
        }
        // A displacement describes the CURRENT occupant, so its generation is the live one. A lapsed death
        // describes the previous occupant, so the live one is the next generation. Reading the generation off
        // the record either way is what stops a repopulated slot falling back to 0.
        generation = record.IsDead ? record.NextGeneration : record.Generation;
        return true;
    }

    /// <summary>
    /// Move <paramref name="metres"/> along the great circle from <paramref name="from"/> toward
    /// <paramref name="to"/>, keeping the source radius. This is both the per-tick homing pull and the
    /// fast-forward a re-observed creature gets: an hour unwatched is the same arithmetic as a frame, which is
    /// why nothing has to be simulated in between.
    /// </summary>
    public static Vector3 DriftToward(Vector3 center, Vector3 from, Vector3 to, float metres)
    {
        Vector3 a = from - center;
        Vector3 b = to - center;
        float radius = a.magnitude;
        if (radius < 1e-4f || b.sqrMagnitude < 1e-8f || !(metres > 0f))
            return from;

        Vector3 ua = a / radius;
        Vector3 ub = b.normalized;
        Vector3 axis = Vector3.Cross(ua, ub);
        float arc = Angle(axis, ua, ub) * radius;
        if (arc <= 1e-4f || metres >= arc || axis.sqrMagnitude < 1e-12f)
            return center + ub * radius;   // arrived, or the two are (anti)parallel and have no unique arc
        return center + Quaternion.AngleAxis(metres / radius * Mathf.Rad2Deg, axis.normalized) * ua * radius;
    }

    /// <summary>Great-circle surface distance between two points at the same centre.</summary>
    public static float SurfaceDistance(Vector3 center, Vector3 a, Vector3 b)
    {
        Vector3 da = a - center;
        Vector3 db = b - center;
        float radius = da.magnitude;
        if (radius < 1e-4f || db.sqrMagnitude < 1e-8f)
            return 0f;
        Vector3 ua = da / radius;
        Vector3 ub = db.normalized;
        return Angle(Vector3.Cross(ua, ub), ua, ub) * radius;
    }

    // atan2(|cross|, dot) rather than acos(dot). At the small separations this is asked about constantly - a
    // creature a few metres from home on a 5 km sphere - dot is within a float ulp of 1 and acos throws away
    // most of the precision, which shows up as a drift step of the wrong size. atan2 is well conditioned at
    // both ends.
    static float Angle(Vector3 cross, Vector3 ua, Vector3 ub) =>
        Mathf.Atan2(cross.magnitude, Vector3.Dot(ua, ub));

    /// <summary>
    /// Every territory cell whose centre lies within <paramref name="bubbleRadiusMeters"/> of the observer.
    /// Built on the same cube-face range walker scatter and grass use, so a bubble that straddles a face seam
    /// picks up the neighbouring face rather than stopping at the edge.
    /// </summary>
    public static void CollectNear(Vector3 observerWorldPos, in PlanetTransformSnapshot planet,
        float planetRadius, float bubbleRadiusMeters, FaceSpaceCell[] scratch, List<Vector3Int> into)
    {
        into.Clear();
        FaceSpaceRangeResult ranges = FaceSpaceCellRangeBuilder.BuildRangesLocal(
            observerWorldPos, planet, planetRadius, bubbleRadiusMeters, CellUvWidth, 1, scratch);

        for (int r = 0; r < ranges.Count; r++)
        {
            FaceSpaceCell cell = scratch[r];
            for (int dy = 0; dy < cell.GridSize.y; dy++)
            for (int dx = 0; dx < cell.GridSize.x; dx++)
            {
                int x = cell.PageOriginCellUV.x + dx;
                int y = cell.PageOriginCellUV.y + dy;
                if ((uint)x >= CellsPerFace || (uint)y >= CellsPerFace)
                    continue;
                var entry = new Vector3Int(cell.FaceIndex, x, y);
                if (!into.Contains(entry))     // seam ranges overlap; the set is at most a few dozen cells
                    into.Add(entry);
            }
        }
    }
}
