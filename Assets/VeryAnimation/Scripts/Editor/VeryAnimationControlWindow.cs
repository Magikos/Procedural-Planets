//#define Enable_Profiler

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Assertions;
#if Enable_Profiler
using UnityEngine.Profiling;
#endif
#if UNITY_6000_4_OR_NEWER
using TreeItemId = UnityEngine.EntityId;
using HierarchyTreeViewBase = UnityEditor.IMGUI.Controls.TreeView<UnityEngine.EntityId>;
using HierarchyTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<UnityEngine.EntityId>;
using HierarchyTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<UnityEngine.EntityId>;
#elif UNITY_6000_2_OR_NEWER
using TreeItemId = System.Int32;
using HierarchyTreeViewBase = UnityEditor.IMGUI.Controls.TreeView<int>;
using HierarchyTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using HierarchyTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using TreeItemId = System.Int32;
using HierarchyTreeViewBase = UnityEditor.IMGUI.Controls.TreeView;
using HierarchyTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem;
using HierarchyTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

namespace VeryAnimation
{
    internal sealed class VeryAnimationControlWindow : EditorWindow
    {
        public static VeryAnimationControlWindow instance;

        private VeryAnimationWindow VAW => VeryAnimationWindow.instance;
        private VeryAnimationEditorWindow VAE => VeryAnimationEditorWindow.instance;

        #region Undo Strings
        private const string UndoChangeBoneShowFlag = "Change bone show flag";
        #endregion

        #region EditorPrefs Keys
        private const string PrefKey_AnimatorIK = "VeryAnimation_Control_AnimatorIK";
        private const string PrefKey_OriginalIK = "VeryAnimation_Control_OriginalIK";
        private const string PrefKey_Humanoid = "VeryAnimation_Control_Humanoid";
        private const string PrefKey_Selection = "VeryAnimation_Control_Selection";
        private const string PrefKey_Hierarchy = "VeryAnimation_Control_Hierarchy";
        private const string PrefKey_AnimatorIKVisible = "VeryAnimation_Control_AnimatorIKVisible";
        private const string PrefKey_OriginalIKVisible = "VeryAnimation_Control_OriginalIKVisible";
        private const string PrefKey_HumanoidVisible = "VeryAnimation_Control_HumanoidVisible";
        private const string PrefKey_SelectionVisible = "VeryAnimation_Control_SelectionVisible";
        private const string PrefKey_HierarchyVisible = "VeryAnimation_Control_HierarchyVisible";
        private const string PrefKey_SelectionType = "VeryAnimation_Control_SelectionType";
        private const string PrefKey_HierarchyWriteLock = "VeryAnimation_Control_HierarchyWriteLock";
        private const string PrefKey_HierarchyMirrorObject = "VeryAnimation_Control_HierarchyMirrorObject";
        private const string PrefKey_HierarchyHumanoidName = "VeryAnimation_Control_HierarchyHumanoidName";
        #endregion

        #region Textures
        private Texture2D avatarHead;
        private Texture2D avatarTorso;
        private Texture2D avatarLeftArm;
        private Texture2D avatarLeftFingers;
        private Texture2D avatarLeftLeg;
        private Texture2D avatarRightArm;
        private Texture2D avatarRightFingers;
        private Texture2D avatarRightLeg;
        private Texture2D avatarHeadZoom;
        private Texture2D avatarLeftHandZoom;
        private Texture2D avatarRightHandZoom;
        private Texture2D avatarBodysilhouette;
        private Texture2D avatarHeadzoomsilhouette;
        private Texture2D avatarLefthandzoomsilhouette;
        private Texture2D avatarRighthandzoomsilhouette;
        private Texture2D avatarRoot;
        private Texture2D avatarLeftFeetIk;
        private Texture2D avatarRightFeetIk;
        private Texture2D avatarLeftFingersIk;
        private Texture2D avatarRightFingersIk;
        private Texture2D avatarBodyPartPicker;
        private Texture2D dotfill;
        private Texture2D dotframe;
        private Texture2D dotframedotted;
        private Texture2D dotselection;
        #endregion

        #region GUIStyles
        class GUIStyles
        {
            public readonly GUIStyle guiStyleBackgroundBox;
            public readonly GUIStyle guiStyleVerticalToolbar;
            public readonly GUIStyle guiStyleBoneButton;

            public GUIStyles()
            {
                Assert.IsNotNull(Event.current, "GUIStyles must be created during OnGUI (GUI.skin requires event context)");
                guiStyleBackgroundBox = new GUIStyle("CurveEditorBackground");
                guiStyleVerticalToolbar = new GUIStyle(GUI.skin.button)
                {
                    margin = new RectOffset(0, 0, 0, 0),
                    fontSize = 9
                };
                guiStyleBoneButton = new GUIStyle(GUI.skin.button)
                {
                    border = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    overflow = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0)
                };
                guiStyleBoneButton.active = guiStyleBoneButton.normal;
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

        #region Editor
        internal enum HumanoidAvatarPartsMode
        {
            Body,
            Head,
            LeftHand,
            RightHand,
        }
        private static readonly string[] HumanoidAvatarPartsModeStrings =
        {
            "Body",
            "Head",
            "Left Hand",
            "Right Hand",
        };
        public HumanoidAvatarPartsMode CurrentHumanoidAvatarPartsMode { get; private set; }

        private static readonly Color GrayColor = new(0.2f, 0.2f, 0.2f);
        private static readonly Color GreenColor = new(0.2f, 0.8f, 0.2f);
        private static readonly Color BlueColor = new Color32(102, 178, 255, 255);

        private Vector2 windowScrollPosition;

        private bool guiAnimatorIkFoldout;
        private bool guiOriginalIkFoldout;
        private bool guiHumanoidFoldout;
        private bool guiSelectionFoldout;
        private bool guiHierarchyFoldout;

        private bool guiAnimatorIkVisible = true;
        private bool guiOriginalIkVisible = true;
        private bool guiHumanoidVisible = true;
        private bool guiSelectionVisible = true;
        private bool guiHierarchyVisible = true;

        private bool guiAnimatorIkHelp;
        private bool guiOriginalIkHelp;
        private bool guiHumanoidHelp;
        private bool guiSelectionHelp;
        private bool guiHierarchyHelp;

        private List<HumanBodyBones> selectionGameObjectsHumanoidIndex;
        private Dictionary<HumanBodyBones, Vector2> controlBoneList;
        private AvatarMaskBodyPart selectionAvatarMaskBodyPart;

        private Color[] maskBodyPartPicker;

        private enum SelectionType
        {
            List,
            Popup,
        }
        private static readonly string[] SelectionTypeString =
        {
            SelectionType.List.ToString(),
            SelectionType.Popup.ToString(),
        };
        private SelectionType selectionType;
        private bool updateSelectionList = true;
        private bool updateSelectionPopup = true;
        private int selectionSetIndex = -1;
        private ReorderableList selectionSetList;
        private string[] selectionSetStrings;
        #endregion

        #region Hierarchy
        private class HierarchyTreeView : HierarchyTreeViewBase
        {
            private VeryAnimationWindow VAW => VeryAnimationWindow.instance;
            private VeryAnimationControlWindow VCW => VeryAnimationControlWindow.instance;

            private Dictionary<Type, Texture2D> typeIconDic;

            #region GUIStyles
            class GUIStyles
            {
                public readonly GUIStyle guiStyleLabelActive;
                public readonly GUIStyle guiStyleLabelNonActive;

