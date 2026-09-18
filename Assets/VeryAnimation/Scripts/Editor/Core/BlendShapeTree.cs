using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    [Serializable]
    internal sealed class BlendShapeTree
    {
        private VeryAnimationWindow VAW => VeryAnimationWindow.instance;
        private VeryAnimationEditorWindow VAE => VeryAnimationEditorWindow.instance;

        private const string PrefKey_BlendShapeMode = "VeryAnimation_BlendShapeMode";
        private const string PrefKey_MirrorName = "VeryAnimation_Control_BlendShapeMirrorName";
        private const string PrefKey_IconShowName = "VeryAnimation_Control_BlendShapeSetIconShowName";
        private const string PrefKey_IconSize = "VeryAnimation_Control_BlendShapeSetIconSize";
        private const string PrefKey_IconCameraMode = "VeryAnimation_BlendShapeSetIconCameraMode";
        private const string PrefKey_IconCameraBounds = "VeryAnimation_BlendShapeSetIconCameraBounds";

        private const string UndoChangeBlendShapeGroup = "Change BlendShape Group";
        private const string UndoTemplateBlendShape = "Template BlendShape";

        private enum BlendShapeMode
        {
            Slider,
            List,
            Icon,
            Total,
        }
        private BlendShapeMode blendShapeMode;

        #region Tree
        [System.Diagnostics.DebuggerDisplay("{blendShapeName}")]
        private class BlendShapeInfo
        {
            public string blendShapeName;
        }
        private class BlendShapeNode
        {
            public string name;
            public bool foldout;
            public BlendShapeInfo[] infoList;
        }
        private class BlendShapeRootNode : BlendShapeNode
        {
            public SkinnedMeshRenderer renderer;
            public Mesh mesh;
            public string[] blendShapeNames;
        }
        private readonly List<BlendShapeRootNode> blendShapeNodes;
        private readonly Dictionary<BlendShapeNode, int> blendShapeGroupTreeTable;

        [SerializeField]
        private float[] blendShapeGroupValues;

        private bool blendShapeMirrorName;
        #endregion

        #region List
        private ReorderableList blendShapeSetListReorderableList;
        #endregion

        #region Icon
        private const int IconTextureSize = 256;
        private bool iconUpdate;
        private bool iconShowName;
        private float iconSize;

        private enum IconCameraMode
        {
            forward,
            back,
            up,
            down,
            right,
            left,
        }
        private IconCameraMode iconCameraMode;

        private enum IconCameraBounds
        {
            allRenderers,
            focusChangedRenderers,
            onlyRenderersWithChanges,
        }
        private IconCameraBounds iconCameraBounds;
        #endregion

        #region GUIStyles
        class GUIStyles
        {
            public const int FoldoutWidth = 22;
            public const int FoldoutSpace = 17;
            public const int FloatFieldWidth = 44;
            public const int IndentWidth = 15;

            public readonly GUILayoutOption guiLayoutFoldoutWidth = GUILayout.Width(FoldoutWidth);

            public GUIStyle guiStyleIconButton;
            public GUIStyle guiStyleNameLabelCenter;
            public GUIStyle guiStyleNameLabelRight;
            public GUIContent guiContentButton;

            public readonly string[] blendShapeModeString =
            {
                nameof(BlendShapeMode.Slider),
                nameof(BlendShapeMode.List),
                nameof(BlendShapeMode.Icon),
            };

            public GUIStyles()
            {
                Assert.IsNotNull(Event.current, "GUIStyles must be created during OnGUI (GUI.skin requires event context)");
                guiStyleIconButton = new GUIStyle(GUI.skin.button)
                {
                    margin = new RectOffset(0, 0, 0, 0),
                    overflow = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0)
                };
                guiStyleNameLabelCenter = new GUIStyle(EditorStyles.whiteLargeLabel)
                {
                    alignment = TextAnchor.LowerCenter
                };
                guiStyleNameLabelRight = new GUIStyle(EditorStyles.whiteLargeLabel)
                {
                    alignment = TextAnchor.LowerRight
                };
                guiContentButton = new();
            }
        }
        private static GUIStyles s_GUIStyles;
        private static GUIStyles Styles
        {
            get
            {
                s_GUIStyles ??= new GUIStyles();
                return s_GUIStyles;
            }
        }
        #endregion

        public BlendShapeTree()
        {
            if (VAW == null || VAW.GameObject == null)
                return;

            #region BlendShapeNode
            {
                blendShapeNodes = new List<BlendShapeRootNode>();
                foreach (var renderer in VAW.VA.Renderers)
                {
                    var smr = renderer as SkinnedMeshRenderer;
                    if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount <= 0)
                        continue;
                    var mesh = smr.sharedMesh;
                    var root = new BlendShapeRootNode
                    {
                        renderer = smr,
                        mesh = mesh,
                        name = renderer.gameObject.name,
                        infoList = new BlendShapeInfo[mesh.blendShapeCount],
                        blendShapeNames = new string[mesh.blendShapeCount + 1]
                    };
                    root.blendShapeNames[0] = "[none]";
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        root.infoList[i] = new BlendShapeInfo()
                        {
                            blendShapeName = mesh.GetBlendShapeName(i),
                        };
                        root.blendShapeNames[i + 1] = mesh.GetBlendShapeName(i);
                    }
                    blendShapeNodes.Add(root);
                }

                {
                    blendShapeGroupTreeTable = new Dictionary<BlendShapeNode, int>();
                    int counter = 0;
                    void AddTable(BlendShapeNode mg)
                    {
                        blendShapeGroupTreeTable.Add(mg, counter++);
                    }

                    foreach (var node in blendShapeNodes)
                    {
                        AddTable(node);
                    }

                    blendShapeGroupValues = new float[blendShapeGroupTreeTable.Count];
                }
            }
            #endregion

            iconUpdate = true;
        }

        public void LoadEditorPref()
        {
            blendShapeMode = (BlendShapeMode)EditorPrefs.GetInt(PrefKey_BlendShapeMode, 0);
            blendShapeMirrorName = EditorPrefs.GetBool(PrefKey_MirrorName, false);
            iconShowName = EditorPrefs.GetBool(PrefKey_IconShowName, true);
            iconSize = EditorPrefs.GetFloat(PrefKey_IconSize, 100f);
            iconCameraMode = (IconCameraMode)EditorPrefs.GetInt(PrefKey_IconCameraMode, 0);
            iconCameraBounds = (IconCameraBounds)EditorPrefs.GetInt(PrefKey_IconCameraBounds, (int)IconCameraBounds.focusChangedRenderers);
        }
        public void SaveEditorPref()
        {
            EditorPrefs.SetInt(PrefKey_BlendShapeMode, (int)blendShapeMode);
            EditorPrefs.SetBool(PrefKey_MirrorName, blendShapeMirrorName);
            EditorPrefs.SetBool(PrefKey_IconShowName, iconShowName);
            EditorPrefs.SetFloat(PrefKey_IconSize, iconSize);
            EditorPrefs.SetInt(PrefKey_IconCameraMode, (int)iconCameraMode);
            EditorPrefs.SetInt(PrefKey_IconCameraBounds, (int)iconCameraBounds);
        }

        public void BlendShapeTreeToolbarGUI()
        {
            EditorGUI.BeginChangeCheck();
            var m = (BlendShapeMode)GUILayout.Toolbar((int)blendShapeMode, Styles.blendShapeModeString, EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck())
            {
                blendShapeMode = m;
            }
        }

        public void BlendShapeTreeSettingsMesh()
        {
            var menu = new GenericMenu();
            menu.AddItem(Language.GetContent(Language.Help.BlendShapeMirrorName), blendShapeMirrorName, () =>
            {
                blendShapeMirrorName = !blendShapeMirrorName;
            });
            menu.AddItem(Language.GetContent(Language.Help.BlendShapeMirrorAutomap), false, () =>
            {
                VAW.VA.BlendShapeMirrorAutomap();
            });
            menu.AddItem(Language.GetContent(Language.Help.BlendShapeMirrorClear), false, () =>
            {
                VAW.VA.BlendShapeMirrorInitialize();
            });
            menu.ShowAsContext();
        }

        public bool IsHaveBlendShapeNodes()
        {
            return blendShapeNodes != null && blendShapeNodes.Count > 0;
        }

        public void BlendShapeTreeGUI()
        {
            var e = Event.current;

            EditorGUILayout.BeginVertical(VAW.GuiStyleSkinBox);
            if (blendShapeMode == BlendShapeMode.Slider)
            {
                #region Slider
                #region SetBlendShapeFoldout
                static void SetBlendShapeFoldout(BlendShapeNode mg, bool foldout)
                {
                    mg.foldout = foldout;
                }
                #endregion

                var mgRoot = blendShapeNodes;

                #region Top
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Select All", GUILayout.Width(100)))
                    {
                        void AppendAllBlendShapes(ICollection<GameObject> goCol, ICollection<EditorCurveBinding> bindingCol)
                        {
                            foreach (var root in mgRoot)
                            {
                                if (root.renderer != null && root.renderer.gameObject != null)
                                    goCol.Add(root.renderer.gameObject);
                                if (root.infoList != null && root.infoList.Length > 0)
                                {
                                    foreach (var info in root.infoList)
                                        bindingCol.Add(VAW.VA.AnimationCurveBindingBlendShape(root.renderer, info.blendShapeName));
                                }
                            }
                        }
                        if (Shortcuts.IsKeyControl(e) || e.shift)
                        {
                            var combineGoList = new HashSet<GameObject>(VAW.VA.SelectionGameObjects);
                            var combineVirtualList = new HashSet<HumanBodyBones>();
                            if (VAW.VA.SelectionHumanVirtualBones != null)
                                combineVirtualList.UnionWith(VAW.VA.SelectionHumanVirtualBones);
                            var combineBindings = new HashSet<EditorCurveBinding>(VAW.VA.UAw.GetCurveSelection());
                            AppendAllBlendShapes(combineGoList, combineBindings);
                            VAW.VA.SelectGameObjects(combineGoList, combineVirtualList);
                            VAW.VA.SetAnimationWindowSynchroSelection(combineBindings);
                        }
                        else
                        {
                            var combineGoList = new List<GameObject>();
                            var combineBindings = new List<EditorCurveBinding>();
                            AppendAllBlendShapes(combineGoList, combineBindings);
                            VAW.VA.SelectGameObjects(combineGoList);
                            VAW.VA.SetAnimationWindowSynchroSelection(combineBindings);
                        }
                    }
                    EditorGUILayout.Space();
                    if (GUILayout.Button("Reset All", VAW.GuiStyleDropDown, GUILayout.Width(100)))
                    {
                        var menu = new GenericMenu();
                        {
                            if (VAW.VA.BlendShapeWeightSave.IsEnablePrefabWeight())
                            {
                                menu.AddItem(Language.GetContent(Language.Help.EditorPosePrefab), false, () =>
                                {
                                    Undo.RecordObject(VAE, "Prefab Pose");
                                    for (int i = 0; i < blendShapeGroupValues.Length; i++)
                                        blendShapeGroupValues[i] = 0f;
                                    foreach (var root in mgRoot)
                                    {
                                        if (root.infoList != null && root.infoList.Length > 0)
                                        {
                                            foreach (var info in root.infoList)
                                                VAW.VA.SetAnimationValueBlendShapeIfNotOriginal(root.renderer, info.blendShapeName, VAW.VA.BlendShapeWeightSave.GetPrefabWeight(root.renderer, info.blendShapeName));
                                        }
                                    }
                                });
                            }
                            {
                                menu.AddItem(Language.GetContent(Language.Help.EditorPoseStart), false, () =>
                                {
                                    Undo.RecordObject(VAE, "Edit Start Pose");
                                    for (int i = 0; i < blendShapeGroupValues.Length; i++)
                                        blendShapeGroupValues[i] = 0f;
                                    foreach (var root in mgRoot)
                                    {
                                        if (root.infoList != null && root.infoList.Length > 0)
                                        {
                                            foreach (var info in root.infoList)
                                                VAW.VA.SetAnimationValueBlendShapeIfNotOriginal(root.renderer, info.blendShapeName, VAW.VA.BlendShapeWeightSave.GetOriginalWeight(root.renderer, info.blendShapeName));
                                        }
                                    }
                                });
                            }
                        }
                        menu.ShowAsContext();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                #endregion

                EditorGUILayout.Space();

                #region BlendShape
                BlendShapeRootNode rootNode = null;
                int RowCount = 0;
                void BlendShapeTreeNodeGUI(BlendShapeNode mg, int level, int brotherMaxLevel)
                {
                    var indentSpace = GUIStyles.IndentWidth * level;
                    EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                    {
                        {
                            var rect = EditorGUILayout.GetControlRect(false, GUILayout.Width(GUIStyles.FoldoutWidth));
                            EditorGUI.BeginChangeCheck();
                            mg.foldout = EditorGUI.Foldout(rect, mg.foldout, "", true);
                            if (EditorGUI.EndChangeCheck())
                            {
                                if (e.alt)
                                    SetBlendShapeFoldout(mg, mg.foldout);
                            }
                        }
                        Styles.guiContentButton.text = mg.name;
                        Styles.guiContentButton.tooltip = mg.name;
                        if (GUILayout.Button(Styles.guiContentButton, GUILayout.Width(VAW.EditorSettings.SettingEditorNameFieldWidth)))
                        {
                            if (Shortcuts.IsKeyControl(e) || e.shift)
                            {
                                var combineGoList = new HashSet<GameObject>(VAW.VA.SelectionGameObjects);
                                var combineVirtualList = new HashSet<HumanBodyBones>();
                                if (VAW.VA.SelectionHumanVirtualBones != null)
                                    combineVirtualList.UnionWith(VAW.VA.SelectionHumanVirtualBones);
                                var combineBindings = new HashSet<EditorCurveBinding>(VAW.VA.UAw.GetCurveSelection());
                                if (rootNode.renderer != null && rootNode.renderer.gameObject != null)
                                    combineGoList.Add(rootNode.renderer.gameObject);
                                if (rootNode.infoList != null && rootNode.infoList.Length > 0)
                                {
                                    foreach (var info in rootNode.infoList)
                                        combineBindings.Add(VAW.VA.AnimationCurveBindingBlendShape(rootNode.renderer, info.blendShapeName));
                                }
                                VAW.VA.SelectGameObjects(combineGoList, combineVirtualList);
                                VAW.VA.SetAnimationWindowSynchroSelection(combineBindings);
                            }
                            else
                            {
                                var combineGoList = new List<GameObject>();
                                var combineBindings = new List<EditorCurveBinding>();
                                if (rootNode.renderer != null && rootNode.renderer.gameObject != null)
                                    combineGoList.Add(rootNode.renderer.gameObject);
                                if (rootNode.infoList != null && rootNode.infoList.Length > 0)
                                {
                                    foreach (var info in rootNode.infoList)
                                        combineBindings.Add(VAW.VA.AnimationCurveBindingBlendShape(rootNode.renderer, info.blendShapeName));
                                }
                                VAW.VA.SelectGameObjects(combineGoList);
                                VAW.VA.SetAnimationWindowSynchroSelection(combineBindings);
                            }
                        }
                        {
                            GUILayout.Space(GUIStyles.FoldoutSpace);
                        }
                        {
                            EditorGUI.BeginChangeCheck();
                            var value = GUILayout.HorizontalSlider(blendShapeGroupValues[blendShapeGroupTreeTable[mg]], 0f, 100f);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(VAE, UndoChangeBlendShapeGroup);
                                blendShapeGroupValues[blendShapeGroupTreeTable[mg]] = value;
                                if (mg.infoList != null && mg.infoList.Length > 0)
                                {
                                    foreach (var info in mg.infoList)
                                    {
                                        VAW.VA.SetAnimationValueBlendShape(rootNode.renderer, info.blendShapeName, value);
                                    }
                                }
                            }
                        }
                        {
                            var width = GUIStyles.FloatFieldWidth + GUIStyles.IndentWidth * Math.Max(0, brotherMaxLevel);
                            EditorGUI.BeginChangeCheck();
                            var value = EditorGUILayout.FloatField(blendShapeGroupValues[blendShapeGroupTreeTable[mg]], GUILayout.Width(width));
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(VAE, UndoChangeBlendShapeGroup);
                                blendShapeGroupValues[blendShapeGroupTreeTable[mg]] = value;
                                if (mg.infoList != null && mg.infoList.Length > 0)
                                {
                                    foreach (var info in mg.infoList)
                                    {
                                        VAW.VA.SetAnimationValueBlendShape(rootNode.renderer, info.blendShapeName, value);
                                    }
                                }
                            }
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    if (mg.foldout)
                    {
                        if (mg.infoList != null && mg.infoList.Length > 0)
                        {
                            #region BlendShape
                            foreach (var info in mg.infoList)
                            {
                                var blendShapeValue = VAW.VA.GetAnimationValueBlendShape(rootNode.renderer, info.blendShapeName);
                                EditorGUILayout.BeginHorizontal(RowCount++ % 2 == 0 ? VAW.GuiStyleAnimationRowEvenStyle : VAW.GuiStyleAnimationRowOddStyle);
                                {
                                    EditorGUILayout.GetControlRect(false, GUILayout.Width(indentSpace + GUIStyles.FoldoutWidth));
                                    Styles.guiContentButton.text = info.blendShapeName;
                                    Styles.guiContentButton.tooltip = info.blendShapeName;
                                    if (GUILayout.Button(Styles.guiContentButton, GUILayout.Width(VAW.EditorSettings.SettingEditorNameFieldWidth)))
                                    {
                                        if (Shortcuts.IsKeyControl(e) || e.shift)
                                        {
                                            var combineGoList = new HashSet<GameObject>(VAW.VA.SelectionGameObjects);
                                            var combineVirtualList = new HashSet<HumanBodyBones>();
                                            if (VAW.VA.SelectionHumanVirtualBones != null)
                                                combineVirtualList.UnionWith(VAW.VA.SelectionHumanVirtualBones);
                                            var combineBindings = new HashSet<EditorCurveBinding>(VAW.VA.UAw.GetCurveSelection());
                                            if (rootNode.renderer != null && rootNode.renderer.gameObject != null)
                                                combineGoList.Add(rootNode.renderer.gameObject);
                                            combineBindings.Add(VAW.VA.AnimationCurveBindingBlendShape(rootNode.renderer, info.blendShapeName));
                                            VAW.VA.SelectGameObjects(combineGoList, combineVirtualList);
                                            VAW.VA.SetAnimationWindowSynchroSelection(combineBindings);
                                        }
                                        else
                                        {
                                            if (rootNode.renderer != null && rootNode.renderer.gameObject != null)
                                                VAW.VA.SelectGameObject(rootNode.renderer.gameObject);
                                            VAW.VA.SetAnimationWindowSynchroSelection(new EditorCurveBinding[] { VAW.VA.AnimationCurveBindingBlendShape(rootNode.renderer, info.blendShapeName) });
                                        }
                                    }
                                }
                                {
                                    var mirrorName = VAW.VA.GetMirrorBlendShape(rootNode.renderer, info.blendShapeName);
                                    if (!string.IsNullOrEmpty(mirrorName))
                                    {
                                        Styles.guiContentButton.text = "";
                                        Styles.guiContentButton.tooltip = mirrorName;
                                        if (GUILayout.Button(Styles.guiContentButton, VAW.GuiStyleMirrorButton, GUILayout.Width(VAW.MirrorTex.width), GUILayout.Height(VAW.MirrorTex.height)))
                                        {
                                            if (rootNode.renderer != null && rootNode.renderer.gameObject != null)
                                                VAW.VA.SelectGameObject(rootNode.renderer.gameObject);
                                            VAW.VA.SetAnimationWindowSynchroSelection(new EditorCurveBinding[] { VAW.VA.AnimationCurveBindingBlendShape(rootNode.renderer, mirrorName) });
                                        }
                                    }
                                    else
                                    {
                                        GUILayout.Space(GUIStyles.FoldoutSpace);
                                    }
                                    if (blendShapeMirrorName)
                                    {
                                        var mirrorIndex = ArrayUtility.IndexOf(rootNode.blendShapeNames, mirrorName);
                                        EditorGUI.BeginChangeCheck();
                                        mirrorIndex = EditorGUILayout.Popup(mirrorIndex, rootNode.blendShapeNames);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            string newMirrorName = mirrorIndex > 0 ? rootNode.blendShapeNames[mirrorIndex] : null;
                                            if (info.blendShapeName == newMirrorName)
                                                newMirrorName = null;
                                            VAW.VA.ChangeBlendShapeMirror(rootNode.renderer, info.blendShapeName, newMirrorName);
                                            if (!string.IsNullOrEmpty(newMirrorName))
                                                VAW.VA.ChangeBlendShapeMirror(rootNode.renderer, newMirrorName, info.blendShapeName);
                                        }
                                    }
                                }
                                {
                                    EditorGUI.BeginChangeCheck();
                                    var value2 = GUILayout.HorizontalSlider(blendShapeValue, 0f, 100f);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        VAW.VA.SetAnimationValueBlendShape(rootNode.renderer, info.blendShapeName, value2);
                                    }
                                }
                                {
                                    EditorGUI.BeginChangeCheck();
                                    var value2 = EditorGUILayout.FloatField(blendShapeValue, GUILayout.Width(GUIStyles.FloatFieldWidth));
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        VAW.VA.SetAnimationValueBlendShape(rootNode.renderer, info.blendShapeName, value2);
                                    }
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                            #endregion
                        }
                    }
                }

                {
                    int maxLevel = 0;
                    foreach (var root in mgRoot)
                    {
                        if (root.renderer != null && root.mesh != null && root.renderer.sharedMesh == root.mesh)
                        {
                            if (root.foldout)
                                maxLevel = Math.Max(maxLevel, 1);
                        }
                    }
                    foreach (var root in mgRoot)
                    {
                        if (root.renderer != null && root.mesh != null && root.renderer.sharedMesh == root.mesh)
                        {
                            rootNode = root;
                            BlendShapeTreeNodeGUI(root, 1, maxLevel);
                        }
                    }
                }
                #endregion
                #endregion
            }
            else if (blendShapeMode == BlendShapeMode.List)
            {
                #region List
                if (e.type == EventType.Layout)
                {
                    UpdateBlendShapeSetListReorderableList();
                }
                blendShapeSetListReorderableList?.DoLayoutList();
                #endregion
            }
            else if (blendShapeMode == BlendShapeMode.Icon)
            {
                #region Icon
                if (e.type == EventType.Layout)
                {
                    UpdateBlendShapeSetIcon();
                }
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                    {
                        EditorGUI.BeginChangeCheck();
                        iconCameraMode = (IconCameraMode)EditorGUILayout.EnumPopup(iconCameraMode, EditorStyles.toolbarDropDown, GUILayout.Width(80f));
                        if (EditorGUI.EndChangeCheck())
                        {
                            iconUpdate = true;
                        }
                    }
                    {
                        EditorGUI.BeginChangeCheck();
                        iconCameraBounds = (IconCameraBounds)EditorGUILayout.EnumPopup(iconCameraBounds, EditorStyles.toolbarDropDown, GUILayout.Width(200f));
                        if (EditorGUI.EndChangeCheck())
                        {
                            iconUpdate = true;
                        }
                    }
                    EditorGUILayout.Space();
                    iconShowName = GUILayout.Toggle(iconShowName, "Show Name", EditorStyles.toolbarButton);
                    EditorGUILayout.Space();
                    iconSize = EditorGUILayout.Slider(iconSize, 32f, IconTextureSize);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space();
                if (VAW.VA.blendShapeSetList.Count > 0)
                {
                    float areaWidth = VAE.position.width - 16f;
                    int countX = Math.Max(1, Mathf.FloorToInt(areaWidth / iconSize));
                    int countY = Mathf.CeilToInt(VAW.VA.blendShapeSetList.Count / (float)countX);
                    for (int i = 0; i < countY; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        for (int j = 0; j < countX; j++)
                        {
                            var index = i * countX + j;
                            if (index >= VAW.VA.blendShapeSetList.Count) break;
                            var rect = EditorGUILayout.GetControlRect(false, iconSize, Styles.guiStyleIconButton, GUILayout.Width(iconSize), GUILayout.Height(iconSize));
                            if (GUI.Button(rect, VAW.VA.blendShapeSetList[index].icon, Styles.guiStyleIconButton))
                            {
                                var poseTemplate = VAW.VA.blendShapeSetList[index].poseTemplate;
                                if (Shortcuts.IsKeyControl(e) || e.shift)
                                    VAW.VA.LoadPoseTemplate(poseTemplate, VeryAnimation.PoseFlags.BlendShape, false, true);
                                else
                                    VAW.VA.LoadPoseTemplate(poseTemplate, VeryAnimation.PoseFlags.BlendShape);
                            }
                            if (iconShowName)
                            {
                                var name = VAW.VA.blendShapeSetList[index].poseTemplate.name;
                                Styles.guiContentButton.text = name;
                                var size = Styles.guiStyleNameLabelCenter.CalcSize(Styles.guiContentButton);
                                if (size.x < rect.width)
                                    EditorGUI.DropShadowLabel(rect, name, Styles.guiStyleNameLabelCenter);
                                else
                                    EditorGUI.DropShadowLabel(rect, name, Styles.guiStyleNameLabelRight);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("List is Empty", EditorStyles.centeredGreyMiniLabel);
                }
                #endregion
            }
            EditorGUILayout.EndVertical();
        }

        private void UpdateBlendShapeSetListReorderableList()
        {
            if (blendShapeSetListReorderableList != null)
                return;

            blendShapeSetListReorderableList = new ReorderableList(VAW.VA.blendShapeSetList, typeof(PoseTemplate), draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true)
            {
                drawHeaderCallback = rect =>
                {
                    float x = rect.x;
                    {
                        const float ButtonWidth = 100f;
                        #region Add
                        {
                            var r = rect;
                            r.width = ButtonWidth;
                            if (GUI.Button(r, Language.GetContent(Language.Help.BlendShapeTemplate), EditorStyles.toolbarDropDown))
                            {
                                var blendShapeTemplates = EditorCommon.CollectAssetPaths("t:blendshapetemplate");

                                var menu = new GenericMenu();
                                {
                                    menu.AddItem(new GUIContent("All"), false, () =>
                                    {
                                        Undo.RecordObject(VAW, UndoTemplateBlendShape);
                                        {
                                            var basePoseTemplate = ScriptableObject.CreateInstance<PoseTemplate>();
                                            VAW.VA.SavePoseTemplate(basePoseTemplate, VeryAnimation.PoseFlags.BlendShape);
                                            for (int i = 0; i < basePoseTemplate.blendShapeValues.Length; i++)
                                            {
                                                for (int j = 0; j < basePoseTemplate.blendShapeValues[i].weights.Length; j++)
                                                    basePoseTemplate.blendShapeValues[i].weights[j] = 0f;
                                            }
                                            for (int i = 0; i < basePoseTemplate.blendShapeValues.Length; i++)
                                            {
                                                var renderer = VAW.GameObject.transform.Find(basePoseTemplate.blendShapePaths[i]);
                                                if (renderer == null)
                                                    continue;
                                                for (int j = 0; j < basePoseTemplate.blendShapeValues[i].weights.Length; j++)
                                                {
                                                    var poseTemplate = ScriptableObject.Instantiate(basePoseTemplate);
                                                    poseTemplate.name = $"{renderer.name}/{basePoseTemplate.blendShapeValues[i].names[j]}";
                                                    poseTemplate.blendShapeValues[i].weights[j] = 100f;
                                                    VAW.VA.blendShapeSetList.Add(new VeryAnimation.BlendShapeSet()
                                                    {
                                                        poseTemplate = poseTemplate,
                                                    });
                                                }
                                            }
                                            ScriptableObject.DestroyImmediate(basePoseTemplate);
                                        }
                                        iconUpdate = true;
                                    });
                                    menu.AddSeparator("");
                                    {
                                        foreach (var kv in blendShapeTemplates)
                                        {
                                            var value = kv.Value;
                                            menu.AddItem(new GUIContent($"Template/{kv.Key}"), false, () =>
                                            {
                                                var blendShapeTemplate = AssetDatabase.LoadAssetAtPath<BlendShapeTemplate>(value);
                                                if (blendShapeTemplate != null)
                                                {
                                                    Undo.RecordObject(VAW, UndoTemplateBlendShape);
                                                    foreach (var template in blendShapeTemplate.list)
                                                    {
                                                        var set = new VeryAnimation.BlendShapeSet
                                                        {
                                                            poseTemplate = template.GetPoseTemplate()
                                                        };
                                                        VAW.VA.blendShapeSetList.Add(set);
                                                    }
                                                    iconUpdate = true;
                                                }
                                            });
                                        }
                                    }
                                }
                                menu.ShowAsContext();
                            }
                        }
                        #endregion
                        #region Clear
                        {
                            var r = rect;
                            r.xMin += ButtonWidth;
                            r.width = ButtonWidth;
                            if (GUI.Button(r, "Clear", EditorStyles.toolbarButton))
                            {
                                Undo.RecordObject(VAW, "Clear BlendShape");
                                VAW.VA.blendShapeSetList.Clear();
                            }
                        }
                        #endregion
                        #region Save as
                        {
                            var r = rect;
                            r.width = ButtonWidth;
                            r.x = rect.xMax - r.width;
                            if (GUI.Button(r, Language.GetContent(Language.Help.BlendShapeSaveAs), EditorStyles.toolbarButton))
                            {
                                string path = EditorCommon.SaveFilePanelInAssets("Save as BlendShape Template", VAE.TemplateSaveDefaultDirectory, $"{VAW.GameObject.name}_BlendShape.asset", "asset");
                                if (path != null)
                                {
                                    VAE.TemplateSaveDefaultDirectory = Path.GetDirectoryName(path);
                                    {
                                        var blendShapeTemplate = ScriptableObject.CreateInstance<BlendShapeTemplate>();
                                        {
                                            foreach (var set in VAW.VA.blendShapeSetList)
                                            {
                                                blendShapeTemplate.Add(set.poseTemplate);
                                            }
                                        }
                                        using (new VeryAnimationWindow.CustomAssetModificationProcessor.PauseScope())
                                        {
                                            AssetDatabase.CreateAsset(blendShapeTemplate, path);
                                        }
                                        VAE.Focus();
                                    }
                                }
                            }
                        }
                        #endregion
                    }
                }
            };
            blendShapeSetListReorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                if (index >= VAW.VA.blendShapeSetList.Count)
                    return;

                float x = rect.x;
                {
                    const float Rate = 0.7f;
                    var r = rect;
                    r.x = x;
                    r.y += 2;
                    r.height -= 4;
                    r.width = rect.width * Rate;
                    x += r.width;
                    if (index == blendShapeSetListReorderableList.index)
                    {
                        EditorGUI.BeginChangeCheck();
                        var text = EditorGUI.TextField(r, VAW.VA.blendShapeSetList[index].poseTemplate.name);
                        if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(text))
                        {
                            Undo.RecordObject(VAW.VA.blendShapeSetList[index].poseTemplate, "Change set name");
                            VAW.VA.blendShapeSetList[index].poseTemplate.name = text;
                        }
                    }
                    else
                    {
                        EditorGUI.LabelField(r, VAW.VA.blendShapeSetList[index].poseTemplate.name);
                    }
                }
                {
                    const float Rate = 0.15f;
                    var r = rect;
                    r.x = x;
                    r.y += 2;
                    r.height -= 4;
                    r.width = rect.width * Rate;
                    x += r.width;
                    if (GUI.Button(r, Language.GetContent(Language.Help.BlendShapeAddButton)))
                    {
                        var poseTemplate = VAW.VA.blendShapeSetList[index].poseTemplate;
                        VAW.VA.LoadPoseTemplate(poseTemplate, VeryAnimation.PoseFlags.BlendShape, false, true);
                    }
                }
                {
                    const float Rate = 0.15f;
                    var r = rect;
                    r.x = x;
                    r.y += 2;
                    r.height -= 4;
                    r.width = rect.width * Rate;
                    x += r.width;
                    if (GUI.Button(r, Language.GetContent(Language.Help.BlendShapeSetButton)))
                    {
                        var poseTemplate = VAW.VA.blendShapeSetList[index].poseTemplate;
                        VAW.VA.LoadPoseTemplate(poseTemplate, VeryAnimation.PoseFlags.BlendShape);
                    }
                }
            };
            blendShapeSetListReorderableList.onAddCallback = list =>
            {
                Undo.RecordObject(VAW, "Add BlendShape Set");

                var poseTemplate = ScriptableObject.CreateInstance<PoseTemplate>();
                VAW.VA.SavePoseTemplate(poseTemplate, VeryAnimation.PoseFlags.BlendShape);
                {
                    var name = $"Set {VAW.VA.blendShapeSetList.Count}";
                    float max = 0f;
                    for (int i = 0; i < poseTemplate.blendShapeValues.Length; i++)
                    {
                        for (int j = 0; j < poseTemplate.blendShapeValues[i].weights.Length; j++)
                        {
                            if (poseTemplate.blendShapeValues[i].weights[j] > max)
                            {
                                var renderer = VAW.GameObject.transform.Find(poseTemplate.blendShapePaths[i]);
                                if (renderer != null)
                                {
                                    name = $"{renderer.name}/{poseTemplate.blendShapeValues[i].names[j]}";
                                    max = poseTemplate.blendShapeValues[i].weights[j];
                                }
                            }
                        }
                    }
                    poseTemplate.name = name;
                }
                VAW.VA.blendShapeSetList.Add(new VeryAnimation.BlendShapeSet()
                {
                    poseTemplate = poseTemplate,
                });
                iconUpdate = true;
                EditorApplication.delayCall += () =>
                {
                    blendShapeSetListReorderableList.index = VAW.VA.blendShapeSetList.Count - 1;
                    VAE.Repaint();
                };
            };
            blendShapeSetListReorderableList.onRemoveCallback = list =>
            {
                Undo.RecordObject(VAW, "Remove BlendShape Set");
                VAW.VA.blendShapeSetList.RemoveAt(list.index);
                if (list.index >= list.count)
                    list.index = list.count - 1;
            };
        }

        private void UpdateBlendShapeSetIcon()
        {
            if (!iconUpdate)
                return;
            iconUpdate = false;

            if (VAW.VA.blendShapeSetList == null || VAW.VA.blendShapeSetList.Count <= 0)
                return;

            VAW.VA.TransformPoseSave.ResetDefaultTransform();
            VAW.VA.BlendShapeWeightSave.ResetDefaultWeight();

            var gameObject = AnimationCommon.InstantiateForPreview(VAW.GameObject);
            RenderTexture iconTexture = null;
            GameObject cameraObject = null;
            Mesh bakeMesh = null;
            try
            {
                if (gameObject.TryGetComponent<Animator>(out var animator))
                {
                    animator.enabled = true;
                    animator.Rebind();
                    animator.enabled = false;
                }

                var blankLayer = EditorCommon.GetBlankLayer();
                foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                        continue;
                    renderer.gameObject.layer = blankLayer;
                }
                var renderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                    .Where(renderer => renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > 0)
                                    .ToArray();
                foreach (var renderer in renderers)
                {
                    renderer.updateWhenOffscreen = true;
                    renderer.forceMatrixRecalculationPerRender = true;
                }

                Dictionary<Renderer, bool> allRenderersEnabled = new();
                foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                {
                    allRenderersEnabled.Add(renderer, renderer.enabled);
                }

                {
                    iconTexture = new RenderTexture(IconTextureSize, IconTextureSize, 16, RenderTextureFormat.ARGB32);
                    {
                        iconTexture.hideFlags |= HideFlags.HideAndDontSave;
                        iconTexture.Create();
                    }
                    var blankColors = new Color32[IconTextureSize * IconTextureSize];
                    cameraObject = new GameObject();
                    {
                        cameraObject.hideFlags |= HideFlags.HideAndDontSave;
                        cameraObject.transform.SetParent(gameObject.transform);
                    }
                    var camera = cameraObject.AddComponent<Camera>();
                    {
                        camera.targetTexture = iconTexture;
                        camera.clearFlags = CameraClearFlags.Color;
                        camera.backgroundColor = Color.clear;
                        camera.cullingMask = 1 << blankLayer;
                    }

                    bakeMesh = new();
                    {
                        bakeMesh.hideFlags |= HideFlags.HideAndDontSave;
                    }

                    Dictionary<Renderer, Vector3[]> defaultVertices = new();
                    var vertices = new List<Vector3>();
                    if (iconCameraBounds != IconCameraBounds.allRenderers)
                    {
                        foreach (var renderer in renderers)
                        {
                            if (renderer.sharedMesh == null)
                                continue;

                            renderer.BakeMesh(bakeMesh);
                            bakeMesh.GetVertices(vertices);

                            defaultVertices.Add(renderer, vertices.ToArray());
                        }
                    }

                    foreach (var set in VAW.VA.blendShapeSetList)
                    {
                        Bounds bounds = new();

                        switch (iconCameraBounds)
                        {
                            case IconCameraBounds.allRenderers:
                            case IconCameraBounds.focusChangedRenderers:
                                foreach (var pair in allRenderersEnabled)
                                {
                                    pair.Key.enabled = pair.Value;
                                }
                                break;
                            case IconCameraBounds.onlyRenderersWithChanges:
                                foreach (var pair in allRenderersEnabled)
                                {
                                    pair.Key.enabled = false;
                                }
                                break;
                        }

                        if (set.poseTemplate.blendShapePaths != null && set.poseTemplate.blendShapeValues != null)
                        {
                            var blendShapePathIndexTable = new Dictionary<string, int>(set.poseTemplate.blendShapePaths.Length);
                            for (int i = 0; i < set.poseTemplate.blendShapePaths.Length; i++)
                            {
                                blendShapePathIndexTable.TryAdd(set.poseTemplate.blendShapePaths[i], i);
                            }
                            foreach (var renderer in renderers)
                            {
                                var path = AnimationUtility.CalculateTransformPath(renderer.transform, gameObject.transform);
                                if (!blendShapePathIndexTable.TryGetValue(path, out int index))
                                    continue;
                                var blendShapeIndexTable = new Dictionary<string, int>(renderer.sharedMesh.blendShapeCount);
                                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                                {
                                    blendShapeIndexTable.TryAdd(renderer.sharedMesh.GetBlendShapeName(i), i);
                                }

                                for (int i = 0; i < set.poseTemplate.blendShapeValues[index].names.Length; i++)
                                {
                                    if (!blendShapeIndexTable.TryGetValue(set.poseTemplate.blendShapeValues[index].names[i], out int sindex))
                                        continue;
                                    renderer.SetBlendShapeWeight(sindex, set.poseTemplate.blendShapeValues[index].weights[i]);
                                }

                                switch (iconCameraBounds)
                                {
                                    case IconCameraBounds.allRenderers:
                                        {
                                            if (Mathf.Approximately(bounds.size.sqrMagnitude, 0f))
                                                bounds = renderer.bounds;
                                            else
                                                bounds.Encapsulate(renderer.bounds);
                                        }
                                        break;
                                    case IconCameraBounds.focusChangedRenderers:
                                    case IconCameraBounds.onlyRenderersWithChanges:
                                        {
                                            renderer.BakeMesh(bakeMesh);
                                            bakeMesh.GetVertices(vertices);
                                            var bakeVertices = defaultVertices[renderer];

                                            if (bakeVertices != null && vertices.Count == bakeVertices.Length)
                                            {
                                                for (int i = 0; i < vertices.Count; i++)
                                                {
                                                    if (vertices[i] != bakeVertices[i])
                                                    {
                                                        if (Mathf.Approximately(bounds.size.sqrMagnitude, 0f))
                                                            bounds = renderer.bounds;
                                                        else
                                                            bounds.Encapsulate(renderer.bounds);
                                                        renderer.enabled = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                }
                            }
                        }

                        {
                            var transform = camera.transform;
                            var sizeMax = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                            switch (iconCameraMode)
                            {
                                case IconCameraMode.forward:
                                    {
                                        var rot = Quaternion.AngleAxis(180f, Vector3.up);
                                        transform.localRotation = rot;
                                        sizeMax = Mathf.Max(bounds.size.x, bounds.size.y);
                                        transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z) - transform.forward;
                                    }
                                    break;
                                case IconCameraMode.back:
                                    {
                                        transform.localRotation = Quaternion.identity;
                                        sizeMax = Mathf.Max(bounds.size.x, bounds.size.y);
                                        transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z) - transform.forward;
                                    }
                                    break;
                                case IconCameraMode.up:
                                    {
                                        var rot = Quaternion.AngleAxis(90f, Vector3.right);
                                        transform.localRotation = rot;
                                        sizeMax = Mathf.Max(bounds.size.x, bounds.size.z);
                                        transform.localPosition = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) - transform.forward;
                                    }
                                    break;
                                case IconCameraMode.down:
                                    {
                                        var rot = Quaternion.AngleAxis(-90f, Vector3.right);
                                        transform.localRotation = rot;
                                        sizeMax = Mathf.Max(bounds.size.x, bounds.size.z);
                                        transform.localPosition = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) - transform.forward;
                                    }
                                    break;
                                case IconCameraMode.right:
                                    {
                                        var rot = Quaternion.AngleAxis(-90f, Vector3.up);
                                        transform.localRotation = rot;
                                        sizeMax = Mathf.Max(bounds.size.y, bounds.size.z);
                                        transform.localPosition = new Vector3(bounds.max.x, bounds.center.y, bounds.center.z) - transform.forward;
                                    }
                                    break;
                                case IconCameraMode.left:
                                    {
                                        var rot = Quaternion.AngleAxis(90f, Vector3.up);
                                        transform.localRotation = rot;
                                        sizeMax = Mathf.Max(bounds.size.y, bounds.size.z);
                                        transform.localPosition = new Vector3(bounds.min.x, bounds.center.y, bounds.center.z) - transform.forward;
                                    }
                                    break;
                            }
                            camera.orthographic = true;
                            camera.orthographicSize = sizeMax * 0.5f;
                            camera.nearClipPlane = 0.0001f;
                            camera.farClipPlane = 1f + sizeMax * 10f;
                        }

                        camera.Render();
                        {
                            RenderTexture save = RenderTexture.active;
                            RenderTexture.active = iconTexture;
                            if (set.icon == null)
                            {
                                set.icon = new Texture2D(iconTexture.width, iconTexture.height, TextureFormat.ARGB32, iconTexture.useMipMap);
                                set.icon.hideFlags |= HideFlags.HideAndDontSave;
                            }
                            if (bounds.size.sqrMagnitude > 0f)
                                set.icon.ReadPixels(new Rect(0, 0, iconTexture.width, iconTexture.height), 0, 0);
                            else
                                set.icon.SetPixels32(blankColors);
                            set.icon.Apply();
                            RenderTexture.active = save;
                        }
                    }

                }
            }
            finally
            {
                if (bakeMesh != null)
                    Mesh.DestroyImmediate(bakeMesh);
                if (cameraObject != null)
                    GameObject.DestroyImmediate(cameraObject);
                if (iconTexture != null)
                {
                    iconTexture.Release();
                    RenderTexture.DestroyImmediate(iconTexture);
                }
                GameObject.DestroyImmediate(gameObject);
            }

            VAW.VA.SetUpdateSampleAnimation();
        }
    }
}
