using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

public static class RiverGenerator
{
    public static RiverField Build(WaterBodyMap bodies, float radius, CancellationToken ct = default, WaterDto settings = null)
    {
        var segments = new List<RiverSegment>();
        if (settings != null && !settings.RiversEnabled) return new RiverField(segments, radius);
        float catchment = settings?.RiverCatchmentFraction ?? .00045f;
        float halfWidth = settings?.RiverHalfWidth ?? 5f;
        float channelDepth = settings?.RiverDepth ?? 2f;
        float minDrop = settings?.WaterfallMinDrop ?? 8f;
        if (!float.IsFinite(catchment) || catchment < .00005f || catchment > .01f ||
            !float.IsFinite(halfWidth) || halfWidth < 1f || halfWidth > 50f ||
            !float.IsFinite(channelDepth) || channelDepth < .25f || channelDepth > 20f ||
            !float.IsFinite(minDrop) || minDrop < 1f || minDrop > 200f)
            throw new System.ArgumentException("River settings exceed the supported generation bounds.");
        var drainage = bodies?.Drainage;
        if (drainage == null) return new RiverField(segments, radius);
        int count = drainage.Filled.Length;
        var area = new double[count];
        var distance = new float[count];
        var identity = new int[count];
        var heights = new float[count];
        var blocked = new bool[count];
        int nextId = bodies.Bodies.Bodies.Count + 1;
        int res = WaterBodyMap.Resolution;
        for (int i = 0; i < count; i++)
        {
            Vector3 d = WaterBodyMap.CellDirection(i);
            double dominant = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
            area[i] = 4.0 * radius * radius / (res * res) * dominant * dominant * dominant;
            heights[i] = radius * (1f + drainage.Filled[i]);
        }
        for (int i = drainage.Order.Length - 1; i >= 0; i--)
        {
            int cell = drainage.Order[i], receiver = drainage.Receiver[cell];
            if (receiver >= 0) area[receiver] += area[cell];
        }
        // Fraction of planetary area: density stays stable when the authored radius changes.
        double threshold = 4.0 * System.Math.PI * radius * radius * catchment;
        float scale = radius / 5000f;
        foreach (int cell in drainage.Order)
        {
            ct.ThrowIfCancellationRequested();
            int receiver = drainage.Receiver[cell];
            if (receiver < 0) continue;
            Vector3 a = WaterBodyMap.CellDirection(cell), b = WaterBodyMap.CellDirection(receiver);
            float length = Vector3.Distance(a, b) * radius;
            distance[cell] = distance[receiver] + length;
            identity[cell] = identity[receiver];
            bool wetA = bodies.TrySolvedLevelAt(a, out float levelA);
            bool wetB = bodies.TrySolvedLevelAt(b, out float levelB);
            // Small depressions intentionally drained by the lake policy have no supporting water body.
            // Do not draw the discarded spill plane as a suspended river through that basin.
            blocked[cell] = !wetA && (blocked[receiver] || drainage.Filled[cell] > drainage.Elevation[cell] + 1e-6f);
            if (blocked[cell] || area[cell] < threshold) continue;
            if (wetA && wetB) continue;
            if (identity[cell] == 0)
            {
                if (nextId > ushort.MaxValue) break;
                identity[cell] = nextId++;
            }
            float lower = wetB ? radius * (1f + levelB) : heights[receiver] - .5f * scale;
            float upper = wetA ? radius * (1f + levelA) : heights[cell] - .5f * scale;
            if (upper < lower) upper = lower;
            float width = Mathf.Clamp((float)System.Math.Sqrt(area[cell] / threshold) * halfWidth, halfWidth, halfWidth * 4f) * scale;
            float depth = Mathf.Clamp(width / halfWidth * channelDepth, channelDepth * scale, channelDepth * 2f * scale);
            float drop = upper - lower;
            // Concentrate a steep descent into a carved step; do not require an existing vertical cliff.
            bool fall = drop > minDrop * scale && drop > length * .35f;
            float speed = Mathf.Clamp(1f + drop / Mathf.Max(length, .01f) * 8f, 1f, 8f);
            if (fall)
            {
                // A short lip feeds a steep sheet and a lower run. The three sections share endpoints.
                Vector3 lip = Vector3.Slerp(a, b, .02f).normalized;
                Vector3 landing = Vector3.Slerp(a, b, .20f).normalized;
                float lipRadius = Mathf.Lerp(upper, lower, .02f);
                Add(a, lip, upper, lipRadius, 0f, distance[cell], distance[cell] - length * .02f);
                Add(lip, landing, lipRadius, lower, 1f, distance[cell] - length * .02f, distance[cell] - length * .20f);
                Add(landing, b, lower, lower, 0f, distance[cell] - length * .20f, distance[receiver]);
            }
            else Add(a, b, upper, lower, 0f, distance[cell], distance[receiver]);

            void Add(Vector3 start, Vector3 end, float r0, float r1, float waterfall, float d0, float d1)
            {
                segments.Add(new RiverSegment
                {
                    A = new float4(start.x, start.y, start.z, r0), B = new float4(end.x, end.y, end.z, r1),
                    Shape = new float4(width, depth, speed, waterfall),
                    Flow = new float4(identity[cell], -d0, -d1, width * 1.8f)
                });
            }
        }
        RoundBends(segments, ct);
        ShapeWidths(segments, radius, ct);
        return new RiverField(segments, radius);
    }