                public GUIStyles()
                {
                    guiStyleLabelActive = new GUIStyle("TV Line");
                    guiStyleLabelNonActive = new GUIStyle("TV Line");

                    static Color AlphaMultiplied(Color color, float multiplier)
                    {
                        return new(color.r, color.g, color.b, color.a * multiplier);
                    }
                    guiStyleLabelNonActive.normal.textColor = AlphaMultiplied(guiStyleLabelNonActive.normal.textColor, VeryAnimationWindow.GUINonActiveAlpha);
                    guiStyleLabelNonActive.hover.textColor = AlphaMultiplied(guiStyleLabelNonActive.hover.textColor, VeryAnimationWindow.GUINonActiveAlpha);
                    guiStyleLabelNonActive.active.textColor = AlphaMultiplied(guiStyleLabelNonActive.active.textColor, VeryAnimationWindow.GUINonActiveAlpha);
                    guiStyleLabelNonActive.focused.textColor = AlphaMultiplied(guiStyleLabelNonActive.focused.textColor, VeryAnimationWindow.GUINonActiveAlpha);
                    guiStyleLabelNonActive.onNormal.textColor = AlphaMultiplied(guiStyleLabelNonActive.onNormal.textColor, VeryAnimationWindow.GUINonActiveAlpha);
                    guiStyleLabelNonActive.onHover.textColor = AlphaMultiplied(guiStyleLabelNonActive.onHover.textColor, VeryAnimationWindow.GUINonActiveAlpha);
                    guiStyleLabelNonActive.onActive.textColor = AlphaMultiplied(guiStyleLabelNonActive.onActive.textColor, VeryAnimationWindow.GUINonActiveAlpha);
                    guiStyleLabelNonActive.onFocused.textColor = AlphaMultiplied(guiStyleLabelNonActive.onFocused.textColor, VeryAnimationWindow.GUINonActiveAlpha);
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

            private bool IsHierarchyUnavailable => instance == null || VAW == null || VAW.VA == null || VAW.VA.IsEditError;
            private int RootDepth => VCW.hierarchyWriteLock ? 1 : 0;
            
            private Texture2D GetIconTexture(Type type)
            {
                typeIconDic ??= new Dictionary<Type, Texture2D>();
                if (!typeIconDic.TryGetValue(type, out Texture2D tex))
                {
                    tex = VAW.UEditorGUIUtility.LoadIcon($"{type.Name} icon");
                    typeIconDic.Add(type, tex);
                }
                return tex;
            }
            private string GetHierarchyItemName(GameObject go)
            {
                var hi = VAW.VA.HumanoidBonesIndexOf(go);
                if (VAW.VA.IsHuman && instance.hierarchyHumanoidName)
                {
                    if (VAW.GameObject == go)
                        return "Root";
                    if (hi >= 0)
                        return hi.ToString();
                }
                return go.name;
            }
            private Texture2D GetHierarchyItemIcon(GameObject go)
            {
                var boneIndex = VAW.VA.BonesIndexOf(go);
                var tex = boneIndex >= 0 ? GetIconTexture(VAW.VA.GetBoneType(boneIndex)) : null;
                return tex != null ? tex : GetIconTexture(typeof(Transform));
            }
            private Dictionary<TreeItemId, GameObject> hierarchyGameObjectLookup;

            private HierarchyTreeViewItem CreateTreeViewItem(Transform t, int depth)
            {
                var id = GetTreeItemId(t.gameObject);
                hierarchyGameObjectLookup[id] = t.gameObject;
                var item = new HierarchyTreeViewItem(id, depth, GetHierarchyItemName(t.gameObject))
                {
                    icon = GetHierarchyItemIcon(t.gameObject),
                    children = new List<HierarchyTreeViewItem>(t.childCount),
                };
                for (int i = 0; i < t.childCount; i++)
                {
                    item.children.Add(CreateTreeViewItem(t.GetChild(i), depth + 1));
                }
                return item;
            }
            public GameObject FindGameObject(TreeItemId id)
            {
                if (hierarchyGameObjectLookup != null && hierarchyGameObjectLookup.TryGetValue(id, out var go))
                    return go;
                return null;
            }

            public HierarchyTreeView(HierarchyTreeViewState state) : base(state)
            {
                extraSpaceBeforeIconAndLabel = 18f;
            }
            protected override HierarchyTreeViewItem BuildRoot()
            {
#if UNITY_6000_4_OR_NEWER
                var root = new HierarchyTreeViewItem(EntityId.None, -1, "Root");
#else
                var root = new HierarchyTreeViewItem(int.MinValue, -1, "Root");
#endif
                hierarchyGameObjectLookup ??= new Dictionary<TreeItemId, GameObject>();
                hierarchyGameObjectLookup.Clear();
                if (IsHierarchyUnavailable)
                {
                    root.children = new List<HierarchyTreeViewItem>();
                    return root;
                }
                root.children = new List<HierarchyTreeViewItem>(1) { CreateTreeViewItem(VAW.GameObject.transform, RootDepth) };

                return root;
            }

            protected override void SelectionChanged(IList<TreeItemId> selectedIds)
            {
                if (VAW == null || VAW.VA == null || VAW.VA.IsEditError) return;

                HashSet<GameObject> selection = new();
                foreach (var instanceID in selectedIds)
                {
                    var go = FindGameObject(instanceID);
                    if (go != null)
                        selection.Add(go);
                }
                if (Event.current.alt)
                {
                    var lastGo = FindGameObject(state.lastClickedID);
                    var lastBoneIndex = VAW.VA.BonesIndexOf(lastGo);
                    if (lastBoneIndex >= 0)
                    {
                        VAW.VA.ActionAllBoneChildren(lastBoneIndex, (boneIndex) =>
                        {
                            selection.Add(VAW.VA.Bones[boneIndex]);
                        });
                    }
                }
                {
                    var lastGo = FindGameObject(state.lastClickedID);
                    if (lastGo != null)
                        Selection.activeGameObject = lastGo;
                }
                VAW.VA.SelectGameObjects(selection);
            }
            protected override void DoubleClickedItem(TreeItemId id)
            {
                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.FrameSelected();
            }
            protected override bool CanStartDrag(CanStartDragArgs args)
            {
                return true;
            }
            protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
            {
                DragAndDrop.PrepareStartDrag();
                {
                    UnityEngine.Object[] objectReferences = new UnityEngine.Object[args.draggedItemIDs.Count];
                    int count = 0;
                    foreach (var id in args.draggedItemIDs)
                    {
                        var go = FindGameObject(id);
                        if (go == null) continue;
                        objectReferences[count++] = go;
                    }
                    if (count == 0)
                        DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                    else
                    {
                        if (count != objectReferences.Length)
                            Array.Resize(ref objectReferences, count);
                        DragAndDrop.objectReferences = objectReferences;
                    }
                }
                DragAndDrop.StartDrag("Dragging GameObject");
            }
            protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
            {
                return DragAndDropVisualMode.Link;
            }

            private void SetChildrenBoneFlags(HierarchyTreeViewItem item, bool[] targetArray, bool value)
            {
                var go = FindGameObject(item.id);
                if (go != null)
                {
                    var bi = VAW.VA.BonesIndexOf(go);
                    if (bi >= 0)
                        targetArray[bi] = value;
                }
                if (item.hasChildren)
                {
                    foreach (var i in item.children)
                        SetChildrenBoneFlags(i, targetArray, value);
                }
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                if (instance == null || VAW == null || VAW.VA == null || VAW.VA.IsEditError) return;

                const float MirrorIconWidth = 13f;
                const float MirrorIconHeight = 14f;
                const float ToggleIconWidth = 16f;

                var gameObject = FindGameObject(args.item.id);
                if (gameObject != null)
                {
                    var boneIndex = VAW.VA.BonesIndexOf(gameObject);
                    if (boneIndex >= 0)
                    {
                        if (VCW.hierarchyWriteLock)
                        {
                            Rect toggleRect = args.rowRect;
                            toggleRect.width = ToggleIconWidth;

                            EditorGUI.BeginChangeCheck();
                            var flag = EditorGUI.Toggle(toggleRect, VAW.VA.IsWriteLockBone(boneIndex), VAW.GuiStyleLockToggle);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(VAW, "Change bone write lock flag");
                                VAW.VA.boneWriteLockFlags[boneIndex] = flag;
                                if (Event.current.alt && args.item.hasChildren)
                                {
                                    foreach (var i in args.item.children)
                                        SetChildrenBoneFlags(i, VAW.VA.boneWriteLockFlags, flag);
                                }
                            }
                        }
                        {
                            Rect toggleRect = args.rowRect;
                            toggleRect.x += GetContentIndent(args.item);
                            toggleRect.width = ToggleIconWidth;
                            {
                                EditorGUI.BeginChangeCheck();
                                var flag = EditorGUI.Toggle(toggleRect, VAW.VA.boneShowFlags[boneIndex]);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    Undo.RecordObject(VAW, UndoChangeBoneShowFlag);
                                    VAW.VA.boneShowFlags[boneIndex] = flag;
                                    if (Event.current.alt && args.item.hasChildren)
                                    {
                                        foreach (var i in args.item.children)
                                            SetChildrenBoneFlags(i, VAW.VA.boneShowFlags, flag);
                                    }
                                    VAW.VA.OnBoneShowFlagsUpdated.Invoke();
                                }
                            }
                        }
                        float currentX = 0f;
                        if (VCW.hierarchyMirrorObject)
                        {
                            Rect r = args.rowRect;
                            r.width = args.rowRect.width / 3f;
                            r.x = args.rowRect.width - r.width;
                            var mirrorBone = VAW.VA.MirrorBoneIndexes[boneIndex] >= 0 ? VAW.VA.Bones[VAW.VA.MirrorBoneIndexes[boneIndex]] : null;
                            EditorGUI.BeginDisabledGroup((VAW.VA.IsHuman && VAW.VA.BoneIndex2humanoidIndex[boneIndex] >= 0) || boneIndex == 0 || boneIndex == VAW.VA.RootMotionBoneIndex);
                            EditorGUI.BeginChangeCheck();
                            var changeBone = EditorGUI.ObjectField(r, mirrorBone, typeof(GameObject), true) as GameObject;
                            if (EditorGUI.EndChangeCheck())
                            {
                                VAW.VA.ChangeBonesMirror(boneIndex, VAW.VA.BonesIndexOf(changeBone));
                            }
                            EditorGUI.EndDisabledGroup();
                            currentX += r.width;
                        }
                        if (VAW.VA.MirrorBoneIndexes[boneIndex] >= 0)
                        {
                            Rect r = args.rowRect;
                            float Margin = (r.height - MirrorIconHeight) / 2f;
                            r.height -= Margin * 2f;
                            r.y += Margin;
                            r.width = MirrorIconWidth;
                            currentX += r.width;
                            r.x = args.rowRect.width - currentX;
                            if (GUI.Button(r, VAW.VA.MirrorBoneTooltips[boneIndex], VAW.GuiStyleMirrorButton))
                            {
                                VAW.VA.SelectGameObject(VAW.VA.Bones[VAW.VA.MirrorBoneIndexes[boneIndex]]);
                            }
                        }

                        if (Event.current.rawType == EventType.Repaint)
                        {
                            const float IconWidth = 16f;
                            const float SpaceBetweenIconAndText = 2f;

                            var rect = args.rowRect;
                            rect.x += GetContentIndent(args.item) + ToggleIconWidth;

                            {
                                Texture icon = args.item.icon;
                                if (icon != null)
                                {
                                    Rect position = rect;
                                    position.width = IconWidth;
                                    Color color = GUI.color;
                                    color.a *= !VAW.VA.IsWriteLockBone(boneIndex) ? 1f : VeryAnimationWindow.GUINonActiveAlpha;
                                    GUI.DrawTexture(position, icon, ScaleMode.ScaleToFit, alphaBlend: true, 0f, color, 0f, 0f);
                                    rect.xMin += IconWidth + SpaceBetweenIconAndText;
                                }
                            }
                            {
                                var lineStyle = !VAW.VA.IsWriteLockBone(boneIndex) ? Styles.guiStyleLabelActive : Styles.guiStyleLabelNonActive;
                                lineStyle.Draw(rect, args.label, false, false, args.selected, args.focused);
                            }
                        }
                    }
                }
            }
        };

        private bool hierarchyWriteLock;
        private bool hierarchyMirrorObject;
        private bool hierarchyHumanoidName;

        private HierarchyTreeViewState hierarchyTreeState;
        private SearchField hierarchyTreeSearchField;
        private HierarchyTreeView hierarchyTreeView;

        private static TreeItemId GetTreeItemId(GameObject go)
        {
#if UNITY_6000_4_OR_NEWER
            return go.GetEntityId();
#else
            return go.GetInstanceID();
#endif
        }

        private void UpdateHierarchyTree()
        {
            if (hierarchyTreeView == null)
                return;

            var expandList = hierarchyTreeView.GetExpanded();

            hierarchyTreeView.Reload();

            if (expandList == null || expandList.Count == 0)
                return;

            hierarchyTreeView.CollapseAll();
            hierarchyTreeView.SetExpanded(expandList);
        }

        private bool hierarchyButtonAll;
        private bool hierarchyButtonWeight;
        private bool hierarchyButtonRenderer;
        private bool hierarchyButtonRendererParent;
        private bool hierarchyButtonBody;
        private bool hierarchyButtonFace;
        private bool hierarchyButtonLeftHand;
        private bool hierarchyButtonRightHand;

        public void ActionAllExpand(Action<GameObject> action)
        {
            foreach (var id in hierarchyTreeView.GetExpanded())
            {
                var go = hierarchyTreeView.FindGameObject(id);
                if (go != null)
                {
                    action(go);
                }
            }
        }
        public void CollapseAll()
        {
            hierarchyTreeView.CollapseAll();
        }
        public void SetExpand(GameObject go, bool expanded)
        {
            hierarchyTreeView.SetExpanded(GetTreeItemId(go), expanded);
        }
        #endregion

