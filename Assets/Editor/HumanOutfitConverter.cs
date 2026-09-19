using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authors reviewed combined Townsfolk bodies through the existing fitting pipeline.</summary>
public sealed class HumanOutfitConverter : EditorWindow
{
    public const string Root = HumanTrialAuthor.Folder + "/Converted";
    public const string Model = HumanTrialAuthor.Folder + "/Review/Townsfolk_Characters.fbx";
    string[] _parts = Array.Empty<string>();
    int _part;
    string _revision = "v1";
    bool _separateHeadgear;
    bool _looseCloth;
    bool _reviewedHead;
    string _message;

    [Serializable]
    public sealed class Receipt
    {
        public int version = 1;
        public string sourcePart;
        public string revision;
        public string inputHash;
        public string outputHash;
        public bool requiresSeparateHeadgear;
        public bool requiresClothReview;
        public string candidatePrefab;
        public string originalPrefab;
    }

    [MenuItem("Tools/Actors/Human/Outfit Converter")]
    public static void Open() => GetWindow<HumanOutfitConverter>("Outfit Converter");

    void OnEnable()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        _parts = model == null ? Array.Empty<string>() : model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Select(r => r.name).OrderBy(n => n).ToArray();
        _part = Mathf.Clamp(_part, 0, Mathf.Max(0, _parts.Length - 1));
        minSize = new Vector2(480, 340);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Source outfit → fitted body", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Uses the reviewed Townsfolk rig and palette. Other packs require their own verified mapping.", MessageType.Info);
        if (_parts.Length == 0) { EditorGUILayout.HelpBox("The imported Townsfolk model is missing.", MessageType.Error); return; }
        int part = EditorGUILayout.Popup("Outfit", _part, _parts);
        if (part != _part) { _part = part; _reviewedHead = false; _message = null; }
        _revision = EditorGUILayout.TextField("Output revision", _revision);
        _separateHeadgear = EditorGUILayout.Toggle("Integrated hood or headgear", _separateHeadgear);
        _looseCloth = EditorGUILayout.Toggle("Loose skirt, robe, or apron", _looseCloth);
        EditorGUILayout.HelpBox("The Mage headgear option preserves its reviewed hood cloth. Other integrated headgear requires separate fitting. Loose cloth keeps its source skin weights; review bent poses.", MessageType.Warning);
        _reviewedHead = EditorGUILayout.ToggleLeft("I reviewed the source head and clothing structure.", _reviewedHead);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !_reviewedHead))
            if (GUILayout.Button("Bake or reuse cached outfit"))
            {
                try
                {
                    var receipt = Convert(_parts[_part], _revision, _separateHeadgear, _looseCloth);
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(receipt.candidatePrefab);
                    EditorGUIUtility.PingObject(Selection.activeObject);
                    _message = "Ready for fitting review: " + receipt.candidatePrefab;
                }
                catch (Exception ex) { _message = ex.Message; }
            }
        if (EditorApplication.isPlaying) EditorGUILayout.HelpBox("Stop Play mode to bake an outfit.", MessageType.Info);
        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
    }

    public static string OutputFolder(string part, string revision)
    {
        if (string.IsNullOrEmpty(part) || !Regex.IsMatch(part, @"\ASM_Chr_[A-Za-z0-9_]+\z"))
            throw new ArgumentException("Select a valid Townsfolk character name.", nameof(part));
        if (string.IsNullOrEmpty(revision) || !Regex.IsMatch(revision, @"\A[A-Za-z0-9_-]{1,32}\z"))
            throw new ArgumentException("Revision must contain 1–32 letters, digits, underscores, or hyphens.", nameof(revision));
        return Root + "/" + RoleName(part) + "_" + revision;
    }

    // The source sub-mesh name is the lookup key and cannot be renamed. Our own assets
    // drop its prefix, so every name we write goes through here and every name we read
    // the source by does not.
    public static string RoleName(string part) =>
        part != null && part.StartsWith("SM_Chr_", StringComparison.Ordinal) ? part.Substring(7) : part;

    public static Receipt Convert(string part, string revision, bool separateHeadgear, bool looseCloth)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before converting outfits.");
        string folder = OutputFolder(part, revision);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (model == null || model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(r => r.name == part) != 1)
            throw new ArgumentException("The selected Townsfolk character is missing or ambiguous: " + part);
        string input = InputHash(part, separateHeadgear, looseCloth);
        if (Directory.Exists(folder) || File.Exists(folder))
            return ReadCached(folder, part, revision, input, separateHeadgear, looseCloth);

        Directory.CreateDirectory(Root);
        AssetDatabase.Refresh();
        string staging = Root + "/__building_" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        AssetDatabase.Refresh();
        var workingScene = EditorSceneManager.NewPreviewScene();
        try
        {
            HumanTownsfolkReviewAuthor.Build(part, staging, workingScene, separateHeadgear && part == "SM_Chr_Mage_01");
            ValidatePrefab(staging + "/" + RoleName(part) + "_Fit.prefab");
            string error = AssetDatabase.MoveAsset(staging, folder);
            if (!string.IsNullOrEmpty(error)) throw new IOException("Cannot publish conversion: " + error);
            var result = new Receipt
            {
                sourcePart = part, revision = revision, inputHash = input,
                requiresSeparateHeadgear = separateHeadgear, requiresClothReview = looseCloth,
                candidatePrefab = folder + "/" + RoleName(part) + "_Fit.prefab",
                originalPrefab = folder + "/" + RoleName(part) + "_Original.prefab"
            };
            AssetDatabase.SaveAssets();
            result.outputHash = OutputHash(result);
            File.WriteAllText(folder + "/Conversion.json", JsonUtility.ToJson(result, true));
            AssetDatabase.ImportAsset(folder + "/Conversion.json");
            return result;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(workingScene);
            // Only the unique staging directory created by this invocation may be removed.
            if (AssetDatabase.IsValidFolder(staging) && !AssetDatabase.DeleteAsset(staging))
                LoggerProvider.Log(LogLevel.Warning, "Human", "Could not remove conversion staging folder: " + staging);
        }
    }

    static Receipt ReadCached(string folder, string part, string revision, string input, bool headgear, bool cloth)
    {
        string path = folder + "/Conversion.json";
        if (!File.Exists(path)) throw new IOException("Existing output has no conversion receipt. Preserve it and choose a new revision.");
        Receipt result;
        try { result = JsonUtility.FromJson<Receipt>(File.ReadAllText(path)); }
        catch (Exception ex) { throw new IOException("Cannot read the conversion receipt. Preserve it and choose a new revision.", ex); }
        if (result == null || result.version != 1 || result.sourcePart != part || result.revision != revision
            || result.inputHash != input || result.requiresSeparateHeadgear != headgear || result.requiresClothReview != cloth
            || result.candidatePrefab != folder + "/" + RoleName(part) + "_Fit.prefab"
            || result.originalPrefab != folder + "/" + RoleName(part) + "_Original.prefab")
            throw new IOException("Conversion inputs changed. Preserve the existing output and choose a new revision.");
        ValidatePrefab(result.candidatePrefab);
        if (result.outputHash != OutputHash(result))
            throw new IOException("Conversion output was edited. Preserve it and choose a new revision.");
        return result;
    }

    static void ValidatePrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var animator = prefab != null ? prefab.GetComponent<Animator>() : null;
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
            throw new InvalidOperationException("Converted prefab requires a valid Humanoid avatar: " + path);
        var skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skins.Any(s => s.sharedMesh == null || s.bones.Any(b => b == null) || s.sharedMaterials.Any(m => m == null)))
            throw new InvalidOperationException("Converted prefab contains missing mesh, bone, or material references: " + path);
        var body = skins.Single(s => s.name.StartsWith("SOURCE / "));
        if (body.sharedMesh.blendShapeCount != 5 || body.sharedMesh.GetBlendShapeName(0) != "SkeletonFit")
            throw new InvalidOperationException("Converted body is missing cached fit or body-shape frames: " + path);
    }

    static string OutputHash(Receipt receipt)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(receipt.originalPrefab) == null)
            throw new IOException("Original comparison prefab is missing.");
        // Unity can retain the old dependency hash during the frame that saves a mesh edit.
        string folder = Path.GetDirectoryName(receipt.candidatePrefab);
        var files = Directory.GetFiles(folder).Where(path => path.EndsWith(".prefab", StringComparison.Ordinal)
            || path.EndsWith(".asset", StringComparison.Ordinal) || path.EndsWith(".prefab.meta", StringComparison.Ordinal)
            || path.EndsWith(".asset.meta", StringComparison.Ordinal)).OrderBy(path => path, StringComparer.Ordinal);
        using var hash = SHA256.Create();
        return Hash128.Compute(string.Join("|", files.Select(path => Path.GetFileName(path) + ":"
            + System.Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)))))).ToString();
    }

    static string InputHash(string part, bool headgear, bool cloth)
    {
        string[] dependencies =
        {
            Model, HumanFullBodyReviewAuthor.PrefabPath, HumanoidAuthor.PrefabPath,
            HumanBodyReviewAuthor.Folder + "/BareBody.prefab",
            HumanTrialAuthor.Folder + "/Review/Townsfolk.mat", HumanTrialAuthor.Folder + "/Human.mat",
            "Assets/Editor/HumanOutfitConverter.cs", "Assets/Editor/HumanTownsfolkReviewAuthor.cs",
            "Assets/Editor/HumanBodyReviewAuthor.cs", "Assets/Editor/HumanBodyShapeAuthor.cs",
            "Assets/Editor/HumanSkinAuthor.cs"
        };
        foreach (string path in dependencies)
            if (AssetDatabase.LoadMainAssetAtPath(path) == null) throw new IOException("Conversion dependency is missing: " + path);
        return Hash128.Compute(part + "|" + headgear + "|" + cloth + "|"
            + string.Join("|", dependencies.Select(path => AssetDatabase.GetAssetDependencyHash(path).ToString()))).ToString();
    }
}
