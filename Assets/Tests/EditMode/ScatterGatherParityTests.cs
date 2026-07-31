using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // The load-bearing determinism guard for the Burst gather: for representative tiles across biomes,
    // the parallel ScatterGatherJob must produce the EXACT same instance set as the managed reference
    // ScatterField.GatherTilePrototype. IDs must match as a set (any diff = an acceptance-threshold drift
    // that would move props); per-ID transforms must match within the same epsilon scatter.verify uses.
    public sealed class ScatterGatherParityTests
    {
        // Deterministic two-biome field with a blend border near dir.x == 0, plus a biome (Desert) no
        // tile ever resolves — exercises accept, blend, and reject-all paths without the heavy Voronoi build.
        sealed class BiomeStub : IBiomeProvider
        {
            public BiomeResult EvaluateBiome(Vector3 dir, float elevation)
            {
                float m = Mathf.Clamp01((dir.x + 1f) * 0.5f);
                BiomeType primary, secondary; float blend;
                if (m < 0.5f) { primary = BiomeType.Grassland; secondary = BiomeType.Forest; blend = Mathf.Clamp01((m - 0.4f) / 0.2f) * 0.5f; }
                else { primary = BiomeType.Forest; secondary = BiomeType.Grassland; blend = Mathf.Clamp01((0.6f - m) / 0.2f) * 0.5f; }
                return new BiomeResult { PrimaryBiome = primary, SecondaryBiome = secondary, BlendWeight = blend, Temperature = 0.5f, Moisture = 0.5f };
            }
            public Color GetBiomeColor(Vector3 p, float e) => Color.green;
            public Color GetBiomeColorAndData(Vector3 p, float e, out Vector4 d) { d = Vector4.zero; return Color.green; }
            public void GetBiomeData(Vector3 p, float e, out Vector4 d) { d = Vector4.zero; }
        }

        const int WorldSeed = 20260731;
        const float PlanetRadius = 5000f;
        const float SeaRadiusLocal = PlanetRadius;
        const bool HasOcean = true;

        ShapeGenerator _shape;
        AnalyticGroundSampler _ground;
        BiomeStub _biome;
        ScatterField _field;
        ScatterLibraryDto _library;
        int[] _levels;
        int _tileLevel;
        GameObject _planetGo;
        PlanetTransformSnapshot _snap;

        [SetUp]
        public void SetUp()
        {
            var noise = new NoiseSettings
            {
                Filter = NoiseSettings.FilterType.Simple,
                Strength = 0.06f, Layers = 4, BaseRoughness = 1.2f, Roughness = 2.1f,
                Persistence = 0.5f, Center = new Vector3(3.1f, -1.7f, 0.4f), MinValue = 0.6f,
            };
            var settings = new ShapeSettings
            {
                PlanetRadius = PlanetRadius,
                NoiseLayers = new[] { new ShapeSettings.NoiseLayer { Enabled = true, UseFirstLayerAsMask = false, NoiseSettings = noise } },
                DiagnosticTerrainLayout = null,
            };
            _shape = new ShapeGenerator();
            _shape.Configure(settings);
            _shape.Initialize(WorldSeed);
            _ground = new AnalyticGroundSampler(_shape);
            _biome = new BiomeStub();
            _planetGo = new GameObject("ScatterParityPlanet");
            _snap = PlanetTransformSnapshot.Capture(_planetGo.transform);
            _field = new ScatterField(_planetGo.transform, _ground, _biome);

            _library = new ScatterLibraryDto(new[]
            {
                Proto("Grass", 0, 3f, BiomeType.Grassland, weight: 1.0f, maxSlope: 32f, fade: 8f, blendPower: 1.5f, randomYaw: true, hasMaxAlt: true, maxAlt: 150f),
                Proto("Trees", 1, 14f, BiomeType.Forest, weight: 0.85f, maxSlope: 35f, fade: 8f, blendPower: 2.0f, randomYaw: true, hasMaxAlt: false, maxAlt: 0f),
                Proto("Desert", 2, 8f, BiomeType.Desert, weight: 1.0f, maxSlope: 30f, fade: 10f, blendPower: 1.0f, randomYaw: false, hasMaxAlt: false, maxAlt: 0f),
            });
            _library.EnsureValid();

            float worldRadius = PlanetRadius * _snap.UniformScale;
            _levels = new int[_library.Prototypes.Length];
            int minLevel = int.MaxValue;
            for (int i = 0; i < _levels.Length; i++)
            {
                _levels[i] = ScatterQuadtree.LevelForSpacing(worldRadius, _library.Prototypes[i].SpacingMeters);
                minLevel = Mathf.Min(minLevel, _levels[i]);
            }
            _tileLevel = Mathf.Clamp(Mathf.Min(7, minLevel), 0, 7);
        }

        [TearDown]
        public void TearDown()
        {
            _field?.Dispose();
            if (_planetGo != null) Object.DestroyImmediate(_planetGo);
        }

        static ScatterPrototypeDto Proto(string name, int slot, float spacing, BiomeType biome, float weight,
            float maxSlope, float fade, float blendPower, bool randomYaw, bool hasMaxAlt, float maxAlt)
            => new ScatterPrototypeDto(name, slot, spacing, biome, blendPower, weight, maxSlope, fade,
                false, 0f, hasMaxAlt, maxAlt, 0f, new Vector2(1f, 1.6f), randomYaw,
                ScatterInteraction.None, System.Array.Empty<ScatterPartDto>());

        [Test]
        public void BurstGather_MatchesManaged_AcrossBiomesAndTiles()
        {
            // Tiles spanning the dir.x biome border on face 0, plus single-biome faces (2 = -x Grassland,
            // 3 = +x Forest), across all three prototypes (dense grass, sparse trees, reject-all desert).
            var pairs = new List<ScatterPairInput>();
            void AddTile(int face, int tx, int ty)
            {
                for (int p = 0; p < _library.Prototypes.Length; p++)
                    pairs.Add(new ScatterPairInput { Face = face, TileX = tx, TileY = ty, Level = _levels[p], ProtoIndex = p });
            }
            int[] borderTx = { 40, 60, 63, 64, 66, 90 };
            foreach (int tx in borderTx) AddTile(0, tx, 64);
            AddTile(2, 64, 64);
            AddTile(3, 64, 64);
            AddTile(0, 10, 100);

            // --- managed reference ---
            var ctx = new ScatterField.GatherContext(_library, _levels, WorldSeed, PlanetRadius, SeaRadiusLocal, HasOcean);
            var managed = new List<ScatterInstance>[pairs.Count];
            int totalManaged = 0, emptyPairs = 0, densest = 0;
            for (int i = 0; i < pairs.Count; i++)
            {
                var buf = new List<ScatterInstance>(4096);
                var pr = pairs[i];
                _field.GatherTilePrototype(ctx, _snap, pr.Face, pr.TileX, pr.TileY, _tileLevel, pr.ProtoIndex, buf, out _);
                managed[i] = buf;
                totalManaged += buf.Count;
                if (buf.Count == 0) emptyPairs++;
                densest = Mathf.Max(densest, buf.Count);
            }

            // --- Burst job over the same pairs ---
            var pairArr = new NativeArray<ScatterPairInput>(pairs.ToArray(), Allocator.TempJob);
            var noiseLayers = _shape.BuildNoiseFilterData(Allocator.TempJob);
            var diagCells = _shape.BuildDiagnosticTerrainCells(Allocator.TempJob);
            var protos = new NativeArray<ScatterProtoParams>(_library.Prototypes.Length, Allocator.TempJob);
            for (int p = 0; p < protos.Length; p++) protos[p] = ScatterProtoParams.From(_library.Prototypes[p]);
            int cellCap = Mathf.Max(1, ScatterBiomePrecompute.CountCells(pairArr, pairArr.Length, _tileLevel));
            var biomeMap = new NativeParallelHashMap<long, ScatterBiomeSample>(cellCap, Allocator.TempJob);
            ScatterBiomePrecompute.Build(pairArr, pairArr.Length, _tileLevel, _ground, _biome, PlanetRadius, biomeMap);
            var stream = new NativeStream(pairArr.Length, Allocator.TempJob);

            var job = new ScatterGatherJob
            {
                Pairs = pairArr,
                NoiseLayers = noiseLayers,
                DiagCells = diagCells,
                DiagData = _shape.DiagnosticTerrainData,
                Protos = protos,
                Biome = biomeMap,
                Snap = _snap,
                WorldSeed = WorldSeed,
                TileLevel = _tileLevel,
                BaseRadiusLocal = PlanetRadius,
                SeaRadiusLocal = SeaRadiusLocal,
                PlanetRadius = PlanetRadius,
                Scale = _snap.UniformScale,
                HasOcean = HasOcean ? (byte)1 : (byte)0,
                Out = stream.AsWriter(),
            };
            job.Schedule(pairArr.Length, 1).Complete();

            var jobByPair = new List<ScatterInstance>[pairs.Count];
            var reader = stream.AsReader();
            for (int i = 0; i < pairs.Count; i++)
            {
                var list = new List<ScatterInstance>();
                int n = reader.BeginForEachIndex(i);
                for (int k = 0; k < n; k++) list.Add(reader.Read<ScatterInstance>());
                reader.EndForEachIndex();
                jobByPair[i] = list;
            }

            // --- compare per pair, then dispose ---
            try
            {
                for (int i = 0; i < pairs.Count; i++)
                {
                    var mMap = new Dictionary<ulong, ScatterInstance>(managed[i].Count);
                    foreach (var inst in managed[i])
                        Assert.IsTrue(mMap.TryAdd(inst.Id, inst), $"pair {i}: managed duplicate id {inst.Id}");
                    var jMap = new Dictionary<ulong, ScatterInstance>(jobByPair[i].Count);
                    foreach (var inst in jobByPair[i])
                        Assert.IsTrue(jMap.TryAdd(inst.Id, inst), $"pair {i}: burst duplicate id {inst.Id}");

                    var mIds = new HashSet<ulong>(mMap.Keys);
                    var jIds = new HashSet<ulong>(jMap.Keys);
                    if (!mIds.SetEquals(jIds))
                    {
                        int missing = 0; foreach (var id in mIds) if (!jIds.Contains(id)) missing++;
                        int extra = 0; foreach (var id in jIds) if (!mIds.Contains(id)) extra++;
                        Assert.Fail($"pair {i} (face {pairs[i].Face} tile {pairs[i].TileX},{pairs[i].TileY} proto {pairs[i].ProtoIndex}): " +
                                    $"id-set mismatch — managed {mIds.Count}, burst {jIds.Count}, {missing} missing, {extra} extra");
                    }

                    foreach (var kv in mMap)
                    {
                        var m = kv.Value; var j = jMap[kv.Key];
                        // Position/rotation/scale are POST-acceptance (never feed a threshold), so they only
                        // need epsilon parity. Burst-compiled noise differs from the managed-IL reference by
                        // ~1-2 ULP, which at this 5000 m test radius is ~1 mm of position — hence a
                        // scale-aware bound (0.05 m), far below anything visible and far above the float
                        // noise. The exact ID-set match above is the real determinism guarantee.
                        Assert.Less((m.PositionWS - j.PositionWS).sqrMagnitude, 2.5e-3f, $"pair {i} id {kv.Key}: position drift {(m.PositionWS - j.PositionWS).magnitude:R} m");
                        Assert.Less(Quaternion.Angle(m.Rotation, j.Rotation), 0.05f, $"pair {i} id {kv.Key}: rotation drift");
                        Assert.Less(Mathf.Abs(m.Scale - j.Scale), 1e-4f, $"pair {i} id {kv.Key}: scale drift");
                        Assert.AreEqual(m.PrototypeIndex, j.PrototypeIndex, $"pair {i} id {kv.Key}: prototype index");
                    }
                }

                // Sanity: the fixture actually exercised accept, dense, and reject-all paths.
                Assert.Greater(totalManaged, 0, "no instances placed — fixture is not exercising the gather");
                Assert.Greater(emptyPairs, 0, "expected at least one empty (Desert) pair");
                Assert.Greater(densest, 50, "expected at least one dense pair");
            }
            finally
            {
                pairArr.Dispose();
                noiseLayers.Dispose();
                diagCells.Dispose();
                protos.Dispose();
                biomeMap.Dispose();
                stream.Dispose();
            }
        }
    }
}