    public static void ShapeWidths(List<RiverSegment> segments, float radius, CancellationToken ct = default)
    {
        var parents = new int[segments.Count];
        var nodes = new Dictionary<float3, int>();
        for (int i = 0; i < segments.Count; i++) parents[i] = i;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].Shape.w > 0f) continue;
            Join(segments[i].A.xyz, i); Join(segments[i].B.xyz, i);
        }
        var widths = new Dictionary<float3, float>();
        var received = new HashSet<float3>();
        var impacts = new HashSet<float3>();
        var lips = new HashSet<float3>();
        foreach (var segment in segments)
        {
            Include(segment.A.xyz, segment.Shape.x);
            Include(segment.B.xyz, segment.Shape.x);
            received.Add(segment.B.xyz);
            if (segment.Shape.w > 0f) { impacts.Add(segment.B.xyz); lips.Add(segment.A.xyz); }
        }
        for (int i = 0; i < segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var s = segments[i];
            s.Profile.z = Root(i) + 1;
            s.Profile.w = s.Shape.w > 0f ? 0f : (impacts.Contains(s.A.xyz) ? 1 : 0) + (lips.Contains(s.B.xyz) ? 2 : 0);
            float originalWidth = s.Shape.x;
            s.Shape.x = widths[s.A.xyz] * (received.Contains(s.A.xyz) ? 1f : .45f);
            s.Profile.x = widths[s.B.xyz];
            s.Flow.w = s.Shape.x * 1.8f;
            float length = math.distance(s.A.xyz, s.B.xyz) * radius;
            float grade = (s.A.w - s.B.w) / math.max(length, .01f);
            // Broaden quiet reaches and waterfall receiving runs into carved pools.
            // The profile retains the downhill radius; it cannot introduce an uphill outlet.
            if (s.Shape.w == 0f && grade < .002f &&
                (impacts.Contains(s.A.xyz) || (i % 17 == 0 && length > originalWidth * 6f)))
            {
                s.Profile.y = originalWidth * (impacts.Contains(s.A.xyz) ? .9f : 1.5f);
                s.Shape.y *= 1.4f;
                s.Shape.z *= .55f;
            }
            segments[i] = s;
        }
        void Include(float3 node, float width)
        {
            widths.TryGetValue(node, out float current);
            widths[node] = math.max(current, width);
        }
        int Root(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }
        void Join(float3 node, int index)
        {
            if (nodes.TryGetValue(node, out int other)) parents[Root(index)] = Root(other);
            else nodes[node] = index;
        }
    }

    // Preserve the lip and receiving run as well as the falling sheet.
    static HashSet<float3> WaterfallSupportNodes(IReadOnlyList<RiverSegment> segments)
    {
        var fallEndpoints = new HashSet<float3>();
        foreach (var segment in segments)
            if (segment.Shape.w > 0f)
            {
                fallEndpoints.Add(segment.A.xyz);
                fallEndpoints.Add(segment.B.xyz);
            }
        var support = new HashSet<float3>(fallEndpoints);
        foreach (var segment in segments)
            if (fallEndpoints.Contains(segment.A.xyz) || fallEndpoints.Contains(segment.B.xyz))
            {
                support.Add(segment.A.xyz);
                support.Add(segment.B.xyz);
            }
        return support;
    }

    // Fair entire unbranched reaches together; anchors retain their solved drainage positions.
    public static void RoundBends(List<RiverSegment> destination, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var segments = new List<RiverSegment>(destination);
        var waterfallSupport = WaterfallSupportNodes(segments);
        var nodes = new Dictionary<float3, int>();
        var original = new List<float3>();
        var incoming = new List<List<int>>();
        var outgoing = new List<List<int>>();
        var starts = new int[segments.Count];
        var ends = new int[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            starts[i] = Node(segments[i].A.xyz);
            ends[i] = Node(segments[i].B.xyz);
            outgoing[starts[i]].Add(i);
            incoming[ends[i]].Add(i);
        }
        var current = original.ToArray();
        var next = new float3[current.Length];
        var movable = new bool[current.Length];
        var limits = new float[current.Length];
        for (int n = 0; n < current.Length; n++)
        {
            if (waterfallSupport.Contains(original[n])) continue;
            if (incoming[n].Count != 1 || outgoing[n].Count != 1) continue;
            int a = incoming[n][0], b = outgoing[n][0];
            if (a == b || segments[a].Shape.w > 0f || segments[b].Shape.w > 0f ||
                segments[a].Flow.x != segments[b].Flow.x) continue;
            movable[n] = true;
            limits[n] = .45f * math.min(math.distance(original[n], original[starts[a]]),
                math.distance(original[n], original[ends[b]]));
        }
        // Jacobi updates make the result independent of dictionary traversal order.
        // The displacement budget keeps the new path near its solved drainage corridor.
        for (int pass = 0; pass < 8; pass++)
        {
            ct.ThrowIfCancellationRequested();
            for (int n = 0; n < current.Length; n++)
            {
                next[n] = current[n];
                if (!movable[n]) continue;
                int left = starts[incoming[n][0]], right = ends[outgoing[n][0]];
                float leftLength = math.distance(original[n], original[left]);
                float rightLength = math.distance(original[n], original[right]);
                float t = leftLength / math.max(leftLength + rightLength, 1e-12f);
                float3 target = math.normalizesafe(math.lerp(current[left], current[right], t), original[n]);
                float3 candidate = math.normalizesafe(math.lerp(current[n], target, .5f), original[n]);
                // Reject over-budget moves rather than normalizing a clamped chord past its limit.
                if (math.distance(candidate, original[n]) <= limits[n]) next[n] = candidate;
            }
            // Prevent collapsed or reversed edges on short, irregular reaches.
            // Rejection is simultaneous so segment order cannot change the result.
            var rejected = new bool[current.Length];
            for (int i = 0; i < segments.Count; i++)
            {
                float3 before = original[ends[i]] - original[starts[i]];
                float3 after = next[ends[i]] - next[starts[i]];
                if (math.dot(before, after) < .1f * math.lengthsq(before))
                    rejected[starts[i]] = rejected[ends[i]] = true;
            }
            for (int n = 0; n < current.Length; n++)
                if (rejected[n]) next[n] = current[n];
            (current, next) = (next, current);
        }
        ct.ThrowIfCancellationRequested();
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            segment.A.xyz = current[starts[i]];
            segment.B.xyz = current[ends[i]];
            segments[i] = segment;
        }

        TessellateBends(segments, ct);
        ct.ThrowIfCancellationRequested();
        destination.Clear();
        destination.AddRange(segments);

        int Node(float3 position)
        {
            if (nodes.TryGetValue(position, out int id)) return id;
            id = original.Count;
            nodes.Add(position, id);
            original.Add(position);
            incoming.Add(new List<int>());
            outgoing.Add(new List<int>());
            return id;
        }
    }


    // Called after reach fairing, before widths/pools are assigned.
    static void TessellateBends(List<RiverSegment> segments, CancellationToken ct)
    {
        var waterfallSupport = WaterfallSupportNodes(segments);
        var source = segments.ToArray();
        var incoming = new Dictionary<float3, List<int>>();
        var outgoing = new Dictionary<float3, List<int>>();
        for (int i = 0; i < source.Length; i++)
        {
            Add(incoming, source[i].B.xyz, i);
            Add(outgoing, source[i].A.xyz, i);
        }
        foreach (var pair in incoming)
        {
            ct.ThrowIfCancellationRequested();
            if (waterfallSupport.Contains(pair.Key)) continue;
            if (pair.Value.Count != 1 || !outgoing.TryGetValue(pair.Key, out var next) || next.Count != 1) continue;
            int before = pair.Value[0], after = next[0];
            var a = source[before]; var b = source[after];
            if (a.Shape.w > 0 || b.Shape.w > 0 || a.Flow.x != b.Flow.x) continue;
            float3 u = a.B.xyz - a.A.xyz, v = b.B.xyz - b.A.xyz;
            float lu = math.length(u), lv = math.length(v);
            if (math.min(lu, lv) < 1e-8f) continue;
            float angle = math.acos(math.clamp(math.dot(u / lu, v / lv), -1f, 1f));
            if (angle < math.radians(2f)) continue;
            // Equal trim lengths give a quadratic whose tangent angle can be sampled directly.
            // At most twelve segments for a non-reversing bend, versus six at every old corner.
            int count = math.clamp((int)math.ceil(angle / math.radians(15f)), 2, 12);
            var parameters = new float[count + 1];
            parameters[count] = 1f;
            for (int j = 1; j < count; j++)
            {
                float theta = angle * j / count;
                parameters[j] = math.sin(theta) / math.max(math.sin(theta) + math.sin(angle - theta), 1e-8f);
            }
            float trim = .3f * math.min(lu, lv);
            // Quadratic midpoint sagitta scales with trim. Bound it to 5% of the narrower half-width.
            float radius = math.max(a.B.w, 1f);
            float tolerance = math.max(.001f, .05f * math.min(a.Shape.x, b.Shape.x)) / radius;
            float maxError = 0f;
            for (int j = 1; j <= count; j++)
            {
                float span = parameters[j] - parameters[j - 1];
                maxError = math.max(maxError, .25f * span * span * trim * math.length(v / lv - u / lu));
            }
            if (maxError > tolerance) trim *= tolerance / maxError;
            float ta = 1f - trim / lu, tb = trim / lv;
            float4 start = Point(a.A, a.B, ta), end = Point(b.A, b.B, tb), corner = a.B;
            float flowStart = math.lerp(a.Flow.y, a.Flow.z, ta);
            float flowEnd = math.lerp(b.Flow.y, b.Flow.z, tb);
            var first = segments[before]; first.B = start; first.Flow.z = flowStart; segments[before] = first;
            var last = segments[after]; last.A = end; last.Flow.y = flowEnd; segments[after] = last;
            float4 previous = start;
            for (int j = 1; j <= count; j++)
            {
                float t = parameters[j];
                float4 point = j == count ? end : Point(Point(start, corner, t), Point(corner, end, t), t);
                var curve = a;
                curve.A = previous; curve.B = point;
                curve.Shape = math.lerp(a.Shape, b.Shape, t);
                curve.Flow = new float4(a.Flow.x, math.lerp(flowStart, flowEnd, parameters[j - 1]),
                    math.lerp(flowStart, flowEnd, t), math.lerp(a.Flow.w, b.Flow.w, t));
                segments.Add(curve);
                previous = point;
            }
        }
        static float4 Point(float4 a, float4 b, float t)
        {
            var p = math.lerp(a, b, t); p.xyz = math.normalizesafe(p.xyz, a.xyz); return p;
        }
        static void Add(Dictionary<float3, List<int>> map, float3 p, int i)
        {
            if (!map.TryGetValue(p, out var list)) map[p] = list = new List<int>();
            list.Add(i);
        }
    }
}

