using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ScatterRenderingRegressionTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void TreeMaterialCache_ReplacesDestroyedMemberAndKeepsSurvivor(bool destroyBark)
        {
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var method = typeof(TreeInjection).GetMethod("MatsFor", flags);
            var cache = (System.Collections.IDictionary)typeof(TreeInjection).GetField("_mats", flags).GetValue(null);
            var species = (TreeDefLibrary.TreeSpecies)int.MaxValue;
            var def = TreeDefLibrary.Species(TreeDefLibrary.TreeSpecies.Broadleaf, 1f);
            (Material bark, Material foliage) result = default;
            try
            {
                result = ((Material, Material))method.Invoke(null, new object[] { species, def });
                Material survivor = destroyBark ? result.foliage : result.bark;
                Object.DestroyImmediate(destroyBark ? result.bark : result.foliage);
                result = ((Material, Material))method.Invoke(null, new object[] { species, def });
                Assert.IsTrue(result.bark != null && result.foliage != null);
                Assert.AreSame(survivor, destroyBark ? result.foliage : result.bark);
                var again = ((Material, Material))method.Invoke(null, new object[] { species, def });
                Assert.AreEqual(result, again, "Healthy lookups must reuse both materials.");
            }
            finally
            {
                cache.Remove(species);
                Object.DestroyImmediate(result.bark);
                Object.DestroyImmediate(result.foliage);
            }
        }

        [TestCase("CleanFoliage", "_cleanFallback")]
        [TestCase("ConiferMat", "_coniferMats")]
        public void TreeLeafCache_ReplacesDestroyedMaterial(string methodName, string cacheName)
        {
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var method = typeof(TreeInjection).GetMethod(methodName, flags);
            var cache = (System.Collections.IDictionary)typeof(TreeInjection).GetField(cacheName, flags).GetValue(null);
            var baseField = typeof(TreeInjection).GetField("_cleanBase", flags);
            var previousBase = baseField.GetValue(null);
            var leafBase = new Material(Shader.Find("Scatter/FoliageLit"));
            var species = (TreeDefLibrary.TreeSpecies)int.MaxValue;
            var def = TreeDefLibrary.Species(TreeDefLibrary.TreeSpecies.Conifer, 1f);
            Material material = null;
            var ownedTextures = new List<Texture>();
            try
            {
                baseField.SetValue(null, leafBase);
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    material = (Material)method.Invoke(null, new object[] { species, def });
                    Assert.IsTrue(material != null);
                    Assert.AreSame(material, method.Invoke(null, new object[] { species, def }));
                    if (methodName == "ConiferMat") ownedTextures.Add(material.GetTexture("_BaseMap"));
                    Object.DestroyImmediate(material);
                }
            }
            finally
            {
                cache.Remove(species);
                baseField.SetValue(null, previousBase);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(leafBase);
                foreach (Texture texture in ownedTextures) Object.DestroyImmediate(texture);
            }
        }

        [TestCase(0, 100, false)]
        [TestCase(200, 100, true)]
        [TestCase(200, 101, false)]
        public void MeshOnly_UsesTheWholePrototypeBudgetAndPreservesRange(int limit, int verticesPerPart, bool expected)
        {
            var mesh = new Mesh { vertices = new Vector3[verticesPerPart], bounds = new Bounds(Vector3.zero, Vector3.one) };
            var material = new Material(Shader.Find("Scatter/VertexColorLit"));
            try
            {
                var part = new ScatterPartDto(material, new[] { mesh }, new[] { 120f }, false, true);
                var proto = new ScatterPrototypeDto("Coral", 0, 1f, default, 1f, 1f, 90f, 0f, 0f,
                    false, 0f, false, 0f, 0f, false, Vector2.one, true, default, new[] { part, part }) { MeshOnlyVertexLimit = limit };
                var result = proto.ApplyMeshOnlyPolicy();
                Assert.AreEqual(expected, result.MeshOnly);
                Assert.AreEqual(!expected, result.HasImpostor);
                Assert.AreEqual(proto.FarGatherRadius, result.FarGatherRadius);
                Assert.AreSame(mesh, result.TrunkPart.LodMeshes[0]);
                Assert.AreSame(result, result.ApplyMeshOnlyPolicy());
                Assert.AreEqual(120f, proto.MaxCullDistance, "The source DTO must remain unchanged.");
                if (expected) Assert.AreEqual(540f, result.MeshCullDistance);
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }

        [Test]
        public void MeshOnlyBudget_IsCopiedFromTheAsset()
        {
            var asset = ScriptableObject.CreateInstance<ScatterPrototype>();
            try
            {
                asset.MeshOnlyVertexLimit = 200;
                Assert.AreEqual(200, ScatterPrototypeDto.From(asset).MeshOnlyVertexLimit);
                asset.MeshOnlyVertexLimit = -1;
                Assert.AreEqual(0, ScatterPrototypeDto.From(asset).MeshOnlyVertexLimit);
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [TestCase(0.0367f, false)]
        [TestCase(2f, true)]
        public void FlatGeometry_KeepsItsMeshAtGrazingAngles(float height, bool expectedCard)
        {
            var mesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(1.1f, height, 1.1f)) };
            var material = new Material(Shader.Find("Scatter/VertexColorLit"));
            try
            {
                var part = new ScatterPartDto(material, new[] { mesh }, new[] { 100f }, false, false);
                var proto = new ScatterPrototypeDto("Shape", 0, 1f, default, 1f, 1f, 90f, 0f, 0f,
                    false, 0f, false, 0f, 0f, true, Vector2.one, true, default, new[] { part });
                Assert.AreEqual(expectedCard, proto.HasImpostor);
                if (!expectedCard) Assert.AreEqual(100f, proto.MeshCullDistance);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CachedCard_PreservesTheFullBakeCenterForLeaningGeometry()
        {
            var mesh = new Mesh();
            var atlas = new Texture2D(512, 512);
            try
            {
                mesh.bounds = new Bounds(new Vector3(2f, 7f, -3f), new Vector3(4f, 14f, 6f));
                ScatterImpostorBaker.AtlasCard card = ScatterImpostorBaker.FromPrebaked(atlas, null, new[] { mesh });
                Assert.IsTrue(card.Valid);
                Assert.AreEqual(mesh.bounds.center, card.CenterOffset);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(atlas);
            }
        }

        [TestCase(0, 4)]
        [TestCase(8, 8)]
        public void CachedAtlas_UsesLayoutMetadataInsteadOfGuessingFromTextureSize(int storedGrid, int expected)
        {
            var mesh = new Mesh { bounds = new Bounds(Vector3.up, Vector3.one * 2f) };
            var atlas = new Texture2D(512, 512);
            try
            {
                var card = ScatterImpostorBaker.FromPrebaked(atlas, null, new[] { mesh }, storedGrid);
                Assert.AreEqual(expected, card.GridN);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(atlas);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Billboard_PreservesSourceShadowCasting(bool castShadows)
        {
            var mesh = new Mesh { bounds = new Bounds(Vector3.up, Vector3.one * 2f) };
            var material = new Material(Shader.Find("Scatter/VertexColorLit"));
            var atlas = new Texture2D(512, 512);
            var part = new ScatterPartDto(material, new[] { mesh }, new[] { 100f }, castShadows, true);
            var proto = new ScatterPrototypeDto("Shadow test", 0, 1f, default, 1f, 1f, 90f, 0f, 0f,
                false, 0f, false, 0f, 0f, true, Vector2.one, true, default, new[] { part }, atlas);
            var card = ScatterImpostorFactory.TryBuild(proto, mesh.bounds);
            try
            {
                Assert.IsTrue(card.Valid);
                Assert.AreEqual(castShadows ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off, card.Params.shadowCastingMode);
            }
            finally
            {
                Object.DestroyImmediate(card.Quad);
                Object.DestroyImmediate(card.Params.material);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void SurfaceAtlases_PreserveTheirDataChannels()
        {
            var manifest = Resources.Load<GeneratedImpostorManifest>(GeneratedImpostorManifest.ResourcePath);
            Assert.IsNotNull(manifest);
            int checkedAtlases = 0;
            foreach (var entry in manifest.Entries)
            {
                if (!entry.HasSurfaceData) continue;
                Assert.Greater(entry.GridN, 0, entry.Key);
                Assert.IsNotNull(entry.Normal, entry.Key);
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(entry.Normal));
                Assert.IsFalse(importer.mipMapsPreserveCoverage, entry.Key + ": alpha is a leaf mask, not coverage");
                Assert.IsFalse(importer.alphaIsTransparency, entry.Key + ": alpha is a leaf mask, not transparency");
                checkedAtlases++;
            }
            Assert.Greater(checkedAtlases, 0);
        }

        [Test]
        public void SavedNormalAtlases_DecodeTheBakeTargetsSrgbEncoding()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D",
                new[] { "Assets/Resources/Settings/Scatter" });
            int normals = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("_n.png")) continue;
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsTrue(importer.sRGBTexture, path);
                normals++;
            }
            Assert.Greater(normals, 0, "The check must cover saved normal atlases.");
        }

        [Test]
        public void LeafCardCanopies_KeepTheirGeometryAcrossBarkLodChanges()
        {
            foreach (TreeDefLibrary.TreeSpecies species in TreeDefLibrary.AllSpecies)
            {
                TreeDef def = TreeDefLibrary.Species(species);
                if (def.FoliageStyle == FoliageStyle.ConiferCone) continue;
                GeneratedTree tree = TreeGenerator.Generate(def, 12345);
                try
                {
                    Mesh near = tree.Foliage;
                    Assert.IsNotNull(near, species.ToString());
                    foreach (Mesh lod in tree.FoliageLods)
                    {
                        CollectionAssert.AreEqual(near.vertices, lod.vertices, species.ToString());
                        CollectionAssert.AreEqual(near.triangles, lod.triangles, species.ToString());
                    }
                }
                finally
                {
                    var meshes = new HashSet<Mesh>(tree.BarkLods);
                    meshes.UnionWith(tree.FoliageLods);
                    meshes.UnionWith(tree.AccentLods);
                    meshes.Add(tree.Stump);
                    meshes.Add(tree.Log);
                    foreach (Mesh mesh in meshes)
                        if (mesh != null) Object.DestroyImmediate(mesh);
                }
            }
        }
    }
}
