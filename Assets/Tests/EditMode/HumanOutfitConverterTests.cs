using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanOutfitConverterTests
    {
        static object Call(string method, params object[] arguments)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HumanOutfitConverter")).First(t => t != null);
            try { return type.GetMethod(method).Invoke(null, arguments); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        [TestCase("../outside", "v1")]
        [TestCase("SM_Chr_Rider_01", "../outside")]
        [TestCase("SM_Chr_Rider_01", "")]
        [TestCase("SM_Chr_Rider_01", "v1\n")]
        public void UnsafeOutputNamesAreRejected(string part, string revision)
            => Assert.Throws<ArgumentException>(() => Call("OutputFolder", part, revision));

        [Test]
        public void CacheReusesIdentityAndRefusesChangedInputsOrEditedOutput()
        {
            const string part = "SM_Chr_Rider_01";
            string revision = "test_" + Guid.NewGuid().ToString("N").Substring(0, 20);
            string folder = (string)Call("OutputFolder", part, revision);
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            int roots = scene.rootCount;
            bool dirty = scene.isDirty;
            try
            {
                object first = Call("Convert", part, revision, false, false);
                Assert.AreEqual(scene, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                Assert.AreEqual(roots, scene.rootCount);
                Assert.AreEqual(dirty, scene.isDirty);
                string path = (string)first.GetType().GetField("candidatePrefab").GetValue(first);
                string guid = AssetDatabase.AssetPathToGUID(path);
                string receipt = File.ReadAllText(folder + "/Conversion.json");
                Call("Convert", part, revision, false, false);
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
                Assert.AreEqual(receipt, File.ReadAllText(folder + "/Conversion.json"));
                Assert.Throws<IOException>(() => Call("Convert", part, revision, true, false));
                Assert.AreEqual(receipt, File.ReadAllText(folder + "/Conversion.json"));

                var mesh = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Single(s => s.name.StartsWith("SOURCE / ")).sharedMesh;
                var vertices = mesh.vertices; vertices[0] += Vector3.right * .01f; mesh.vertices = vertices;
                EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(mesh), ImportAssetOptions.ForceUpdate);
                Assert.Throws<IOException>(() => Call("Convert", part, revision, false, false));
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path), "An edited result must not be overwritten.");
            }
            finally
            {
                // This generated test directory did not exist before this test.
                if (AssetDatabase.IsValidFolder(folder)) Assert.IsTrue(AssetDatabase.DeleteAsset(folder));
            }
        }
    }
}