        #region SelectionRect
        private struct SelectionRect
        {
            public void Reset()
            {
                Enable = false;
                Start = Vector2.zero;
                End = Vector2.zero;
                Distance = 0f;
                calcList ??= new List<HumanBodyBones>();
                calcList.Clear();
                beforeSelection = null;
            }
            public void SetStart(Vector2 add)
            {
                Enable = true;
                Start = add;
                End = add;
                Distance = 0f;
            }
            public void SetEnd(Vector2 add)
            {
                Distance += Vector2.Distance(End, add);
                End = add;
            }
            public bool Enable { get; private set; }
            public readonly Vector2 Min => Vector2.Min(Start, End);
            public readonly Vector2 Max => Vector2.Max(Start, End);
            public readonly Rect Rect => new(Min.x, Min.y, Max.x - Min.x, Max.y - Min.y);

            public Vector2 Start { get; private set; }
            public Vector2 End { get; private set; }
            public float Distance { get; private set; }

            public List<HumanBodyBones> calcList;
            public HumanBodyBones[] beforeSelection;
        }
        private SelectionRect selectionRect;
        #endregion

        private bool initialized;

        void OnEnable()
        {
            if (VAW == null || VAW.VA == null) return;

            instance = this;

            titleContent = new GUIContent("VA Control");
            avatarHead = EditorGUIUtility.IconContent("avatarinspector/head").image as Texture2D;
            avatarTorso = EditorGUIUtility.IconContent("avatarinspector/torso").image as Texture2D;
            avatarLeftArm = EditorGUIUtility.IconContent("avatarinspector/leftarm").image as Texture2D;
            avatarLeftFingers = EditorGUIUtility.IconContent("avatarinspector/leftfingers").image as Texture2D;
            avatarLeftLeg = EditorGUIUtility.IconContent("avatarinspector/leftleg").image as Texture2D;
            avatarRightArm = EditorGUIUtility.IconContent("avatarinspector/rightarm").image as Texture2D;
            avatarRightFingers = EditorGUIUtility.IconContent("avatarinspector/rightfingers").image as Texture2D;
            avatarRightLeg = EditorGUIUtility.IconContent("avatarinspector/rightleg").image as Texture2D;
            avatarHeadZoom = EditorGUIUtility.IconContent("avatarinspector/headzoom").image as Texture2D;
            avatarLeftHandZoom = EditorGUIUtility.IconContent("avatarinspector/lefthandzoom").image as Texture2D;
            avatarRightHandZoom = EditorGUIUtility.IconContent("avatarinspector/righthandzoom").image as Texture2D;
            avatarBodysilhouette = EditorGUIUtility.IconContent("avatarinspector/bodysilhouette").image as Texture2D;
            avatarHeadzoomsilhouette = EditorGUIUtility.IconContent("avatarinspector/headzoomsilhouette").image as Texture2D;
            avatarLefthandzoomsilhouette = EditorGUIUtility.IconContent("avatarinspector/lefthandzoomsilhouette").image as Texture2D;
            avatarRighthandzoomsilhouette = EditorGUIUtility.IconContent("avatarinspector/righthandzoomsilhouette").image as Texture2D;
            avatarRoot = EditorGUIUtility.IconContent("avatarinspector/MaskEditor_Root").image as Texture2D;
            avatarLeftFeetIk = EditorGUIUtility.IconContent("avatarinspector/leftfeetik").image as Texture2D;
            avatarRightFeetIk = EditorGUIUtility.IconContent("avatarinspector/rightfeetik").image as Texture2D;
            avatarLeftFingersIk = EditorGUIUtility.IconContent("avatarinspector/leftfingersik").image as Texture2D;
            avatarRightFingersIk = EditorGUIUtility.IconContent("avatarinspector/rightfingersik").image as Texture2D;
            avatarBodyPartPicker = EditorGUIUtility.IconContent("avatarinspector/bodypartpicker").image as Texture2D;
            dotfill = EditorGUIUtility.IconContent("avatarinspector/dotfill").image as Texture2D;
            dotframe = EditorGUIUtility.IconContent("avatarinspector/dotframe").image as Texture2D;
            dotframedotted = EditorGUIUtility.IconContent("avatarinspector/dotframedotted").image as Texture2D;
            dotselection = EditorGUIUtility.IconContent("avatarinspector/dotselection").image as Texture2D;

            {
                var uBodyMaskEditor = new UBodyMaskEditor();
                maskBodyPartPicker = uBodyMaskEditor.GetMaskBodyPartPicker();
            }

            {
                hierarchyTreeState = new HierarchyTreeViewState();
                hierarchyTreeSearchField = new SearchField();
                hierarchyTreeView = new HierarchyTreeView(hierarchyTreeState);
                hierarchyTreeSearchField.downOrUpArrowKeyPressed += hierarchyTreeView.SetFocusAndEnsureSelectedItem;
            }

            selectionGameObjectsHumanoidIndex = new List<HumanBodyBones>();
            controlBoneList = new Dictionary<HumanBodyBones, Vector2>();
            selectionAvatarMaskBodyPart = (AvatarMaskBodyPart)(-1);

            VAW.VA.OnHierarchyUpdated += UpdateHierarchyTree;
            VAW.VA.OnBoneShowFlagsUpdated += UpdateHierarchyFlags;

            OnSelectionChange();

            Undo.undoRedoPerformed += UndoRedoPerformed;
        }
        void OnDisable()
        {
            if (VAW != null && VAW.VA != null)
            {
                VAW.VA.OnHierarchyUpdated -= UpdateHierarchyTree;
                VAW.VA.OnBoneShowFlagsUpdated -= UpdateHierarchyFlags;
            }
            Undo.undoRedoPerformed -= UndoRedoPerformed;

            Release();

            instance = null;
        }

        public void Initialize()
        {
            Release();

            #region EditorPref
            {
                guiAnimatorIkFoldout = EditorPrefs.GetBool(PrefKey_AnimatorIK, false);
                guiOriginalIkFoldout = EditorPrefs.GetBool(PrefKey_OriginalIK, false);
                guiHumanoidFoldout = EditorPrefs.GetBool(PrefKey_Humanoid, true);
                guiSelectionFoldout = EditorPrefs.GetBool(PrefKey_Selection, false);
                guiHierarchyFoldout = EditorPrefs.GetBool(PrefKey_Hierarchy, true);

                guiAnimatorIkVisible = EditorPrefs.GetBool(PrefKey_AnimatorIKVisible, true);
                guiOriginalIkVisible = EditorPrefs.GetBool(PrefKey_OriginalIKVisible, true);
                guiHumanoidVisible = EditorPrefs.GetBool(PrefKey_HumanoidVisible, true);
                guiSelectionVisible = EditorPrefs.GetBool(PrefKey_SelectionVisible, true);
                guiHierarchyVisible = EditorPrefs.GetBool(PrefKey_HierarchyVisible, true);

                selectionType = (SelectionType)EditorPrefs.GetInt(PrefKey_SelectionType, 0);
                hierarchyWriteLock = EditorPrefs.GetBool(PrefKey_HierarchyWriteLock, false);
                hierarchyMirrorObject = EditorPrefs.GetBool(PrefKey_HierarchyMirrorObject, false);
                hierarchyHumanoidName = EditorPrefs.GetBool(PrefKey_HierarchyHumanoidName, true);
            }
            #endregion

            updateSelectionList = true;
            updateSelectionPopup = true;

            UpdateHierarchyTree();
            UpdateHierarchyFlags();

            hierarchyTreeView.ExpandAll();

            initialized = true;
        }
        private void Release()
        {
            if (!initialized) return;

            #region EditorPref
            {
                EditorPrefs.SetBool(PrefKey_AnimatorIK, guiAnimatorIkFoldout);
                EditorPrefs.SetBool(PrefKey_OriginalIK, guiOriginalIkFoldout);
                EditorPrefs.SetBool(PrefKey_Humanoid, guiHumanoidFoldout);
                EditorPrefs.SetBool(PrefKey_Selection, guiSelectionFoldout);
                EditorPrefs.SetBool(PrefKey_Hierarchy, guiHierarchyFoldout);

                EditorPrefs.SetBool(PrefKey_AnimatorIKVisible, guiAnimatorIkVisible);
                EditorPrefs.SetBool(PrefKey_OriginalIKVisible, guiOriginalIkVisible);
                EditorPrefs.SetBool(PrefKey_HumanoidVisible, guiHumanoidVisible);
                EditorPrefs.SetBool(PrefKey_SelectionVisible, guiSelectionVisible);
                EditorPrefs.SetBool(PrefKey_HierarchyVisible, guiHierarchyVisible);
            }
            #endregion
        }

        void OnSelectionChange()
        {
            if (VAW == null || VAW.VA == null || VAW.VA.IsEditError) return;

            if (hierarchyTreeState != null)
            {
                List<TreeItemId> selectedIDs = new();
                foreach (var go in Selection.gameObjects)
                {
                    selectedIDs.Add(GetTreeItemId(go));
                    if (VAW.EditorSettings.SettingHierarchyExpandSelectObject)
                    {
                        var tmp = go.transform.parent;
                        while (tmp != null)
                        {
                            SetExpand(tmp.gameObject, true);
                            tmp = tmp.transform.parent;
                        }
                    }
                }
                hierarchyTreeState.selectedIDs = selectedIDs;

                if (VAW.EditorSettings.SettingHierarchyExpandSelectObject &&
                    Selection.activeGameObject != null)
                {
                    try
                    {
                        hierarchyTreeView.FrameItem(GetTreeItemId(Selection.activeGameObject));
                    }
                    catch
                    {
                    }
                }
            }

            if (guiSelectionFoldout)
            {
                EditorApplication.delayCall += () =>
                {
                    if (this == null) return;
                    UpdateSelection();
                };
            }

            Repaint();
        }

        void OnInspectorUpdate()
        {
            if (VAW == null || VAW.VA == null || !VAW.Initialized || VAE == null)
            {
                Close();
                return;
            }
        }

