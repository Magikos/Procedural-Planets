using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VeryAnimation
{
    internal static class EditorCommon
    {
        public const float TimeEpsilon = 0.0001f;

        internal class ArrowMesh : IDisposable
        {
            public Mesh Mesh { get; private set; }
            public Material Material { get; private set; }

            public ArrowMesh()
            {
                #region Mesh
                Mesh = new Mesh();
                Mesh.hideFlags |= HideFlags.DontSave;
                Vector3[] lines = new Vector3[]
                {
                    new(0, 0, 0),

                    new(0, 0.1f, 0.1f),
                    new(0.09f, -0.05f, 0.1f),
                    new(-0.09f, -0.05f, 0.1f),

                    new(0, 0, 1),
                };
                int[] indices = new int[]
                {
                    0, 1,
                    0, 2,
                    0, 3,

                    1, 2,
                    2, 3,
                    3, 1,

                    4, 1,
                    4, 2,
                    4, 3,
                };
                Mesh.vertices = lines;
                Mesh.SetIndices(indices, MeshTopology.Lines, 0);
                Mesh.RecalculateBounds();
                #endregion

                Material = new Material(Shader.Find("Hidden/Very Animation/VertexColor-Transparent"));
                Material.hideFlags |= HideFlags.DontSave;
            }

            public void Dispose()
            {
                if (Mesh != null)
                {
                    Mesh.DestroyImmediate(Mesh);
                    Mesh = null;
                }
                if (Material != null)
                {
                    Material.DestroyImmediate(Material);
                    Material = null;
                }
            }
        }

        public static void SaveInsideAssetsFolderDisplayDialog()
        {
            EditorUtility.DisplayDialog(Language.GetText(Language.Help.DisplayDialogSaveInsideAssetsFolder),
                                        Language.GetTooltip(Language.Help.DisplayDialogSaveInsideAssetsFolder), "ok");
        }

        public static string GetSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "GameObject";

            const string WindowsInvalidFileNameChars = "<>:\"/\\|?*";
            var invalidFileNameChars = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalidFileNameChars, chars[i]) >= 0 ||
                    WindowsInvalidFileNameChars.IndexOf(chars[i]) >= 0)
                    chars[i] = '_';
            }

            var safeName = new string(chars).Trim().TrimEnd('.');
            return !string.IsNullOrEmpty(safeName) ? safeName : "GameObject";
        }

        public static string SaveFilePanelInAssets(string title, string directory, string defaultName, string extension)
        {
            defaultName = GetSafeFileName(defaultName);
            string path = EditorUtility.SaveFilePanel(title, directory, defaultName, extension);
            if (string.IsNullOrEmpty(path))
                return null;
            var projectRelativePath = FileUtil.GetProjectRelativePath(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(projectRelativePath) ||
                (!projectRelativePath.Equals("Assets", StringComparison.Ordinal) && !projectRelativePath.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                SaveInsideAssetsFolderDisplayDialog();
                return null;
            }
            return projectRelativePath;
        }

        public static Texture2D LoadTexture2DAssetAtPath(string path)
        {
            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (result == null)
            {
                var fileName = Path.GetFileName(path);
                var guids = AssetDatabase.FindAssets("t:Texture2D");
                for (int i = 0; i < guids.Length; i++)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (Path.GetFileName(assetPath) == fileName)
                    {
                        if (assetPath.IndexOf("VeryAnimation", StringComparison.Ordinal) >= 0)
                        {
                            result = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                            break;
                        }
                    }
                }
            }
            return result;
        }

        public static Dictionary<string, string> CollectAssetPaths(string filter)
        {
            var result = new Dictionary<string, string>();
            var guids = AssetDatabase.FindAssets(filter);
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var name = path.StartsWith("Assets/", StringComparison.Ordinal) ? path["Assets/".Length..] : path;
                result[name] = path;
            }
            return result;
        }

        public static T[] CopyArrayOrNull<T>(IReadOnlyCollection<T> source)
        {
            if (source == null)
                return null;
            if (source.Count == 0)
                return Array.Empty<T>();

            var array = new T[source.Count];
            int index = 0;
            foreach (var item in source)
                array[index++] = item;
            return array;
        }
        public static List<T> CopyListOrNull<T>(IReadOnlyCollection<T> source)
        {
            if (source == null)
                return null;

            var list = new List<T>(source.Count);
            foreach (var item in source)
                list.Add(item);
            return list;
        }
        public static bool IsAncestorObject(GameObject obj, GameObject ancestorObject)
        {
            if (obj == null || ancestorObject == null)
                return false;
            var t = obj.transform;
            var ancestorT = ancestorObject.transform;
            while (t != null)
            {
                if (t == ancestorT)
                    return true;
                t = t.parent;
            }
            return false;
        }

        public static bool Ray_Triangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out Vector3 resultP)
        {
            var e1 = v1 - v0;
            var e2 = v2 - v0;

            resultP = Vector3.zero;

            var pvec = Vector3.Cross(ray.direction, e2);
            var det = Vector3.Dot(e1, pvec);

            Vector3 qvec;
            float u, v;
            if (det > Mathf.Epsilon)
            {
                var tvec = ray.origin - v0;
                u = Vector3.Dot(tvec, pvec);
                if (u < 0.0f || u > det) return false;

                qvec = Vector3.Cross(tvec, e1);

                v = Vector3.Dot(ray.direction, qvec);
                if (v < 0.0 || u + v > det) return false;
            }
            else
            {
                return false;
            }

            var inv_det = 1.0f / det;

            var t = Vector3.Dot(e2, qvec);
            t *= inv_det;

            if (t < 0f) return false;

            resultP = ray.origin + ray.direction * t;

            return true;
        }

        public static bool IsInsideTriangle(Vector3 pos, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)
        {
            float result = 0f;
            {
                var vec0 = v0 - pos;
                var vec1 = v1 - pos;
                var vec2 = v2 - pos;
                {
                    var angle = Vector3.Angle(vec0, vec1);
                    if (Vector3.Dot(Vector3.Cross(vec0, vec1), normal) < 0f)
                        angle *= -1f;
                    result += angle;
                }
                {
                    var angle = Vector3.Angle(vec1, vec2);
                    if (Vector3.Dot(Vector3.Cross(vec1, vec2), normal) < 0f)
                        angle *= -1f;
                    result += angle;
                }
                {
                    var angle = Vector3.Angle(vec2, vec0);
                    if (Vector3.Dot(Vector3.Cross(vec2, vec0), normal) < 0f)
                        angle *= -1f;
                    result += angle;
                }
                result = Mathf.Abs(result);
            }
            return Mathf.Abs(result - 360f) < 0.001f;
        }

        public static void GetTRS(Matrix4x4 mat, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = mat.GetColumn(3);
            rotation = Quaternion.LookRotation(mat.GetColumn(2), mat.GetColumn(1));
            scale = new Vector3(mat.GetColumn(0).magnitude, mat.GetColumn(1).magnitude, mat.GetColumn(2).magnitude);
        }

        public static float InverseLerpUnclamped(float a, float b, float value)
        {
            if (a != b)
            {
                return (value - a) / (b - a);
            }
            return 0f;
        }

        public static void ShowNotification(string message)
        {
            var scene = SceneView.lastActiveSceneView;
            if (scene == null) return;
            scene.ShowNotification(new GUIContent(message));
        }

        public static void PingObject(UnityEngine.Object obj)
        {
            try
            {
                EditorGUIUtility.PingObject(obj);
            }
            catch
            {
                // Workaround for a temporary bug that causes NullPo in the new Hierarchy.
            }
        }

        public static T FindResourceFirst<T>(Predicate<T> match) where T : UnityEngine.Object
        {
            if (match == null) return null;
            foreach (var obj in Resources.FindObjectsOfTypeAll<T>())
            {
                if (match(obj))
                    return obj;
            }
            return null;
        }

        public static string GetAssetPath(UnityEngine.Object obj)
        {
            if (AssetDatabase.Contains(obj))
            {
                var assetPath = AssetDatabase.GetAssetPath(obj);
                return Path.GetDirectoryName(assetPath);
            }
            else
            {
                return "Assets";
            }
        }

        public static int GetLastFrame(float length, float frameRate)
        {
            return Mathf.RoundToInt(length * frameRate);
        }
        public static int GetTimeFrameRound(float time, float frameRate)
        {
            return Mathf.RoundToInt(time * frameRate);
        }
        public static int GetTimeFrameFloor(float time, float frameRate)
        {
            return Mathf.FloorToInt(time * frameRate);
        }
        public static float GetFrameTime(int frame, float frameRate)
        {
            var time = frame * (1f / frameRate);
            return SnapToFrame(time, frameRate);
        }
        public static float SnapToFrame(float time, float frameRate)
        {
            return Mathf.Round(time * frameRate) / frameRate;
        }
        public static float GetHalfFrameTime(float frameRate)
        {
            return (0.5f / frameRate) - TimeEpsilon;
        }

        public static int CeilToPowerOfTwo(int value)
        {
            if (value <= 4) return 4;
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            return value;
        }

        public static int GetBlankLayer()
        {
            for (int layer = 31; layer > 0; layer--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(layer)))
                    return layer;
            }
            return 31;
        }

        public const float FullRotation = 360f;
        public const float HalfRotation = 180f;
        public static float WrapAngle(float angle) => Mathf.Repeat(angle + FullRotation, FullRotation);
        public static float WrapAngleSigned(float angle) => Mathf.Repeat(angle + HalfRotation, FullRotation) - HalfRotation;

        public static readonly string[] AxisLabels = { "X", "Y", "Z" };

        public static bool RemoveNullKeys<TKey, TValue>(Dictionary<TKey, TValue> dictionary) where TKey : class
        {
            static bool IsKeyNull(TKey k) => k is UnityEngine.Object uo ? uo == null : k is null;
            bool hasNull = false;
            foreach (var k in dictionary.Keys)
            {
                if (IsKeyNull(k)) { hasNull = true; break; }
            }
            if (!hasNull)
                return false;
            var keys = new TKey[dictionary.Count];
            dictionary.Keys.CopyTo(keys, 0);
            foreach (var k in keys)
            {
                if (IsKeyNull(k))
                    dictionary.Remove(k);
            }
            return true;
        }

        public static bool IsBuiltInRenderPipeline()
        {
            return GraphicsSettings.currentRenderPipeline == null;
        }
        public static bool IsUniversalRenderPipeline()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                return false;
            var shader = GraphicsSettings.currentRenderPipeline.defaultShader;
            if (shader == Shader.Find("Universal Render Pipeline/Lit"))
                return true;
            return false;
        }
        public static bool IsHighDefinitionRenderPipeline()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                return false;
            var shader = GraphicsSettings.currentRenderPipeline.defaultShader;
            if (shader == Shader.Find("HDRenderPipeline/Lit") ||
                shader == Shader.Find("HDRP/Lit"))
                return true;
            return false;
        }
        public static Shader GetStandardShader()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var shader = GraphicsSettings.currentRenderPipeline.defaultShader;
                if (shader != null)
                    return shader;
            }
            return Shader.Find("Standard");
        }
    }
}
