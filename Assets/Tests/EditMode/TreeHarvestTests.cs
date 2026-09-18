using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class TreeHarvestTests
    {
        static ScatterPrototypeDto Prototype(GeneratedTree tree) => new ScatterPrototypeDto("Tree", 0, 5,
            BiomeType.Forest, 1, 1, 45, 5, 0, false, 0, false, 0, 0, false, Vector2.one,
            true, ScatterInteraction.Chop, Array.Empty<ScatterPartDto>()) { Tree = tree };

        [TestCase(TreeDefLibrary.TreeSpecies.Broadleaf)]
        [TestCase(TreeDefLibrary.TreeSpecies.Conifer)]
        [TestCase(TreeDefLibrary.TreeSpecies.Birch)]
        [TestCase(TreeDefLibrary.TreeSpecies.Palm)]
        public void MatureCutIsReachableAndPartsRetainTheTrunk(TreeDefLibrary.TreeSpecies species)
        {
            using var tree = TreeGenerator.Generate(TreeDefLibrary.Species(species), 719);
            Assert.That(tree.CutPosition.y, Is.EqualTo(.75f).Within(.001f));
            Assert.That(tree.LogSections.Length, Is.InRange(1, TreeHarvestGeometry.MaxSections));
            var bounds = tree.LogSections[0].bounds;
            foreach (var section in tree.LogSections) bounds.Encapsulate(section.bounds);
            Assert.That(Vector3.Distance(bounds.min, tree.Log.bounds.min), Is.LessThan(.1f));
            Assert.That(Vector3.Distance(bounds.max, tree.Log.bounds.max), Is.LessThan(.1f));
            Assert.That(tree.WoodYield, Is.GreaterThanOrEqualTo(tree.LogSections.Length));
        }

        [Test]
        public void CappedTubeFacesOutwardAndHasASeparateUvSeam()
        {
            Mesh mesh = TreeTubeMesher.BuildCappedTube(new[]{Vector3.zero,Vector3.up*3},new[]{1f,1f},6);
            try
            {
                var vertices = mesh.vertices; var triangles = mesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    Vector3 a=vertices[triangles[i]], b=vertices[triangles[i+1]], c=vertices[triangles[i+2]];
                    Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                    Vector3 center=(a+b+c)/3f;
                    if(Mathf.Abs(center.y)<.001f) Assert.That(normal.y,Is.LessThan(-.99f));
                    else if(Mathf.Abs(center.y-3f)<.001f) Assert.That(normal.y,Is.GreaterThan(.99f));
                    else Assert.That(Vector3.Dot(normal,new Vector3(center.x,0,center.z)),Is.GreaterThan(0));
                }
                Assert.That(Vector3.Distance(vertices[0],vertices[6]),Is.LessThan(.001f));
                Assert.That(mesh.uv[6].x-mesh.uv[0].x,Is.EqualTo(1f));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void PlacementUsesTheRootRadiusInsteadOfTheCrownWidth()
        {
            using var tree = TreeGenerator.Generate(TreeDefLibrary.Broadleaf(), 719);
            var prototype = Prototype(tree);
            Assert.That(prototype.GroundContactRadius(), Is.EqualTo(tree.TrunkRadii[0]));
            Assert.That(prototype.GroundContactRadius(), Is.LessThan(tree.Bark.bounds.extents.x));
        }

        [Test]
        public void EverySpeciesGeneratesFiniteHarvestPartsAcrossAges()
        {
            foreach (var species in TreeDefLibrary.AllSpecies)
            foreach (float age in new[] { .25f, .8f, 1f })
            {
                using var tree = TreeGenerator.Generate(TreeDefLibrary.Species(species, age), 719);
                foreach (var mesh in tree.LogSections)
                foreach (Vector3 vertex in mesh.vertices)
                    Assert.IsTrue(float.IsFinite(vertex.x) && float.IsFinite(vertex.y) && float.IsFinite(vertex.z), $"{species} {age}");
                Assert.That(tree.CutPosition.y, Is.LessThanOrEqualTo(.751f));
            }
        }

        [TestCase(TreeDefLibrary.TreeSpecies.Conifer)]
        [TestCase(TreeDefLibrary.TreeSpecies.Broadleaf)]
        public void GroundBranchesBreakWithoutHoldingTheTrunkUp(TreeDefLibrary.TreeSpecies species)
        {
            using var tree = TreeGenerator.Generate(TreeDefLibrary.Species(species, .8f), 719);
            TreeFallSystem.SolveRest(tree, tree.CutPosition, Quaternion.identity, 1, Vector3.up, Vector3.forward,
                p => p.y, out Vector3 position, out Quaternion rotation);
            Vector3 trunk = tree.TrunkPoints[^1] - tree.CutPosition;
            Assert.That(Mathf.Abs((rotation * trunk.normalized).y), Is.LessThan(.12f));
            uint broken = TreeFallSystem.GroundContactBranches(tree, position, rotation, 1, p => p.y);
            Assert.That(broken, Is.Not.Zero);
            for (int i = 0; i < tree.BranchSupport.Length; i++)
            foreach (Vector3 support in tree.BranchSupport[i])
                if ((position + rotation * support).y <= .035f)
                    Assert.That(broken & (1u << i), Is.Not.Zero, "Every penetrating branch must break.");
            Assert.That(TreeFallSystem.GroundContactBranches(tree, position + Vector3.up * 100, rotation, 1, p => p.y), Is.Zero);
        }

        [TestCase(.6f, -.25f)]
        [TestCase(1.8f, .3f)]
        public void TrunkRestAndBranchContactRespectScaleSlopeAndPlanetUp(float scale, float slope)
        {
            using var tree = TreeGenerator.Generate(TreeDefLibrary.Broadleaf(.8f), 719);
            Quaternion planet = Quaternion.Euler(31, 54, 19);
            Vector3 up = planet * Vector3.up, forward = planet * Vector3.forward;
            Vector3 origin = new Vector3(21, -5, 13);
            float Ground(Vector3 p) => Vector3.Dot(p - origin, up) - slope * Vector3.Dot(p - origin, forward);
            TreeFallSystem.SolveRest(tree, origin + planet * tree.CutPosition * scale, planet, scale, up, forward,
                Ground, out Vector3 position, out Quaternion rotation);
            Vector3 trunkDirection = rotation * (tree.TrunkPoints[^1] - tree.CutPosition).normalized;
            Assert.That(Vector3.Dot(trunkDirection, (forward + up * slope).normalized), Is.GreaterThan(.98f));
            float minimum = float.MaxValue;
            for (int i = 0; i < tree.TrunkPoints.Length; i++)
            {
                if (tree.TrunkPoints[i].y < tree.CutPosition.y) continue;
                float gap = Ground(position + rotation * ((tree.TrunkPoints[i] - tree.CutPosition) * scale)) - tree.TrunkRadii[i] * scale;
                Assert.That(gap, Is.GreaterThanOrEqualTo(-.001f));
                minimum = Mathf.Min(minimum, gap);
            }
            Assert.That(minimum, Is.EqualTo(0).Within(.001f));
            Assert.That(TreeFallSystem.GroundContactBranches(tree, position, rotation, scale, Ground), Is.Not.Zero);
        }

        [Test]
        public void AutomaticBranchLossPersistsAndDoesNotChangeTotalWood()
        {
            using var tree = TreeGenerator.Generate(TreeDefLibrary.Broadleaf(.8f), 719);
            var library = new ScatterLibraryDto(new[]{Prototype(tree)});
            var store = new ScatterHarvestStore();
            TreeFallSystem.SolveRest(tree, tree.CutPosition, Quaternion.identity, 1, Vector3.up, Vector3.forward,
                p => p.y, out Vector3 position, out Quaternion rotation);
            ulong id = store.RecordLog(position, rotation, 1, 0, true);
            store.TryGetLog(id, out var record);
            record.RemovedBranches = TreeFallSystem.GroundContactBranches(tree, position, rotation, 1, p => p.y);
            Assert.IsTrue(store.UpdateLog(record));
            store.TryGetLog(id, out var saved);
            Assert.That(saved.RemovedBranches, Is.EqualTo(record.RemovedBranches));
            int wood = 0;
            var service = new TreeHarvestService(store, () => library, (_, n) => wood += n);
            foreach (Vector3 anchor in tree.SectionAnchors)
                for (int hit = 0; hit < 10; hit++) service.Strike(id, position + rotation * anchor, ToolTier.BasicAxe);
            Assert.That(wood, Is.EqualTo(tree.WoodYield));
        }

        [Test]
        public void NeedleFoliageHasUsableNormals()
        {
            foreach (var species in new[] { TreeDefLibrary.TreeSpecies.Conifer, TreeDefLibrary.TreeSpecies.Pine })
            {
                using var tree = TreeGenerator.Generate(TreeDefLibrary.Species(species), 719);
                foreach (var normal in tree.Foliage.normals)
                    Assert.That(normal.sqrMagnitude, Is.GreaterThan(.9f), species.ToString());
            }
        }

        [Test]
        public void DeadConifersKeepTheirSpeciesAndHaveNoFoliage()
        {
            var def=TreeDefLibrary.DeadSpecies(TreeDefLibrary.TreeSpecies.Conifer);
            Assert.That(def.Name,Is.EqualTo("Conifer"));
            using var tree=TreeGenerator.Generate(def,719);
            Assert.That(tree.Foliage.vertexCount,Is.EqualTo(0));
            Assert.That(tree.BranchBark[0].vertexCount+tree.BranchBark[1].vertexCount+tree.BranchBark[2].vertexCount,Is.GreaterThan(0));
            var direct=TreeDefLibrary.Conifer(); direct.Dead=true;
            using var directTree=TreeGenerator.Generate(direct,719);
            Assert.That(directTree.Foliage.vertexCount,Is.EqualTo(0));
        }

        [Test]
        public void GeneratorRejectsInvalidDimensionsAndPreservesRandomState()
        {
            var def=TreeDefLibrary.Conifer(); def.Age=float.NaN;
            Assert.Throws<ArgumentException>(()=>TreeGenerator.Generate(def,1));
            def=TreeDefLibrary.Conifer(); def.Levels[0]=null;
            Assert.Throws<ArgumentException>(()=>TreeGenerator.Generate(def,1));
            UnityEngine.Random.InitState(52); var state=UnityEngine.Random.state;
            float expected=UnityEngine.Random.value; UnityEngine.Random.state=state;
            using var tree=TreeGenerator.Generate(TreeDefLibrary.Conifer(),719);
            Assert.That(UnityEngine.Random.value,Is.EqualTo(expected));
        }

        [Test]
        public void TrunkCanBeHitAboveItsBaseAtDifferentPlanetOrientations()
        {
            using var tree=TreeGenerator.Generate(TreeDefLibrary.Conifer(.25f),719);
            var matrix=Matrix4x4.TRS(new Vector3(20,30,40),Quaternion.Euler(28,60,45),Vector3.one*1.2f);
            var ray=new Ray(matrix.MultiplyPoint3x4(new Vector3(0,2,-3)),matrix.MultiplyVector(Vector3.forward));
            Assert.IsTrue(ScatterPicker.TryPickTrunk(tree,matrix,ray,5,.2f,out float distance));
            Assert.That(distance,Is.InRange(0,5));
        }

        [Test]
        public void FellingDefersWoodAndPersistentHealthSurvivesServiceReplacement()
        {
            using var tree=TreeGenerator.Generate(TreeDefLibrary.Conifer(),719);
            var store=new ScatterHarvestStore(); int wood=0;
            HarvestService Service()=>new HarvestService(p=>store.RecordStump(p),(_,__)=>{},(_,count)=>wood+=count,
                _=>new ProtoHarvestInfo(ScatterInteraction.Chop,"Tree",tree.ChopHp,new HarvestYield("Wood",0)),store.RecordDug,
                readHealth:store.RemainingHealth,writeHealth:store.RecordDamage);
            var pick=new ScatterPick(11,0,Vector3.zero,Quaternion.identity,1);
            Assert.That(Service().TryHarvest(pick,ToolTier.BasicAxe).Outcome,Is.EqualTo(HarvestOutcome.Hit));
            for(int i=1;i<tree.ChopHp;i++) Service().TryHarvest(pick,ToolTier.BasicAxe);
            Assert.IsTrue(store.Contains(11)); Assert.That(wood,Is.Zero);
            Assert.That(Service().TryHarvest(pick,ToolTier.BasicAxe).Outcome,Is.EqualTo(HarvestOutcome.AlreadyHarvested));
        }

        [Test]
        public void BranchesSectionsAndPartialDamageSurviveCompactionWithoutDuplicateWood()
        {
            string directory=Path.Combine(Application.temporaryCachePath,"TreeHarvest-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using var tree=TreeGenerator.Generate(TreeDefLibrary.Conifer(.8f),719);
                var library=new ScatterLibraryDto(new[]{Prototype(tree)});
                int wood=0; ulong id;
                using(var log=new WorldDeltaLog())
                {
                    log.Open(directory,"test"); var store=new ScatterHarvestStore(); store.Configure(872019,log);
                    id=store.RecordLog(Vector3.zero,Quaternion.Euler(90,0,0),1,0,true);
                    var service=new TreeHarvestService(store,()=>library,(_,count)=>wood+=count);
                    for(int i=0;i<3;i++) service.Strike(id,Vector3.zero,ToolTier.BasicAxe);
                    Assert.That(wood,Is.Zero);
                    service.Strike(id,Vector3.zero,ToolTier.BasicAxe);
                    store.RecordDamage(123,2); log.Compact();
                }
                using(var log=new WorldDeltaLog())
                {
                    log.Open(directory,"test"); var store=new ScatterHarvestStore(); store.Configure(872019,log);
                    Assert.That(store.RemainingHealth(123),Is.EqualTo(2));
                    Assert.IsTrue(store.TryGetLog(id,out var saved));
                    Assert.That(saved.RemovedBranches,Is.EqualTo(7u));
                    Assert.That(saved.SectionDamage[0],Is.GreaterThan(0));
                    var service=new TreeHarvestService(store,()=>library,(_,count)=>wood+=count);
                    for(int section=0;section<tree.LogSections.Length;section++)
                    {
                        Vector3 point=saved.Position+saved.Rotation*tree.SectionAnchors[section];
                        for(int hit=0;hit<8;hit++) service.Strike(id,point,ToolTier.BasicAxe);
                    }
                    Assert.That(wood,Is.EqualTo(tree.WoodYield));
                    Assert.That(service.Strike(id,Vector3.zero,ToolTier.BasicAxe).Outcome,Is.EqualTo(HarvestOutcome.AlreadyHarvested));
                    Assert.That(wood,Is.EqualTo(tree.WoodYield));
                    Assert.IsFalse(service.TryPick(new Ray(new Vector3(0,0,-3),Vector3.forward),10,.2f,out _,out _));
                }
            }
            finally { Directory.Delete(directory,true); }
        }

        [Test]
        public void RestingPoseSupportsTheTrunkOnFlatGround()
        {
            using var tree=TreeGenerator.Generate(TreeDefLibrary.Conifer(.8f),719);
            TreeFallSystem.SolveRest(tree,tree.CutPosition,Quaternion.identity,1,Vector3.up,Vector3.forward,
                p=>p.y,out Vector3 position,out Quaternion rotation);
            for(int i=0;i<tree.TrunkPoints.Length;i++)
            {
                if(tree.TrunkPoints[i].y<tree.CutPosition.y) continue;
                Vector3 p=position+rotation*(tree.TrunkPoints[i]-tree.CutPosition);
                Assert.That(p.y-tree.TrunkRadii[i],Is.GreaterThanOrEqualTo(-.001f));
            }
        }
    }
}