        void OnGUI()
        {
            if (VAW == null || VAW.VA == null || VAW.VA.IsEditError || !VAW.IsGuiStyleReady) return;

#if Enable_Profiler
            Profiler.BeginSample("****VeryAnimationControlWindow.OnGUI");
#endif

            Event e = Event.current;
            bool repaint = false;

            #region Event
            switch (e.type)
            {
                case EventType.MouseUp:
                    SceneView.RepaintAll();
                    break;
            }
            #endregion

            windowScrollPosition = EditorGUILayout.BeginScrollView(windowScrollPosition);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                if (VAW.VA.IsHuman && guiAnimatorIkVisible)
                {
                    guiAnimatorIkFoldout = GUILayout.Toggle(guiAnimatorIkFoldout, "Animator IK", EditorStyles.toolbarButton);
                }
                if (guiOriginalIkVisible)
                {
                    guiOriginalIkFoldout = GUILayout.Toggle(guiOriginalIkFoldout, "Original IK", EditorStyles.toolbarButton);
                }
                if (VAW.VA.IsHuman && guiHumanoidVisible)
                {
                    guiHumanoidFoldout = GUILayout.Toggle(guiHumanoidFoldout, "Humanoid", EditorStyles.toolbarButton);
                }
                if (guiSelectionVisible)
                {
                    EditorGUI.BeginChangeCheck();
                    guiSelectionFoldout = GUILayout.Toggle(guiSelectionFoldout, "Selection", EditorStyles.toolbarButton);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (guiSelectionFoldout)
                            UpdateSelection();
                    }
                }
                if (guiHierarchyVisible)
                {
                    guiHierarchyFoldout = GUILayout.Toggle(guiHierarchyFoldout, "Hierarchy", EditorStyles.toolbarButton);
                }
                if (EditorGUILayout.DropdownButton(VAW.UEditorGUI.GUIContents.GetTitleSettingsIcon(), FocusType.Passive, VAW.GuiStyleIconButton, GUILayout.Width(19)))
                {
                    GenericMenu menu = new();
                    if (VAW.VA.IsHuman)
                        menu.AddItem(new GUIContent("Animator IK"), guiAnimatorIkVisible, () => { guiAnimatorIkVisible = !guiAnimatorIkVisible; guiAnimatorIkFoldout = guiAnimatorIkVisible; });
                    menu.AddItem(new GUIContent("Original IK"), guiOriginalIkVisible, () => { guiOriginalIkVisible = !guiOriginalIkVisible; guiOriginalIkFoldout = guiOriginalIkVisible; });
                    if (VAW.VA.IsHuman)
                        menu.AddItem(new GUIContent("Humanoid"), guiHumanoidVisible, () => { guiHumanoidVisible = !guiHumanoidVisible; guiHumanoidFoldout = guiHumanoidVisible; });
                    menu.AddItem(new GUIContent("Selection"), guiSelectionVisible, () => { guiSelectionVisible = !guiSelectionVisible; guiSelectionFoldout = guiSelectionVisible; });
                    menu.AddItem(new GUIContent("Hierarchy"), guiHierarchyVisible, () => { guiHierarchyVisible = !guiHierarchyVisible; guiHierarchyFoldout = guiHierarchyVisible; });
                    menu.ShowAsContext();
                }
            }
            EditorGUILayout.EndHorizontal();

            #region AnimatorIK
            if (VAW.VA.IsHuman && guiAnimatorIkFoldout && guiAnimatorIkVisible)
            {
                EditorGUILayout.BeginHorizontal();
                guiAnimatorIkFoldout = EditorGUILayout.Foldout(guiAnimatorIkFoldout, "Animator IK", true, VAW.GuiStyleBoldFoldout);
                {
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), guiAnimatorIkHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        guiAnimatorIkHelp = !guiAnimatorIkHelp;
                    }
#if VERYANIMATION_ANIMATIONRIGGING
                    if (EditorGUILayout.DropdownButton(VAW.UEditorGUI.GUIContents.GetTitleSettingsIcon(), FocusType.Passive, VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        var rigEnable = VAW.VA.AnimationRigging.IsValid;
                        GenericMenu menu = new();
                        menu.AddItem(Language.GetContent(Language.Help.AnimatorIKAnimationRiggingEnable), rigEnable, () =>
                        {
                            Undo.RecordObject(VAW, "Animation Rigging Enable");
                            VAW.VA.AnimationRigging.Enable();
                        });
                        menu.AddItem(Language.GetContent(Language.Help.AnimatorIKAnimationRiggingDisable), !rigEnable, () =>
                        {
                            Undo.RecordObject(VAW, "Animation Rigging Disable");
                            VAW.VA.AnimationRigging.Disable();
                        });
                        menu.ShowAsContext();
                    }
#endif
                }
                EditorGUILayout.EndHorizontal();
                {
                    if (guiAnimatorIkHelp)
                    {
                        EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpAnimatorIK), MessageType.Info);
                    }
                    VAW.VA.animatorIK.ControlGUI();
                }
            }
            #endregion

            #region OriginalIK
            if (guiOriginalIkFoldout && guiOriginalIkVisible)
            {
                EditorGUILayout.BeginHorizontal();
                guiOriginalIkFoldout = EditorGUILayout.Foldout(guiOriginalIkFoldout, "Original IK", true, VAW.GuiStyleBoldFoldout);
                {
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), guiOriginalIkHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        guiOriginalIkHelp = !guiOriginalIkHelp;
                    }
                }
                EditorGUILayout.EndHorizontal();
                {
                    if (guiOriginalIkHelp)
                    {
                        EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpOriginalIK), MessageType.Info);
                    }
                    VAW.VA.originalIK.ControlGUI();
                }
            }
            #endregion

            #region Humanoid
            OnGUI_Humanoid(e, ref repaint);
            #endregion

            #region Selection
            if (guiSelectionFoldout && guiSelectionVisible)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                guiSelectionFoldout = EditorGUILayout.Foldout(guiSelectionFoldout, "Selection", true, VAW.GuiStyleBoldFoldout);
                if (EditorGUI.EndChangeCheck())
                {
                    if (guiSelectionFoldout)
                        UpdateSelection();
                }
                EditorGUILayout.Space();
                {
                    EditorGUI.BeginChangeCheck();
                    var type = (SelectionType)GUILayout.Toolbar((int)selectionType, SelectionTypeString, EditorStyles.miniButton);
                    if (EditorGUI.EndChangeCheck())
                    {
                        selectionType = type;
                        EditorPrefs.SetInt(PrefKey_SelectionType, (int)selectionType);
                        switch (selectionType)
                        {
                            case SelectionType.List:
                                updateSelectionList = true;
                                break;
                            case SelectionType.Popup:
                                updateSelectionPopup = true;
                                break;
                        }
                    }
                }
                EditorGUILayout.Space();
                {
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), guiSelectionHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        guiSelectionHelp = !guiSelectionHelp;
                    }
                }
                EditorGUILayout.EndHorizontal();
                {
                    if (guiSelectionHelp)
                    {
                        EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpSelectionSet), MessageType.Info);
                    }
                    else
                    {
                        GUILayout.Space(2f);
                    }
                    SelectionGUI();
                }
            }
            #endregion

            #region Hierarchy
            if (guiHierarchyFoldout && guiHierarchyVisible)
            {
                EditorGUILayout.BeginHorizontal();
                guiHierarchyFoldout = EditorGUILayout.Foldout(guiHierarchyFoldout, "Hierarchy", true, VAW.GuiStyleBoldFoldout);
                {
                    EditorGUILayout.Space();
                    if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), guiHierarchyHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        guiHierarchyHelp = !guiHierarchyHelp;
                    }
                    if (EditorGUILayout.DropdownButton(VAW.UEditorGUI.GUIContents.GetTitleSettingsIcon(), FocusType.Passive, VAW.GuiStyleIconButton, GUILayout.Width(19)))
                    {
                        GenericMenu menu = new();
                        menu.AddItem(Language.GetContent(Language.Help.HierarchyWriteLock), hierarchyWriteLock, () =>
                        {
                            hierarchyWriteLock = !hierarchyWriteLock;
                            EditorPrefs.SetBool(PrefKey_HierarchyWriteLock, hierarchyWriteLock);
                            UpdateHierarchyTree();
                        });
                        menu.AddSeparator(string.Empty);
                        menu.AddItem(Language.GetContent(Language.Help.HierarchyMirrorObject), hierarchyMirrorObject, () =>
                        {
                            hierarchyMirrorObject = !hierarchyMirrorObject;
                            EditorPrefs.SetBool(PrefKey_HierarchyMirrorObject, hierarchyMirrorObject);
                        });
                        menu.AddItem(Language.GetContent(Language.Help.HierarchyMirrorAutomap), false, () =>
                        {
                            VAW.VA.BonesMirrorAutomap();
                        });
                        menu.AddItem(Language.GetContent(Language.Help.HierarchyMirrorClear), false, () =>
                        {
                            VAW.VA.BonesMirrorInitialize();
                        });
                        if (VAW.VA.IsHuman)
                        {
                            menu.AddSeparator(string.Empty);
                            menu.AddItem(Language.GetContent(Language.Help.HierarchyHumanoidName), hierarchyHumanoidName, () =>
                            {
                                hierarchyHumanoidName = !hierarchyHumanoidName;
                                EditorPrefs.SetBool(PrefKey_HierarchyHumanoidName, hierarchyHumanoidName);
                                UpdateHierarchyTree();
                            });
                        }
                        menu.ShowAsContext();
                    }
                }
                EditorGUILayout.EndHorizontal();
                {
                    if (guiHierarchyHelp)
                    {
                        EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpHierarchy), MessageType.Info);
                    }
                    else
                    {
                        GUILayout.Space(2f);
                    }
                    HierarchyToolBarGUI();
                    {
                        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                        hierarchyTreeView.searchString = hierarchyTreeSearchField.OnToolbarGUI(hierarchyTreeView.searchString);
                        EditorGUILayout.LabelField(new GUIContent($"{VAW.VA.BoneShowCount} / {VAW.VA.boneShowFlags.Length}", "Show / All"), VAW.GuiStyleMiddleRightMiniLabel, GUILayout.Width(60f));
                        EditorGUILayout.EndHorizontal();
                    }
                    {
                        var rect = EditorGUILayout.GetControlRect(false, 0);
                        rect.height = Math.Max(position.height - rect.y, 0);
                        hierarchyTreeView.OnGUI(rect);
                    }
                }
            }
            #endregion

            EditorGUILayout.EndScrollView();

            if (repaint)
            {
                Repaint();
            }

#if Enable_Profiler
            Profiler.EndSample();
#endif
        }

        private void OnGUI_Humanoid(Event e, ref bool repaint)
        {
            if (VAW.VA.IsHuman)
            {
                if (guiHumanoidFoldout && guiHumanoidVisible)
                {
                    if (e.type == EventType.Layout)
                    {
                        selectionGameObjectsHumanoidIndex.Clear();
                        if (VAW.VA.SelectionGameObjectsIndexOf(VAW.GameObject) >= 0)
                            selectionGameObjectsHumanoidIndex.Add((HumanBodyBones)(-1));
                        foreach (var hi in VAW.VA.SelectionGameObjectsHumanoidIndex())
                            selectionGameObjectsHumanoidIndex.Add(hi);
                    }
                    controlBoneList.Clear();
                    //
                    EditorGUILayout.BeginHorizontal();
                    guiHumanoidFoldout = EditorGUILayout.Foldout(guiHumanoidFoldout, "Humanoid", true, VAW.GuiStyleBoldFoldout);
                    {
                        EditorGUILayout.Space();
                        if (GUILayout.Button(VAW.UEditorGUI.GUIContents.GetHelpIcon(), guiHumanoidHelp ? VAW.GuiStyleIconActiveButton : VAW.GuiStyleIconButton, GUILayout.Width(19)))
                        {
                            guiHumanoidHelp = !guiHumanoidHelp;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    {
                        if (guiHumanoidHelp)
                        {
                            EditorGUILayout.HelpBox(Language.GetText(Language.Help.HelpHumanoid), MessageType.Info);
                        }

                        Rect backgroundRect;
                        {
                            backgroundRect = GUILayoutUtility.GetRect(avatarBodyPartPicker.width, float.MaxValue, avatarBodyPartPicker.height, avatarBodyPartPicker.height);
                            GUI.Box(backgroundRect, "", Styles.guiStyleBackgroundBox);
                        }

                        var saveGUIColor = GUI.color;
                        if (CurrentHumanoidAvatarPartsMode == HumanoidAvatarPartsMode.Body)
                        {
                            #region Body
                            #region Root
                            GUI.color = VAW.VA.SelectionGameObjectsIndexOf(VAW.GameObject) < 0 ? GreenColor : BlueColor;
                            GUI.DrawTexture(backgroundRect, avatarRoot, ScaleMode.ScaleToFit);
                            #endregion
                            #region BackGround
                            GUI.color = GrayColor;
                            GUI.DrawTexture(backgroundRect, avatarBodysilhouette, ScaleMode.ScaleToFit);
                            GUI.color = GreenColor;
                            GUI.DrawTexture(backgroundRect, avatarHead, ScaleMode.ScaleToFit);
                            GUI.DrawTexture(backgroundRect, avatarTorso, ScaleMode.ScaleToFit);
                            GUI.DrawTexture(backgroundRect, avatarLeftArm, ScaleMode.ScaleToFit);
                            GUI.DrawTexture(backgroundRect, avatarLeftFingers, ScaleMode.ScaleToFit);
                            GUI.DrawTexture(backgroundRect, avatarLeftLeg, ScaleMode.ScaleToFit);
                            GUI.DrawTexture(backgroundRect, avatarRightArm, ScaleMode.ScaleToFit);
                            GUI.DrawTexture(backgroundRect, avatarRightFingers, ScaleMode.ScaleToFit);
                            GUI.DrawTexture(backgroundRect, avatarRightLeg, ScaleMode.ScaleToFit);
                            #endregion
                            #region IK
                            {
                                Color GetIKTargetColor(AnimatorIKCore.IKTarget t)
                                {
                                    if (!VAW.VA.animatorIK.ikData[(int)t].enable)
                                        return GrayColor;
                                    else if (VAW.VA.animatorIK.ikTargetSelect != null && ArrayUtility.Contains(VAW.VA.animatorIK.ikTargetSelect, t))
                                        return BlueColor;
                                    else
                                        return GreenColor;
                                }
                                void IKTargetToggle(AnimatorIKCore.IKTarget t, Vector2 position)
                                {
                                    Rect rect = new(position, new Vector2(GUI.skin.toggle.border.horizontal, GUI.skin.toggle.border.vertical));
                                    GUI.color = Color.white;
                                    EditorGUI.BeginChangeCheck();
                                    EditorGUI.Toggle(rect, VAW.VA.animatorIK.ikData[(int)t].enable);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        VAW.VA.animatorIK.ChangeTargetIK(t);
                                    }
                                }
                                {
                                    GUI.color = GetIKTargetColor(AnimatorIKCore.IKTarget.LeftFoot);
                                    GUI.DrawTexture(backgroundRect, avatarLeftFeetIk, ScaleMode.ScaleToFit);
                                    IKTargetToggle(AnimatorIKCore.IKTarget.LeftFoot, new Vector2(backgroundRect.center.x + 86, backgroundRect.y + 355));
                                }
                                {
                                    GUI.color = GetIKTargetColor(AnimatorIKCore.IKTarget.RightFoot);
                                    GUI.DrawTexture(backgroundRect, avatarRightFeetIk, ScaleMode.ScaleToFit);
                                    IKTargetToggle(AnimatorIKCore.IKTarget.RightFoot, new Vector2(backgroundRect.center.x - 100, backgroundRect.y + 355));
                                }
                                {
                                    GUI.color = GetIKTargetColor(AnimatorIKCore.IKTarget.LeftHand);
                                    GUI.DrawTexture(backgroundRect, avatarLeftFingersIk, ScaleMode.ScaleToFit);
                                    IKTargetToggle(AnimatorIKCore.IKTarget.LeftHand, new Vector2(backgroundRect.center.x + 76, backgroundRect.y + 220));
                                }
                                {
                                    GUI.color = GetIKTargetColor(AnimatorIKCore.IKTarget.RightHand);
                                    GUI.DrawTexture(backgroundRect, avatarRightFingersIk, ScaleMode.ScaleToFit);
                                    IKTargetToggle(AnimatorIKCore.IKTarget.RightHand, new Vector2(backgroundRect.center.x - 90, backgroundRect.y + 220));
                                }
                                {
                                    GUI.color = GetIKTargetColor(AnimatorIKCore.IKTarget.Head);
                                    var rect = backgroundRect;
                                    rect.center -= new Vector2(-12f, 212f);
                                    GUI.DrawTexture(rect, avatarRightFingersIk, ScaleMode.ScaleToFit);
                                    IKTargetToggle(AnimatorIKCore.IKTarget.Head, new Vector2(backgroundRect.center.x - 79, backgroundRect.y + 8));
                                }
                            }
                            #endregion
                            #region Bone
                            {
                                var position = backgroundRect.center;
                                position.y = backgroundRect.y - 19;
                                //HumanoidControlBoneGUI(new Vector2(position.x, position.y + 191), HumanBodyBones.Hips);
                                HumanoidControlBoneGUI(new Vector2(position.x, position.y + 170), HumanBodyBones.Spine);
                                HumanoidControlBoneGUI(new Vector2(position.x, position.y + 140), HumanBodyBones.Chest);
                                HumanoidControlBoneGUI(new Vector2(position.x, position.y + 112), HumanBodyBones.UpperChest);
                                HumanoidControlBoneGUI(new Vector2(position.x, position.y + 82), HumanBodyBones.Neck);
                                HumanoidControlBoneGUI(new Vector2(position.x, position.y + 63), HumanBodyBones.Head);
                                HumanoidControlBoneGUI(new Vector2(position.x + 12, position.y + 93), HumanBodyBones.LeftShoulder);
                                HumanoidControlBoneGUI(new Vector2(position.x - 12, position.y + 93), HumanBodyBones.RightShoulder);
                                HumanoidControlBoneGUI(new Vector2(position.x + 27, position.y + 99), HumanBodyBones.LeftUpperArm);
                                HumanoidControlBoneGUI(new Vector2(position.x + 43, position.y + 150), HumanBodyBones.LeftLowerArm);
                                HumanoidControlBoneGUI(new Vector2(position.x + 59, position.y + 201), HumanBodyBones.LeftHand);
                                HumanoidControlBoneGUI(new Vector2(position.x - 27, position.y + 99), HumanBodyBones.RightUpperArm);
                                HumanoidControlBoneGUI(new Vector2(position.x - 43, position.y + 150), HumanBodyBones.RightLowerArm);
                                HumanoidControlBoneGUI(new Vector2(position.x - 59, position.y + 201), HumanBodyBones.RightHand);
                                HumanoidControlBoneGUI(new Vector2(position.x + 14, position.y + 205), HumanBodyBones.LeftUpperLeg);
                                HumanoidControlBoneGUI(new Vector2(position.x + 18, position.y + 282), HumanBodyBones.LeftLowerLeg);
                                HumanoidControlBoneGUI(new Vector2(position.x + 20, position.y + 358), HumanBodyBones.LeftFoot);
                                HumanoidControlBoneGUI(new Vector2(position.x - 14, position.y + 205), HumanBodyBones.RightUpperLeg);
                                HumanoidControlBoneGUI(new Vector2(position.x - 18, position.y + 282), HumanBodyBones.RightLowerLeg);
                                HumanoidControlBoneGUI(new Vector2(position.x - 20, position.y + 358), HumanBodyBones.RightFoot);
                                HumanoidControlBoneGUI(new Vector2(position.x + 23, position.y + 375), HumanBodyBones.LeftToes);
                                HumanoidControlBoneGUI(new Vector2(position.x - 23, position.y + 375), HumanBodyBones.RightToes);

                                controlBoneList.Add((HumanBodyBones)(-1), new Vector2(position.x, position.y + 372));   //Root
                            }
                            #endregion
                            #endregion
                        }
                        else if (CurrentHumanoidAvatarPartsMode == HumanoidAvatarPartsMode.Head)
                        {
                            #region Head
                            #region BackGround
                            GUI.color = GrayColor;
                            GUI.DrawTexture(backgroundRect, avatarHeadzoomsilhouette, ScaleMode.ScaleToFit);
                            //base
                            {
                                GUI.color = GreenColor;
                                GUI.DrawTexture(backgroundRect, avatarHeadZoom, ScaleMode.ScaleToFit);
                            }
                            #endregion
                            #region Bone
                            {
                                var position = backgroundRect.center;
                                position.y = backgroundRect.y - 19;
                                HumanoidControlBoneGUI(new Vector2(position.x - 14, position.y + 263), HumanBodyBones.Head);
                                HumanoidControlBoneGUI(new Vector2(position.x - 18, position.y + 324), HumanBodyBones.Neck);
                                HumanoidControlBoneGUI(new Vector2(position.x + 56, position.y + 176), HumanBodyBones.LeftEye);
                                HumanoidControlBoneGUI(new Vector2(position.x + 13, position.y + 176), HumanBodyBones.RightEye);
                                HumanoidControlBoneGUI(new Vector2(position.x + 40, position.y + 282), HumanBodyBones.Jaw);
                            }
                            #endregion
                            #endregion
                        }
                        else if (CurrentHumanoidAvatarPartsMode == HumanoidAvatarPartsMode.LeftHand)
                        {
                            #region LeftHand
                            #region BackGround
                            GUI.color = GrayColor;
                            GUI.DrawTexture(backgroundRect, avatarLefthandzoomsilhouette, ScaleMode.ScaleToFit);
                            //base
                            GUI.color = VAW.VA.HumanoidHasLeftHand ? GreenColor : GrayColor;
                            GUI.DrawTexture(backgroundRect, avatarLeftHandZoom, ScaleMode.ScaleToFit);
                            #endregion
                            #region Bone
                            {
                                var position = backgroundRect.center;
                                position.y = backgroundRect.y - 19;
                                HumanoidControlBoneGUI(new Vector2(position.x - 42, position.y + 186), HumanBodyBones.LeftThumbProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x - 20, position.y + 162), HumanBodyBones.LeftThumbIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x - 4, position.y + 144), HumanBodyBones.LeftThumbDistal);
                                HumanoidControlBoneGUI(new Vector2(position.x + 22, position.y + 186), HumanBodyBones.LeftIndexProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x + 54, position.y + 179), HumanBodyBones.LeftIndexIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x + 78, position.y + 175), HumanBodyBones.LeftIndexDistal);
                                HumanoidControlBoneGUI(new Vector2(position.x + 26, position.y + 207), HumanBodyBones.LeftMiddleProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x + 62, position.y + 207), HumanBodyBones.LeftMiddleIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x + 88, position.y + 207), HumanBodyBones.LeftMiddleDistal);
                                HumanoidControlBoneGUI(new Vector2(position.x + 19, position.y + 229), HumanBodyBones.LeftRingProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x + 54, position.y + 230), HumanBodyBones.LeftRingIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x + 79, position.y + 232), HumanBodyBones.LeftRingDistal);
                                HumanoidControlBoneGUI(new Vector2(position.x + 10, position.y + 250), HumanBodyBones.LeftLittleProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x + 35, position.y + 251), HumanBodyBones.LeftLittleIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x + 54, position.y + 253), HumanBodyBones.LeftLittleDistal);
                            }
                            #endregion
                            #endregion
                        }
                        else if (CurrentHumanoidAvatarPartsMode == HumanoidAvatarPartsMode.RightHand)
                        {
                            #region RightHand
                            #region BackGround
                            GUI.color = GrayColor;
                            GUI.DrawTexture(backgroundRect, avatarRighthandzoomsilhouette, ScaleMode.ScaleToFit);
                            //base
                            GUI.color = VAW.VA.HumanoidHasRightHand ? GreenColor : GrayColor;
                            GUI.DrawTexture(backgroundRect, avatarRightHandZoom, ScaleMode.ScaleToFit);
                            #endregion
                            #region Bone
                            {
                                var position = backgroundRect.center;
                                position.y = backgroundRect.y - 19;
                                HumanoidControlBoneGUI(new Vector2(position.x + 42, position.y + 186), HumanBodyBones.RightThumbProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x + 20, position.y + 162), HumanBodyBones.RightThumbIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x + 4, position.y + 144), HumanBodyBones.RightThumbDistal);
                                HumanoidControlBoneGUI(new Vector2(position.x - 22, position.y + 186), HumanBodyBones.RightIndexProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x - 54, position.y + 179), HumanBodyBones.RightIndexIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x - 78, position.y + 175), HumanBodyBones.RightIndexDistal);
                                HumanoidControlBoneGUI(new Vector2(position.x - 26, position.y + 207), HumanBodyBones.RightMiddleProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x - 62, position.y + 207), HumanBodyBones.RightMiddleIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x - 88, position.y + 207), HumanBodyBones.RightMiddleDistal);
                                HumanoidControlBoneGUI(new Vector2(position.x - 19, position.y + 229), HumanBodyBones.RightRingProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x - 54, position.y + 230), HumanBodyBones.RightRingIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x - 79, position.y + 232), HumanBodyBones.RightRingDistal);
                                HumanoidControlBoneGUI(new Vector2(position.x - 10, position.y + 250), HumanBodyBones.RightLittleProximal);
                                HumanoidControlBoneGUI(new Vector2(position.x - 35, position.y + 251), HumanBodyBones.RightLittleIntermediate);
                                HumanoidControlBoneGUI(new Vector2(position.x - 54, position.y + 253), HumanBodyBones.RightLittleDistal);
                            }
                            #endregion
                            #endregion
                        }
                        GUI.color = saveGUIColor;

                        #region Toolbar
                        {
                            Rect rect = backgroundRect;
                            {
                                rect.position = new Vector2(backgroundRect.position.x + 5, backgroundRect.position.y + 308);
                                rect.width = 70;
                                rect.height = 64;
                            }
                            CurrentHumanoidAvatarPartsMode = (HumanoidAvatarPartsMode)GUI.SelectionGrid(rect, (int)CurrentHumanoidAvatarPartsMode, HumanoidAvatarPartsModeStrings, 1, Styles.guiStyleVerticalToolbar);
                        }
                        #endregion

                        #region Event
                        switch (e.type)
                        {
                            case EventType.MouseDown:
                                if (e.button == 0)
                                {
                                    selectionRect.Reset();
                                    selectionAvatarMaskBodyPart = (AvatarMaskBodyPart)(-1);
                                    if (GUIUtility.hotControl == 0 && backgroundRect.Contains(e.mousePosition))
                                    {
                                        var pos = e.mousePosition - backgroundRect.min;
                                        pos.x -= (backgroundRect.width - avatarBodyPartPicker.width) / 2f;
                                        if (CurrentHumanoidAvatarPartsMode == HumanoidAvatarPartsMode.Body &&
                                            pos.x >= 0f && pos.x < avatarBodyPartPicker.width &&
                                            pos.y >= 0f && pos.y < avatarBodyPartPicker.height)
                                        {
                                            var pixel = avatarBodyPartPicker.GetPixel((int)pos.x, avatarBodyPartPicker.height - (int)pos.y);
                                            selectionAvatarMaskBodyPart = (AvatarMaskBodyPart)ArrayUtility.IndexOf(maskBodyPartPicker, pixel);
                                            switch (selectionAvatarMaskBodyPart)
                                            {
                                                case AvatarMaskBodyPart.Root:
                                                    VAW.VA.SelectGameObjectPlusKey(VAW.GameObject);
                                                    break;
                                                case AvatarMaskBodyPart.LeftFootIK:
                                                    if (VAW.VA.animatorIK.ikData[(int)AnimatorIKCore.IKTarget.LeftFoot].enable)
                                                        VAW.VA.SelectAnimatorIKTargetPlusKey(AnimatorIKCore.IKTarget.LeftFoot);
                                                    break;
                                                case AvatarMaskBodyPart.RightFootIK:
                                                    if (VAW.VA.animatorIK.ikData[(int)AnimatorIKCore.IKTarget.RightFoot].enable)
                                                        VAW.VA.SelectAnimatorIKTargetPlusKey(AnimatorIKCore.IKTarget.RightFoot);
                                                    break;
                                                case AvatarMaskBodyPart.LeftHandIK:
                                                    if (VAW.VA.animatorIK.ikData[(int)AnimatorIKCore.IKTarget.LeftHand].enable)
                                                        VAW.VA.SelectAnimatorIKTargetPlusKey(AnimatorIKCore.IKTarget.LeftHand);
                                                    break;
                                                case AvatarMaskBodyPart.RightHandIK:
                                                    if (VAW.VA.animatorIK.ikData[(int)AnimatorIKCore.IKTarget.RightHand].enable)
                                                        VAW.VA.SelectAnimatorIKTargetPlusKey(AnimatorIKCore.IKTarget.RightHand);
                                                    break;
                                                case AvatarMaskBodyPart.LastBodyPart:
                                                    if (VAW.VA.animatorIK.ikData[(int)AnimatorIKCore.IKTarget.Head].enable)
                                                        VAW.VA.SelectAnimatorIKTargetPlusKey(AnimatorIKCore.IKTarget.Head);
                                                    break;
                                                default:
                                                    VAW.VA.SelectGameObject(null);
                                                    break;
                                            }
                                        }
                                        {
                                            selectionRect.SetStart(e.mousePosition);
                                            if (Shortcuts.IsKeyControl(e) || e.shift)
                                            {
                                                selectionRect.beforeSelection = selectionGameObjectsHumanoidIndex.ToArray();
                                            }
                                        }
                                        e.Use();
                                        repaint = true;
                                    }
                                }
                                break;
                            case EventType.MouseUp:
                                if (e.button == 0)
                                {
                                    if (backgroundRect.Contains(e.mousePosition))
                                    {
                                        if (selectionAvatarMaskBodyPart < 0 && (!selectionRect.Enable || selectionRect.Distance <= 0f) && selectionRect.beforeSelection == null)
                                        {
                                            VAW.VA.SelectGameObject(null);
                                        }
                                        selectionRect.Reset();
                                        selectionAvatarMaskBodyPart = (AvatarMaskBodyPart)(-1);
                                        repaint = true;
                                    }
                                    else if (selectionRect.Enable)
                                    {
                                        selectionRect.Reset();
                                        selectionAvatarMaskBodyPart = (AvatarMaskBodyPart)(-1);
                                        repaint = true;
                                    }
                                }
                                break;
                            case EventType.MouseDrag:
                                if (e.button == 0)
                                {
                                    if (selectionRect.Enable)
                                    {
                                        if (GUIUtility.hotControl == 0)
                                        {
                                            {
                                                var rect = position;
                                                rect.position -= rect.position;
                                                if (rect.Contains(e.mousePosition - windowScrollPosition))
                                                {
                                                    var pos = e.mousePosition;
                                                    pos.x = Mathf.Clamp(pos.x, backgroundRect.xMin, backgroundRect.xMax);
                                                    pos.y = Mathf.Clamp(pos.y, backgroundRect.yMin, backgroundRect.yMax);
                                                    selectionRect.SetEnd(pos);
                                                }
                                                else
                                                {
                                                    selectionRect.Reset();
                                                }
                                            }
                                            #region Selection
                                            if (selectionRect.Enable)
                                            {
                                                HashSet<HumanBodyBones> oldCalcSet = new(selectionRect.calcList);
                                                HashSet<HumanBodyBones> calcSet = new();
                                                selectionRect.calcList.Clear();
                                                var rect = selectionRect.Rect;
                                                foreach (var pair in controlBoneList)
                                                {
                                                    if (rect.Contains(pair.Value))
                                                        calcSet.Add(pair.Key);
                                                }
                                                if ((Shortcuts.IsKeyControl(e) || e.shift) && selectionRect.beforeSelection != null)
                                                {
                                                    if (e.shift)
                                                    {
                                                        foreach (var hi in selectionRect.beforeSelection)
                                                        {
                                                            calcSet.Add(hi);
                                                        }
                                                    }
                                                    else if (Shortcuts.IsKeyControl(e))
                                                    {
                                                        foreach (var hi in selectionRect.beforeSelection)
                                                        {
                                                            if (!controlBoneList.TryGetValue(hi, out var boneRect)) continue;
                                                            if (!rect.Contains(boneRect))
                                                            {
                                                                calcSet.Add(hi);
                                                            }
                                                            else
                                                            {
                                                                calcSet.Remove(hi);
                                                            }
                                                        }
                                                    }
                                                }

                                                foreach (var pair in controlBoneList)
                                                {
                                                    if (calcSet.Contains(pair.Key))
                                                        selectionRect.calcList.Add(pair.Key);
                                                }
                                                if (selectionRect.beforeSelection != null)
                                                {
                                                    HashSet<HumanBodyBones> addedSet = new(selectionRect.calcList);
                                                    foreach (var hi in selectionRect.beforeSelection)
                                                    {
                                                        if (calcSet.Contains(hi) && addedSet.Add(hi))
                                                            selectionRect.calcList.Add(hi);
                                                    }
                                                }

                                                bool selectionChange = !oldCalcSet.SetEquals(calcSet);
                                                if (selectionChange)
                                                {
                                                    VAW.VA.SelectHumanoidBones(selectionRect.calcList);
                                                    ForceSelectionChange();
                                                }
                                            }
                                            #endregion
                                            e.Use();
                                        }
                                        else
                                        {
                                            selectionRect.Reset();
                                        }
                                    }
                                    repaint = true;
                                }
                                break;
                        }

                        #region SelectionRect
                        if (selectionRect.Enable && selectionRect.Rect.width > 0f && selectionRect.Rect.height > 0f)
                        {
                            GUI.Box(selectionRect.Rect, "", "SelectionRect");
                        }
                        #endregion
                        #endregion
                    }
                }
            }
        }


        private void SelectionGUI()
        {
            var e = Event.current;

            EditorGUILayout.BeginVertical(VAW.GuiStyleSkinBox);
            {
                if (selectionType == SelectionType.List)
                {
                    #region List
                    if (Event.current.type == EventType.Layout && updateSelectionList)
                    {
                        #region SelectionSet
                        selectionSetList = null;
                        if (VAW.VA.selectionSetList != null)
                        {
                            selectionSetList = new ReorderableList(VAW.VA.selectionSetList, typeof(VeryAnimationSaveSettings.SelectionData), draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);
                            selectionSetList.drawHeaderCallback = rect =>
                            {
                                float x = rect.x;
                                {
                                    const float ButtonWidth = 100f;
                                    #region Add
                                    {
                                        var r = rect;
                                        r.width = ButtonWidth;
                                        if (GUI.Button(r, Language.GetContent(Language.Help.SelectionSetTemplate), EditorStyles.toolbarDropDown))
                                        {
                                            var selectionTemplates = EditorCommon.CollectAssetPaths("t:selectiontemplate");

                                            var menu = new GenericMenu();
                                            {
                                                foreach (var kv in selectionTemplates)
                                                {
                                                    var value = kv.Value;
                                                    menu.AddItem(new GUIContent(kv.Key), false, () =>
                                                    {
                                                        var selectionTemplate = AssetDatabase.LoadAssetAtPath<SelectionTemplate>(value);
                                                        if (selectionTemplate != null)
                                                        {
                                                            Undo.RecordObject(VAW, "Template Selection");
                                                            VAW.VA.selectionSetList.AddRange(VAW.VA.LoadSelectionSaveSettings(selectionTemplate.selectionData));
                                                            updateSelectionList = true;
                                                        }
                                                    });
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
                                            Undo.RecordObject(VAW, "Clear Selection Set");
                                            VAW.VA.selectionSetList.Clear();
                                            selectionSetList.index = -1;
                                            updateSelectionList = true;
                                        }
                                    }
                                    #endregion
                                    #region Save as
                                    {
                                        var r = rect;
                                        r.width = ButtonWidth;
                                        r.x = rect.xMax - r.width;
                                        if (GUI.Button(r, Language.GetContent(Language.Help.SelectionSetSaveAs), EditorStyles.toolbarButton))
                                        {
                                            string path = EditorCommon.SaveFilePanelInAssets("Save as Selection Template", VAE.TemplateSaveDefaultDirectory, $"{VAW.GameObject.name}_Selection.asset", "asset");
                                            if (path != null)
                                            {
                                                VAE.TemplateSaveDefaultDirectory = Path.GetDirectoryName(path);
                                                {
                                                    var selectionTemplate = ScriptableObject.CreateInstance<SelectionTemplate>();
                                                    {
                                                        selectionTemplate.selectionData = VAW.VA.SaveSelectionSaveSettings();
                                                    }
                                                    using (new VeryAnimationWindow.CustomAssetModificationProcessor.PauseScope())
                                                    {
                                                        AssetDatabase.CreateAsset(selectionTemplate, path);
                                                    }
                                                    Focus();
                                                }
                                            }
                                        }
                                    }
                                    #endregion
                                }
                            };
                            selectionSetList.drawElementCallback = (rect, index, isActive, isFocused) =>
                            {
                                if (index >= VAW.VA.selectionSetList.Count)
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
                                    if (index == selectionSetList.index)
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        var text = EditorGUI.TextField(r, VAW.VA.selectionSetList[index].name);
                                        if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(text))
                                        {
                                            Undo.RecordObject(VAW, "Change Selection Set");
                                            VAW.VA.selectionSetList[index].name = text;
                                        }
                                    }
                                    else
                                    {
                                        EditorGUI.LabelField(r, VAW.VA.selectionSetList[index].name);
                                    }
                                }
                                {
                                    const float Rate = 0.2f;
                                    var r = rect;
                                    r.width = rect.width * Rate;
                                    r.x = rect.xMax - r.width;
                                    r.y += 2;
                                    r.height -= 4;
                                    EditorGUI.LabelField(r, VAW.VA.selectionSetList[index].Count.ToString(), VAW.GuiStyleCenterAlignLabel);
                                }
                            };
                            selectionSetList.onSelectCallback = list =>
                            {
                                if (list.index < 0 || list.index >= VAW.VA.selectionSetList.Count)
                                    return;
                                if (VAW.VA.selectionSetList[list.index].bones.Length > 0)
                                    Selection.activeGameObject = VAW.VA.selectionSetList[list.index].bones[0];
                                VAW.VA.SelectGameObjects(VAW.VA.selectionSetList[list.index].bones, VAW.VA.selectionSetList[list.index].virtualBones);
                            };
                            selectionSetList.onCanAddCallback = list =>
                            {
                                return (VAW.VA.SelectionGameObjects != null && VAW.VA.SelectionGameObjects.Count > 0) || (VAW.VA.SelectionHumanVirtualBones != null && VAW.VA.SelectionHumanVirtualBones.Count > 0);
                            };
                            selectionSetList.onAddCallback = list =>
                            {
                                if ((VAW.VA.SelectionGameObjects == null || VAW.VA.SelectionGameObjects.Count <= 0) && (VAW.VA.SelectionHumanVirtualBones == null || VAW.VA.SelectionHumanVirtualBones.Count <= 0))
                                    return;

                                Undo.RecordObject(VAW, "Add Selection Set");
                                {
                                    var data = new VeryAnimationSaveSettings.SelectionData()
                                    {
                                        name = "New Set",
                                        bones = VAW.VA.SelectionGameObjects.ToArray(),
                                        virtualBones = VAW.VA.SelectionHumanVirtualBones?.ToArray() ?? Array.Empty<HumanBodyBones>(),
                                    };
                                    if (VAW.VA.SelectionActiveGameObject != null)
                                        data.name = VAW.VA.SelectionActiveGameObject.name;
                                    else if (VAW.VA.SelectionHumanVirtualBones != null && VAW.VA.SelectionHumanVirtualBones.Count > 0)
                                        data.name = $"Virtual{VAW.VA.SelectionHumanVirtualBones[0]}";
                                    VAW.VA.selectionSetList.Add(data);
                                }
                                updateSelectionList = true;
                            };
                            selectionSetList.onRemoveCallback = list =>
                            {
                                if (list.index < 0 || list.index >= VAW.VA.selectionSetList.Count)
                                    return;
                                Undo.RecordObject(VAW, "Remove Selection Set");
                                VAW.VA.selectionSetList.RemoveAt(list.index);
                                list.index = -1;
                                updateSelectionList = true;
                            };
                        }
                        #endregion
                        updateSelectionList = false;
                        UpdateSelection();
                        Repaint();
                    }
                    selectionSetList?.DoLayoutList();
                    #endregion
                }
                else if (selectionType == SelectionType.Popup)
                {
                    #region Popup
                    if (Event.current.type == EventType.Layout && updateSelectionPopup)
                    {
                        selectionSetStrings = new string[VAW.VA.selectionSetList.Count];
                        for (int i = 0; i < VAW.VA.selectionSetList.Count; i++)
                        {
                            selectionSetStrings[i] = VAW.VA.selectionSetList[i].name;
                        }
                        updateSelectionPopup = false;
                        UpdateSelection();
                        Repaint();
                    }
                    if (selectionSetStrings != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        selectionSetIndex = EditorGUILayout.Popup("Selection Set", selectionSetIndex, selectionSetStrings);
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (selectionSetIndex >= 0 && selectionSetIndex < VAW.VA.selectionSetList.Count)
                            {
                                if (VAW.VA.selectionSetList[selectionSetIndex].bones.Length > 0)
                                    Selection.activeGameObject = VAW.VA.selectionSetList[selectionSetIndex].bones[0];
                                VAW.VA.SelectGameObjects(VAW.VA.selectionSetList[selectionSetIndex].bones, VAW.VA.selectionSetList[selectionSetIndex].virtualBones);
                            }
                        }
                    }
                    #endregion
                }

                #region Move select
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginDisabledGroup(VAW.VA.SelectionActiveBone < 0);
                    EditorGUILayout.PrefixLabel(Language.GetContent(Language.Help.MoveSelect));
                    if (GUILayout.Button("Upper"))
                    {
                        #region Upper
                        HashSet<GameObject> selectBones;
                        if (Shortcuts.IsKeyControl(e) || e.shift)
                            selectBones = new HashSet<GameObject>(VAW.VA.SelectionGameObjects);
                        else
                            selectBones = new HashSet<GameObject>();
                        foreach (var boneIndex in VAW.VA.SelectionBones)
                        {
                            if (VAW.VA.ParentBoneIndexes[boneIndex] >= 0)
                                selectBones.Add(VAW.VA.Bones[VAW.VA.ParentBoneIndexes[boneIndex]]);
                        }
                        if (VAW.VA.SelectionActiveGameObject != null)
                        {
                            GameObject activeGo = null;
                            if (selectBones.Contains(VAW.VA.SelectionActiveGameObject))
                            {
                                activeGo = VAW.VA.SelectionActiveGameObject;
                            }
                            else
                            {
                                var pt = VAW.VA.SelectionActiveGameObject.transform.parent;
                                if (pt != null && selectBones.Contains(pt.gameObject))
                                {
                                    activeGo = pt.gameObject;
                                }
                            }
                            if (activeGo != null)
                            {
                                Selection.activeGameObject = activeGo;
                            }
                        }
                        VAW.VA.SelectGameObjects(selectBones);
                        #endregion
                    }
                    if (GUILayout.Button("Lower"))
                    {
                        #region Lower
                        HashSet<GameObject> selectBones;
                        if (Shortcuts.IsKeyControl(e) || e.shift)
                            selectBones = new HashSet<GameObject>(VAW.VA.SelectionGameObjects);
                        else
                            selectBones = new HashSet<GameObject>();
                        foreach (var boneIndex in VAW.VA.SelectionBones)
                        {
                            for (int i = 0; i < VAW.VA.Bones.Length; i++)
                            {
                                if (boneIndex == VAW.VA.ParentBoneIndexes[i])
                                    selectBones.Add(VAW.VA.Bones[i]);
                            }
                        }
                        if (VAW.VA.SelectionActiveGameObject != null)
                        {
                            GameObject activeGo = null;
                            if (selectBones.Contains(VAW.VA.SelectionActiveGameObject))
                            {
                                activeGo = VAW.VA.SelectionActiveGameObject;
                            }
                            else
                            {
                                for (int i = 0; i < VAW.VA.SelectionActiveGameObject.transform.childCount; i++)
                                {
                                    var ct = VAW.VA.SelectionActiveGameObject.transform.GetChild(i);
                                    if (selectBones.Contains(ct.gameObject))
                                    {
                                        activeGo = ct.gameObject;
                                        break;
                                    }
                                }
                            }
                            if (activeGo != null)
                            {
                                Selection.activeGameObject = activeGo;
                            }
                        }
                        VAW.VA.SelectGameObjects(selectBones);
                        #endregion
                    }
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndHorizontal();
                }
                #endregion
            }
            EditorGUILayout.EndVertical();
        }
        private void UpdateSelection()
        {
            if (VAW == null || VAW.VA == null) return;

            selectionSetIndex = -1;
            if (VAW.VA.selectionSetList != null)
            {
                for (int i = 0; i < VAW.VA.selectionSetList.Count; i++)
                {
                    #region Bone
                    {
                        if ((VAW.VA.selectionSetList[i].bones?.Length ?? 0) != (VAW.VA.SelectionGameObjects?.Count ?? 0))
                            continue;
                        if (VAW.VA.selectionSetList[i].bones != null && VAW.VA.selectionSetList[i].bones.Length > 0)
                        {
                            if (VAW.VA.selectionSetList[i].bones[0] != VAW.VA.SelectionActiveGameObject)
                                continue;
                        }
                        if (VAW.VA.SelectionGameObjects != null)
                        {
                            bool contain = true;
                            foreach (var bone in VAW.VA.SelectionGameObjects)
                            {
                                if (!ArrayUtility.Contains(VAW.VA.selectionSetList[i].bones, bone))
                                {
                                    contain = false;
                                    break;
                                }
                            }
                            if (!contain) continue;
                        }
                    }
                    #endregion
                    #region VirtualBone
                    {
                        if ((VAW.VA.selectionSetList[i].virtualBones?.Length ?? 0) != (VAW.VA.SelectionHumanVirtualBones?.Count ?? 0))
                            continue;
                        if (VAW.VA.SelectionHumanVirtualBones != null)
                        {
                            bool contain = true;
                            foreach (var bone in VAW.VA.SelectionHumanVirtualBones)
                            {
                                if (!ArrayUtility.Contains(VAW.VA.selectionSetList[i].virtualBones, bone))
                                {
                                    contain = false;
                                    break;
                                }
                            }
                            if (!contain) continue;
                        }
                    }
                    #endregion
                    selectionSetIndex = i;
                    break;
                }
            }
            if (selectionSetList != null)
                selectionSetList.index = selectionSetIndex;
            Repaint();
        }

        private void HierarchyToolBarGUI()
        {
            if (VAW == null || VAW.VA == null || VAW.VA.IsEditError) return;

            void ShowFlagsToggle(bool buttonFlag, Language.Help help, Action<Action<int>> actionBoneShowFlags)
            {
                EditorGUI.BeginChangeCheck();
                var flag = GUILayout.Toggle(buttonFlag, Language.GetContent(help), EditorStyles.toolbarButton);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(VAW, UndoChangeBoneShowFlag);
                    actionBoneShowFlags((index) =>
                    {
                        VAW.VA.boneShowFlags[index] = flag;
                    });
                    VAW.VA.OnBoneShowFlagsUpdated.Invoke();
                }
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                ShowFlagsToggle(hierarchyButtonAll, Language.Help.HierarchyToolbarAll, VAW.VA.ActionBoneShowFlagsAll);
                ShowFlagsToggle(hierarchyButtonWeight, Language.Help.HierarchyToolbarWeight, VAW.VA.ActionBoneShowFlagsHaveWeight);
                if (VAW.VA.IsHuman)
                {
                    ShowFlagsToggle(hierarchyButtonBody, Language.Help.HierarchyToolbarBody, VAW.VA.ActionBoneShowFlagsHumanoidBody);
                    EditorGUI.BeginDisabledGroup(VAW.VA.Skeleton.HumanoidBones[(int)HumanBodyBones.LeftEye] == null &&
                                                    VAW.VA.Skeleton.HumanoidBones[(int)HumanBodyBones.RightEye] == null &&
                                                    VAW.VA.Skeleton.HumanoidBones[(int)HumanBodyBones.Jaw] == null);
                    ShowFlagsToggle(hierarchyButtonFace, Language.Help.HierarchyToolbarFace, VAW.VA.ActionBoneShowFlagsHumanoidFace);
                    EditorGUI.EndDisabledGroup();
                    EditorGUI.BeginDisabledGroup(!VAW.VA.HumanoidHasLeftHand);
                    ShowFlagsToggle(hierarchyButtonLeftHand, Language.Help.HierarchyToolbarLeftHand, VAW.VA.ActionBoneShowFlagsHumanoidLeftHand);
                    EditorGUI.EndDisabledGroup();
                    EditorGUI.BeginDisabledGroup(!VAW.VA.HumanoidHasRightHand);
                    ShowFlagsToggle(hierarchyButtonRightHand, Language.Help.HierarchyToolbarRightHand, VAW.VA.ActionBoneShowFlagsHumanoidRightHand);
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    ShowFlagsToggle(hierarchyButtonRenderer, Language.Help.HierarchyToolbarRenderer, VAW.VA.ActionBoneShowFlagsHaveRenderer);
                    ShowFlagsToggle(hierarchyButtonRendererParent, Language.Help.HierarchyToolbarRendererParent, VAW.VA.ActionBoneShowFlagsHaveRendererParent);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        private void HumanoidControlBoneGUI(Vector2 position, HumanBodyBones select)
        {
            if (VAW.VA.IsIKBone(select))
                return;

            var bone = VAW.VA.HumanoidBones[(int)select];
            if (bone == null && VeryAnimation.HumanVirtualBones[(int)select] == null)
                return;

            var saveGUIColor = GUI.color;
            GUI.color = GreenColor;

            var selected = (selectionGameObjectsHumanoidIndex != null && selectionGameObjectsHumanoidIndex.Contains(select));

            Texture2D frameTex = bone != null ? dotframe : dotframedotted;
            Rect rect = new(new Vector2(position.x - frameTex.width / 2f, position.y - frameTex.height / 2f), new Vector2(frameTex.width, frameTex.height));

            Styles.guiStyleBoneButton.normal.background = frameTex;
            Styles.guiStyleBoneButton.normal.scaledBackgrounds = null;
            Styles.guiStyleBoneButton.active.background = frameTex;
            Styles.guiStyleBoneButton.active.scaledBackgrounds = null;
            if (GUI.Button(rect, dotfill, Styles.guiStyleBoneButton))
            {
                if (bone != null)
                    VAW.VA.SelectGameObjectPlusKey(bone);
                else
                    VAW.VA.SelectVirtualBonePlusKey(select);
                ForceSelectionChange();
            }

            if (selected)
            {
                GUI.color = BlueColor;
                GUI.DrawTexture(rect, dotselection, ScaleMode.ScaleToFit);
            }

            GUI.color = saveGUIColor;

            controlBoneList.Add(select, position);
        }

        private void UndoRedoPerformed()
        {
            if (VAW == null || VAW.VA == null || VAW.VA.IsEditError) return;

            updateSelectionList = true;
            updateSelectionPopup = true;
            UpdateHierarchyFlags();
            Repaint();
        }

        private void UpdateHierarchyFlags()
        {
            if (VAW == null || VAW.VA == null || VAW.VA.IsEditError) return;

            bool AllShown(Action<Action<int>> actionBoneShowFlags)
            {
                var result = true;
                actionBoneShowFlags((index) =>
                {
                    if (!VAW.VA.boneShowFlags[index])
                        result = false;
                });
                return result;
            }

            hierarchyButtonAll = AllShown(VAW.VA.ActionBoneShowFlagsAll);
            hierarchyButtonWeight = AllShown(VAW.VA.ActionBoneShowFlagsHaveWeight);
            if (VAW.VA.IsHuman)
            {
                hierarchyButtonBody = AllShown(VAW.VA.ActionBoneShowFlagsHumanoidBody);
                hierarchyButtonFace = AllShown(VAW.VA.ActionBoneShowFlagsHumanoidFace);
                hierarchyButtonLeftHand = AllShown(VAW.VA.ActionBoneShowFlagsHumanoidLeftHand);
                hierarchyButtonRightHand = AllShown(VAW.VA.ActionBoneShowFlagsHumanoidRightHand);
            }
            else
            {
                hierarchyButtonRenderer = AllShown(VAW.VA.ActionBoneShowFlagsHaveRenderer);
                hierarchyButtonRendererParent = AllShown(VAW.VA.ActionBoneShowFlagsHaveRendererParent);
            }
        }

        public static void ForceRepaint()
        {
            if (instance == null) return;
            instance.Repaint();
        }

        public static void ForceSelectionChange()
        {
            if (instance == null) return;
            if (instance.guiSelectionFoldout)
            {
                instance.UpdateSelection();
            }
            ForceRepaint();
        }
    }
}
