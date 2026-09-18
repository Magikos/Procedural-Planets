using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace VeryAnimation
{
    internal class EditorSettings
    {
        private VeryAnimationWindow VAW => VeryAnimationWindow.instance;
        private VeryAnimationEditorWindow VAE => VeryAnimationEditorWindow.instance;

        private static readonly Color DefaultIKTargetNormalColor = new(1f, 1f, 1f, 0.5f);
        private static readonly Color DefaultIKTargetActiveColor = new(1f, 0.92f, 0.016f, 0.5f);

        #region EditorPrefs Keys
        private const string PrefKey_LanguageType = "VeryAnimation_LanguageType";
        private const string PrefKey_ComponentSaveSettings = "VeryAnimation_ComponentSaveSettings";
        private const string PrefKey_BoneButtonSize = "VeryAnimation_BoneButtonSize";
        private const string PrefKey_BoneNormalColor = "VeryAnimation_BoneNormalColor";
        private const string PrefKey_BoneActiveColor = "VeryAnimation_BoneActiveColor";
        private const string PrefKey_BoneMuscleLimit = "VeryAnimation_BoneMuscleLimit";
        private const string PrefKey_SkeletonType = "VeryAnimation_SkeletonType";
        private const string PrefKey_SkeletonColor = "VeryAnimation_SkeletonColor";
        private const string PrefKey_SkeletonIKType = "VeryAnimation_SkeletonIKType";
        private const string PrefKey_SkeletonIKColor = "VeryAnimation_SkeletonIKColor";
        private const string PrefKey_RootMotionColor = "VeryAnimation_RootMotionColor";
        private const string PrefKey_IKTargetSize = "VeryAnimation_IKTargetSize";
        private const string PrefKey_IKTargetNormalColor = "VeryAnimation_IKTargetNormalColor";
        private const string PrefKey_IKTargetActiveColor = "VeryAnimation_IKTargetActiveColor";
        private const string PrefKey_EditorWindowStyle = "VeryAnimation_EditorWindowStyle";
        private const string PrefKey_EditorNameFieldWidth = "VeryAnimation_EditorNameFieldWidth";
        private const string PrefKey_HierarchyExpandSelectObject = "VeryAnimation_HierarchyExpandSelectObject";
        private const string PrefKey_PropertyStyle = "VeryAnimation_PropertyStyle";
        private const string PrefKey_AutorunFrameAll = "VeryAnimation_AutorunFrameAll";
        private const string PrefKey_GenericMirrorScale = "VeryAnimation_GenericMirrorScale";
        private const string PrefKey_GenericMirrorName = "VeryAnimation_GenericMirrorName";
        private const string PrefKey_GenericMirrorNameDifferentCharacters = "VeryAnimation_GenericMirrorNameDifferentCharacters";
        private const string PrefKey_GenericMirrorNameIgnoreCharacter = "VeryAnimation_GenericMirrorNameIgnoreCharacter";
        private const string PrefKey_GenericMirrorNameIgnoreCharacterString = "VeryAnimation_GenericMirrorNameIgnoreCharacterString";
        private const string PrefKey_BlendShapeMirrorName = "VeryAnimation_BlendShapeMirrorName";
        private const string PrefKey_BlendShapeMirrorNameDifferentCharacters = "VeryAnimation_BlendShapeMirrorNameDifferentCharacters";
        private const string PrefKey_ExtraOnionSkinMode = "VeryAnimation_ExtraOnionSkinMode";
        private const string PrefKey_ExtraOnionSkinFrameIncrement = "VeryAnimation_ExtraOnionSkinFrameIncrement";
        private const string PrefKey_ExtraOnionSkinNextCount = "VeryAnimation_ExtraOnionSkinNextCount";
        private const string PrefKey_ExtraOnionSkinNextColor = "VeryAnimation_ExtraOnionSkinNextColor";
        private const string PrefKey_ExtraOnionSkinNextMinAlpha = "VeryAnimation_ExtraOnionSkinNextMinAlpha";
        private const string PrefKey_ExtraOnionSkinPrevCount = "VeryAnimation_ExtraOnionSkinPrevCount";
        private const string PrefKey_ExtraOnionSkinPrevColor = "VeryAnimation_ExtraOnionSkinPrevColor";
        private const string PrefKey_ExtraOnionSkinPrevMinAlpha = "VeryAnimation_ExtraOnionSkinPrevMinAlpha";
        private const string PrefKey_ExtraRootTrailColor = "VeryAnimation_ExtraRootTrailColor";
        #endregion

        #region GUIStyles
        class GUIStyles
        {
            public readonly GUIContent guiContentIK;
            public readonly GUIContent guiContentExpandSelectObject;
            public readonly GUIContent guiContentDifferentCharacters;
            public readonly GUIContent guiContentColor;
            public readonly GUIContent[] propertyStyleString = new GUIContent[2];
            public readonly string[] skeletonTypeString =
            {
                nameof(SkeletonType.None),
                nameof(SkeletonType.Line),
                nameof(SkeletonType.Lines),
                nameof(SkeletonType.Mesh),
            };
            public readonly string[] editorWindowStyleString =
            {
                nameof(EditorWindowStyle.Floating),
                nameof(EditorWindowStyle.Docking),
            };
            public readonly string[] onionSkinModeStrings =
            {
                nameof(OnionSkinMode.Keyframes),
                nameof(OnionSkinMode.Frames),
            };

            public GUIStyles()
            {
                guiContentIK = new GUIContent("IK", "Foot IK and Animation Rigging");
                guiContentExpandSelectObject = new GUIContent("Expand select object");
                guiContentDifferentCharacters = new GUIContent("Characters", "Different Characters");
                guiContentColor = new GUIContent("Color", "Near Color + Far Alpha");
                UpdateLanguage();
                Language.OnLanguageChanged += UpdateLanguage;
            }
            private void UpdateLanguage()
            {
                propertyStyleString[0] = Language.GetContent(Language.Help.SettingsPropertyStyle_Default);
                propertyStyleString[1] = Language.GetContent(Language.Help.SettingsPropertyStyle_Filter);
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

        public Language.LanguageType SettingLanguageType { get; private set; }
        public bool SettingComponentSaveSettings { get; private set; }
        public float SettingBoneButtonSize { get; private set; }
        public Color SettingBoneNormalColor { get; private set; }
        public Color SettingBoneActiveColor { get; private set; }
        public bool SettingBoneMuscleLimit { get; private set; }
        public enum SkeletonType
        {
            None,
            Line,
            Lines,
            Mesh,
        }

        public SkeletonType SettingsSkeletonFKType { get; private set; }
        public Color SettingSkeletonFKColor { get; private set; }
        public SkeletonType SettingsSkeletonIKType { get; private set; }
        public Color SettingSkeletonIKColor { get; private set; }
        public Color SettingRootMotionColor { get; private set; }
        public float SettingIKTargetSize { get; private set; }
        public Color SettingIKTargetNormalColor { get; private set; }
        public Color SettingIKTargetActiveColor { get; private set; }
        public enum EditorWindowStyle
        {
            Floating,
            Docking,
        }
        public EditorWindowStyle SettingEditorWindowStyle { get; private set; }
        public float SettingEditorNameFieldWidth { get; private set; }
        public bool SettingHierarchyExpandSelectObject { get; private set; }
        public enum PropertyStyle
        {
            Default,
            Filter,
        }
        public PropertyStyle SettingPropertyStyle { get; private set; }
        public bool SettingAutorunFrameAll { get; private set; }
        public bool SettingGenericMirrorScale { get; private set; }
        public bool SettingGenericMirrorName { get; private set; }
        public string SettingGenericMirrorNameDifferentCharacters { get; private set; }
        public bool SettingGenericMirrorNameIgnoreCharacter { get; private set; }
        public string SettingGenericMirrorNameIgnoreCharacterString { get; private set; }
        public bool SettingBlendShapeMirrorName { get; private set; }
        public string SettingBlendShapeMirrorNameDifferentCharacters { get; private set; }
        public enum OnionSkinMode
        {
            Keyframes,
            Frames,
        }
        public OnionSkinMode SettingExtraOnionSkinMode { get; private set; }
        public int SettingExtraOnionSkinFrameIncrement { get; private set; }
        public int SettingExtraOnionSkinNextCount { get; private set; }
        public Color SettingExtraOnionSkinNextColor { get; private set; }
        private static readonly Color DefaultOnionSkinNextColor = new(0.6039216f, 0.9529412f, 0.282353f, 0.5f);
        public float SettingExtraOnionSkinNextMinAlpha { get; private set; }
        private const float DefaultOnionSkinNextMinAlpha = 0.15f;
        public int SettingExtraOnionSkinPrevCount { get; private set; }
        public Color SettingExtraOnionSkinPrevColor { get; private set; }
        private static readonly Color DefaultOnionSkinPrevColor = new(0.8588235f, 0.2431373f, 0.1137255f, 0.5f);
        public float SettingExtraOnionSkinPrevMinAlpha { get; private set; }
        private const float DefaultOnionSkinPrevMinAlpha = 0.15f;
        public Color SettingExtraRootTrailColor { get; private set; }
        private static readonly Color DefaultRootTrailColor = new(1f, 0.5f, 0.5f, 0.5f);

        private bool componentFoldout;
        private bool gizmosFoldout;
        private bool gizmosBoneFoldout;
        private bool gizmosSkeletonFoldout;
        private bool gizmosIkFoldout;
        private bool editorWindowFoldout;
        private bool controlWindowFoldout;
        private bool controlWindowHierarchyFoldout;
        private bool animationWindowFoldout;
        private bool mirrorFoldout;
        private bool mirrorAutomapFoldout;
        private bool extraFoldout;
        private bool extraOnionSkinningFoldout;
        private bool extraRootTrailFoldout;

        #region RestartOnly
        private EditorWindowStyle settingEditorWindowStyleBefore;
        #endregion

        public EditorSettings()
        {
            SettingLanguageType = (Language.LanguageType)EditorPrefs.GetInt(PrefKey_LanguageType, 0);
            SettingComponentSaveSettings = EditorPrefs.GetBool(PrefKey_ComponentSaveSettings, true);
            SettingBoneButtonSize = EditorPrefs.GetFloat(PrefKey_BoneButtonSize, 16f);
            SettingBoneNormalColor = GetEditorPrefsColor(PrefKey_BoneNormalColor, Color.white);
            SettingBoneActiveColor = GetEditorPrefsColor(PrefKey_BoneActiveColor, Color.yellow);
            SettingBoneMuscleLimit = EditorPrefs.GetBool(PrefKey_BoneMuscleLimit, true);
            SettingsSkeletonFKType = (SkeletonType)EditorPrefs.GetInt(PrefKey_SkeletonType, (int)SkeletonType.Lines);
            SettingSkeletonFKColor = GetEditorPrefsColor(PrefKey_SkeletonColor, Color.green);
            SettingsSkeletonIKType = (SkeletonType)EditorPrefs.GetInt(PrefKey_SkeletonIKType, (int)SkeletonType.Line);
            SettingSkeletonIKColor = GetEditorPrefsColor(PrefKey_SkeletonIKColor, Color.magenta);
            SettingRootMotionColor = GetEditorPrefsColor(PrefKey_RootMotionColor, Color.cyan);
            SettingIKTargetSize = EditorPrefs.GetFloat(PrefKey_IKTargetSize, 0.15f);
            SettingIKTargetNormalColor = GetEditorPrefsColor(PrefKey_IKTargetNormalColor, DefaultIKTargetNormalColor);
            SettingIKTargetActiveColor = GetEditorPrefsColor(PrefKey_IKTargetActiveColor, DefaultIKTargetActiveColor);
            SettingEditorWindowStyle = (EditorWindowStyle)EditorPrefs.GetInt(PrefKey_EditorWindowStyle, (int)EditorWindowStyle.Docking);
            SettingEditorNameFieldWidth = EditorPrefs.GetFloat(PrefKey_EditorNameFieldWidth, 180f);
            SettingHierarchyExpandSelectObject = EditorPrefs.GetBool(PrefKey_HierarchyExpandSelectObject, true);
            SettingPropertyStyle = (PropertyStyle)EditorPrefs.GetInt(PrefKey_PropertyStyle, 1);
            SettingAutorunFrameAll = EditorPrefs.GetBool(PrefKey_AutorunFrameAll, true);
            SettingGenericMirrorScale = EditorPrefs.GetBool(PrefKey_GenericMirrorScale, false);
            SettingGenericMirrorName = EditorPrefs.GetBool(PrefKey_GenericMirrorName, true);
            SettingGenericMirrorNameDifferentCharacters = EditorPrefs.GetString(PrefKey_GenericMirrorNameDifferentCharacters, "Left,Right,Hidari,Migi,L,R");
            SettingGenericMirrorNameIgnoreCharacter = EditorPrefs.GetBool(PrefKey_GenericMirrorNameIgnoreCharacter, false);
            SettingGenericMirrorNameIgnoreCharacterString = EditorPrefs.GetString(PrefKey_GenericMirrorNameIgnoreCharacterString, ".");
            SettingBlendShapeMirrorName = EditorPrefs.GetBool(PrefKey_BlendShapeMirrorName, true);
            SettingBlendShapeMirrorNameDifferentCharacters = EditorPrefs.GetString(PrefKey_BlendShapeMirrorNameDifferentCharacters, "Left,Right,Hidari,Migi,L,R");
            SettingExtraOnionSkinMode = (OnionSkinMode)EditorPrefs.GetInt(PrefKey_ExtraOnionSkinMode, 0);
            SettingExtraOnionSkinFrameIncrement = EditorPrefs.GetInt(PrefKey_ExtraOnionSkinFrameIncrement, 1);
            SettingExtraOnionSkinNextCount = EditorPrefs.GetInt(PrefKey_ExtraOnionSkinNextCount, 2);
            SettingExtraOnionSkinNextColor = GetEditorPrefsColor(PrefKey_ExtraOnionSkinNextColor, DefaultOnionSkinNextColor);
            SettingExtraOnionSkinNextMinAlpha = EditorPrefs.GetFloat(PrefKey_ExtraOnionSkinNextMinAlpha, DefaultOnionSkinNextMinAlpha);
            SettingExtraOnionSkinPrevCount = EditorPrefs.GetInt(PrefKey_ExtraOnionSkinPrevCount, 2);
            SettingExtraOnionSkinPrevColor = GetEditorPrefsColor(PrefKey_ExtraOnionSkinPrevColor, DefaultOnionSkinPrevColor);
            SettingExtraOnionSkinPrevMinAlpha = EditorPrefs.GetFloat(PrefKey_ExtraOnionSkinPrevMinAlpha, DefaultOnionSkinPrevMinAlpha);
            SettingExtraRootTrailColor = GetEditorPrefsColor(PrefKey_ExtraRootTrailColor, DefaultRootTrailColor);

            if (SettingPropertyStyle > PropertyStyle.Filter)
                SettingPropertyStyle = PropertyStyle.Filter;

            Language.SetLanguage(SettingLanguageType);
        }
        public void Reset()
        {
            EditorPrefs.SetInt(PrefKey_LanguageType, (int)(SettingLanguageType = (Language.LanguageType)0));
            EditorPrefs.SetBool(PrefKey_ComponentSaveSettings, SettingComponentSaveSettings = true);
            EditorPrefs.SetFloat(PrefKey_BoneButtonSize, SettingBoneButtonSize = 16f);
            SetEditorPrefsColor(PrefKey_BoneNormalColor, SettingBoneNormalColor = Color.white);
            SetEditorPrefsColor(PrefKey_BoneActiveColor, SettingBoneActiveColor = Color.yellow);
            EditorPrefs.SetBool(PrefKey_BoneMuscleLimit, SettingBoneMuscleLimit = true);
            EditorPrefs.SetInt(PrefKey_SkeletonType, (int)(SettingsSkeletonFKType = SkeletonType.Lines));
            SetEditorPrefsColor(PrefKey_SkeletonColor, SettingSkeletonFKColor = Color.green);
            EditorPrefs.SetInt(PrefKey_SkeletonIKType, (int)(SettingsSkeletonIKType = SkeletonType.Line));
            SetEditorPrefsColor(PrefKey_SkeletonIKColor, SettingSkeletonIKColor = Color.magenta);
            SetEditorPrefsColor(PrefKey_RootMotionColor, SettingRootMotionColor = Color.cyan);
            EditorPrefs.SetFloat(PrefKey_IKTargetSize, SettingIKTargetSize = 0.15f);
            SetEditorPrefsColor(PrefKey_IKTargetNormalColor, SettingIKTargetNormalColor = DefaultIKTargetNormalColor);
            SetEditorPrefsColor(PrefKey_IKTargetActiveColor, SettingIKTargetActiveColor = DefaultIKTargetActiveColor);
            EditorPrefs.SetInt(PrefKey_EditorWindowStyle, (int)(SettingEditorWindowStyle = EditorWindowStyle.Docking));
            EditorPrefs.SetFloat(PrefKey_EditorNameFieldWidth, SettingEditorNameFieldWidth = 180f);
            EditorPrefs.SetBool(PrefKey_HierarchyExpandSelectObject, SettingHierarchyExpandSelectObject = true);
            EditorPrefs.SetInt(PrefKey_PropertyStyle, (int)(SettingPropertyStyle = (PropertyStyle)1));
            EditorPrefs.SetBool(PrefKey_AutorunFrameAll, SettingAutorunFrameAll = true);
            EditorPrefs.SetBool(PrefKey_GenericMirrorScale, SettingGenericMirrorScale = false);
            EditorPrefs.SetBool(PrefKey_GenericMirrorName, SettingGenericMirrorName = true);
            EditorPrefs.SetString(PrefKey_GenericMirrorNameDifferentCharacters, SettingGenericMirrorNameDifferentCharacters = "Left,Right,Hidari,Migi,L,R");
            EditorPrefs.SetBool(PrefKey_GenericMirrorNameIgnoreCharacter, SettingGenericMirrorNameIgnoreCharacter = false);
            EditorPrefs.SetString(PrefKey_GenericMirrorNameIgnoreCharacterString, SettingGenericMirrorNameIgnoreCharacterString = ".");
            EditorPrefs.SetBool(PrefKey_BlendShapeMirrorName, SettingBlendShapeMirrorName = true);
            EditorPrefs.SetString(PrefKey_BlendShapeMirrorNameDifferentCharacters, SettingBlendShapeMirrorNameDifferentCharacters = "Left,Right,Hidari,Migi,L,R");
            EditorPrefs.SetInt(PrefKey_ExtraOnionSkinMode, (int)(SettingExtraOnionSkinMode = (OnionSkinMode)0));
            EditorPrefs.SetInt(PrefKey_ExtraOnionSkinFrameIncrement, SettingExtraOnionSkinFrameIncrement = 1);
            EditorPrefs.SetInt(PrefKey_ExtraOnionSkinNextCount, SettingExtraOnionSkinNextCount = 2);
            SetEditorPrefsColor(PrefKey_ExtraOnionSkinNextColor, SettingExtraOnionSkinNextColor = DefaultOnionSkinNextColor);
            EditorPrefs.SetFloat(PrefKey_ExtraOnionSkinNextMinAlpha, SettingExtraOnionSkinNextMinAlpha = DefaultOnionSkinNextMinAlpha);
            EditorPrefs.SetInt(PrefKey_ExtraOnionSkinPrevCount, SettingExtraOnionSkinPrevCount = 2);
            SetEditorPrefsColor(PrefKey_ExtraOnionSkinPrevColor, SettingExtraOnionSkinPrevColor = DefaultOnionSkinPrevColor);
            EditorPrefs.SetFloat(PrefKey_ExtraOnionSkinPrevMinAlpha, SettingExtraOnionSkinPrevMinAlpha = DefaultOnionSkinPrevMinAlpha);
            SetEditorPrefsColor(PrefKey_ExtraRootTrailColor, SettingExtraRootTrailColor = DefaultRootTrailColor);

            Language.SetLanguage(SettingLanguageType);
            VAW.VA.SetUpdateSampleAnimation();
            VAW.VA.SetAnimationWindowSynchroSelection();
            SceneView.RepaintAll();
        }

        public static void SetGlobalSetting()
        {
            var SettingLanguageType = (Language.LanguageType)EditorPrefs.GetInt(PrefKey_LanguageType, 0);
            Language.SetLanguage(SettingLanguageType);
        }

        public void Initialize()
        {
            Release();

            #region RestartOnly
            settingEditorWindowStyleBefore = SettingEditorWindowStyle;
            #endregion
        }
        public void Release()
        {
        }

        public void SettingsGUI()
        {
            EditorGUILayout.BeginVertical(VAW.GuiStyleSkinBox);
            {
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel("Language");
                    EditorGUI.BeginChangeCheck();
                    SettingLanguageType = (Language.LanguageType)GUILayout.Toolbar((int)SettingLanguageType, Language.LanguageTypeString, EditorStyles.miniButton);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorPrefs.SetInt(PrefKey_LanguageType, (int)(SettingLanguageType));
                        Language.SetLanguage(SettingLanguageType);
                        VAW.Repaint();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                componentFoldout = EditorGUILayout.Foldout(componentFoldout, "Component", true);
                if (componentFoldout)
                {
                    EditorGUI.indentLevel++;
                    {
                        #region settingComponentSaveSettings
                        {
                            EditorGUI.BeginChangeCheck();
                            SettingComponentSaveSettings = EditorGUILayout.Toggle(Language.GetContent(Language.Help.SettingsSaveSettings), SettingComponentSaveSettings);
                            if (EditorGUI.EndChangeCheck())
                            {
                                EditorPrefs.SetBool(PrefKey_ComponentSaveSettings, SettingComponentSaveSettings);
                            }
                        }
                        #endregion
                    }
                    EditorGUI.indentLevel--;
                }
                gizmosFoldout = EditorGUILayout.Foldout(gizmosFoldout, "Gizmos", true);
                if (gizmosFoldout)
                {
                    EditorGUI.indentLevel++;
                    {
                        gizmosBoneFoldout = EditorGUILayout.Foldout(gizmosBoneFoldout, "Bone", true);
                        if (gizmosBoneFoldout)
                        {
                            EditorGUI.indentLevel++;
                            {
                                #region Button Size
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingBoneButtonSize = EditorGUILayout.Slider("Button Size", SettingBoneButtonSize, 1f, 32f);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        EditorPrefs.SetFloat(PrefKey_BoneButtonSize, SettingBoneButtonSize);
                                        SceneView.RepaintAll();
                                    }
                                }
                                #endregion
                                #region Button Normal Color
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingBoneNormalColor = EditorGUILayout.ColorField("Button Normal Color", SettingBoneNormalColor);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        SetEditorPrefsColor(PrefKey_BoneNormalColor, SettingBoneNormalColor);
                                        SceneView.RepaintAll();
                                    }
                                }
                                #endregion
                                #region Button Active Color
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingBoneActiveColor = EditorGUILayout.ColorField("Button Active Color", SettingBoneActiveColor);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        SetEditorPrefsColor(PrefKey_BoneActiveColor, SettingBoneActiveColor);
                                        SceneView.RepaintAll();
                                    }
                                }
                                #endregion
                                #region MuscleLimit
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingBoneMuscleLimit = EditorGUILayout.Toggle("Muscle Limit Gizmo", SettingBoneMuscleLimit);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        EditorPrefs.SetBool(PrefKey_BoneMuscleLimit, SettingBoneMuscleLimit);
                                        SceneView.RepaintAll();
                                    }
                                }
                                #endregion
                            }
                            EditorGUI.indentLevel--;
                        }
                        gizmosSkeletonFoldout = EditorGUILayout.Foldout(gizmosSkeletonFoldout, "Skeleton", true);
                        if (gizmosSkeletonFoldout)
                        {
                            EditorGUI.indentLevel++;
                            {
                                #region FK
                                EditorGUILayout.LabelField("FK");
                                {
                                    EditorGUI.indentLevel++;
                                    #region SkeletonType
                                    {
                                        EditorGUILayout.BeginHorizontal();
                                        EditorGUI.BeginChangeCheck();
                                        EditorGUILayout.PrefixLabel("Preview Type");
                                        SettingsSkeletonFKType = (SkeletonType)GUILayout.Toolbar((int)SettingsSkeletonFKType, Styles.skeletonTypeString, EditorStyles.miniButton);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            EditorPrefs.SetInt(PrefKey_SkeletonType, (int)SettingsSkeletonFKType);
                                            SceneView.RepaintAll();
                                        }
                                        EditorGUILayout.EndHorizontal();
                                    }
                                    #endregion
                                    #region Skeleton Color
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        SettingSkeletonFKColor = EditorGUILayout.ColorField("Preview Color", SettingSkeletonFKColor);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            SetEditorPrefsColor(PrefKey_SkeletonColor, SettingSkeletonFKColor);
                                            SceneView.RepaintAll();
                                        }
                                    }
                                    #endregion
                                    EditorGUI.indentLevel--;
                                }
                                #endregion
                                #region IK
                                EditorGUILayout.LabelField(Styles.guiContentIK);
                                {
                                    EditorGUI.indentLevel++;
                                    #region SkeletonType
                                    {
                                        EditorGUILayout.BeginHorizontal();
                                        EditorGUI.BeginChangeCheck();
                                        EditorGUILayout.PrefixLabel("Preview Type");
                                        SettingsSkeletonIKType = (SkeletonType)GUILayout.Toolbar((int)SettingsSkeletonIKType, Styles.skeletonTypeString, EditorStyles.miniButton);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            EditorPrefs.SetInt(PrefKey_SkeletonIKType, (int)SettingsSkeletonIKType);
                                            SceneView.RepaintAll();
                                        }
                                        EditorGUILayout.EndHorizontal();
                                    }
                                    #endregion
                                    #region Skeleton Color
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        SettingSkeletonIKColor = EditorGUILayout.ColorField("Preview Color", SettingSkeletonIKColor);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            SetEditorPrefsColor(PrefKey_SkeletonIKColor, SettingSkeletonIKColor);
                                            SceneView.RepaintAll();
                                        }
                                    }
                                    #endregion
                                    EditorGUI.indentLevel--;
                                }
                                #endregion
                                #region RootMotion Color
                                EditorGUILayout.LabelField("Root Motion");
                                {
                                    EditorGUI.indentLevel++;
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        SettingRootMotionColor = EditorGUILayout.ColorField("Preview Color", SettingRootMotionColor);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            SetEditorPrefsColor(PrefKey_RootMotionColor, SettingRootMotionColor);
                                            SceneView.RepaintAll();
                                        }
                                    }
                                    EditorGUI.indentLevel--;
                                }
                                #endregion
                            }
                            EditorGUI.indentLevel--;
                        }
                        gizmosIkFoldout = EditorGUILayout.Foldout(gizmosIkFoldout, "IK", true);
                        if (gizmosIkFoldout)
                        {
                            EditorGUI.indentLevel++;
                            {
                                #region IK Target Size
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingIKTargetSize = EditorGUILayout.Slider("Button Size", SettingIKTargetSize, 0.01f, 1f);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        EditorPrefs.SetFloat(PrefKey_IKTargetSize, SettingIKTargetSize);
                                        SceneView.RepaintAll();
                                    }
                                }
                                #endregion
                                #region IK Target Normal Color
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingIKTargetNormalColor = EditorGUILayout.ColorField("Button Normal Color", SettingIKTargetNormalColor);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        SetEditorPrefsColor(PrefKey_IKTargetNormalColor, SettingIKTargetNormalColor);
                                        SceneView.RepaintAll();
                                    }
                                }
                                #endregion
                                #region IK Target Active Color
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingIKTargetActiveColor = EditorGUILayout.ColorField("Button Active Color", SettingIKTargetActiveColor);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        SetEditorPrefsColor(PrefKey_IKTargetActiveColor, SettingIKTargetActiveColor);
                                        SceneView.RepaintAll();
                                    }
                                }
                                #endregion
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                    EditorGUI.indentLevel--;
                }
                editorWindowFoldout = EditorGUILayout.Foldout(editorWindowFoldout, "Editor Window", true);
                if (editorWindowFoldout)
                {
                    EditorGUI.indentLevel++;
                    {
                        #region Window Style
                        EditorGUILayout.BeginHorizontal();
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PrefixLabel("Window Style");
                        SettingEditorWindowStyle = (EditorWindowStyle)GUILayout.Toolbar((int)SettingEditorWindowStyle, Styles.editorWindowStyleString, EditorStyles.miniButton);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorPrefs.SetInt(PrefKey_EditorWindowStyle, (int)SettingEditorWindowStyle);
                        }
                        EditorGUILayout.EndHorizontal();
                        #endregion
                    }
                    {
                        #region NameFieldWidth
                        {
                            EditorGUI.BeginChangeCheck();
                            SettingEditorNameFieldWidth = EditorGUILayout.Slider("Name Field Width", SettingEditorNameFieldWidth, 50f, 500f);
                            if (EditorGUI.EndChangeCheck())
                            {
                                EditorPrefs.SetFloat(PrefKey_EditorNameFieldWidth, SettingEditorNameFieldWidth);
                                VAE.Repaint();
                            }
                        }
                        #endregion
                    }
                    EditorGUI.indentLevel--;
                }
                controlWindowFoldout = EditorGUILayout.Foldout(controlWindowFoldout, "Control Window", true);
                if (controlWindowFoldout)
                {
                    EditorGUI.indentLevel++;
                    {
                        controlWindowHierarchyFoldout = EditorGUILayout.Foldout(controlWindowHierarchyFoldout, "Hierarchy", true);
                        if (controlWindowHierarchyFoldout)
                        {
                            EditorGUI.indentLevel++;
                            {
                                #region ExpandSelectObject
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingHierarchyExpandSelectObject = EditorGUILayout.Toggle(Styles.guiContentExpandSelectObject, SettingHierarchyExpandSelectObject);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        EditorPrefs.SetBool(PrefKey_HierarchyExpandSelectObject, SettingHierarchyExpandSelectObject);
                                    }
                                }
                                #endregion
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                    EditorGUI.indentLevel--;
                }
                animationWindowFoldout = EditorGUILayout.Foldout(animationWindowFoldout, "Animation Window", true);
                if (animationWindowFoldout)
                {
                    EditorGUI.indentLevel++;
                    {
                        #region Property Style
                        EditorGUILayout.BeginHorizontal();
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PrefixLabel("Property Style");
                        SettingPropertyStyle = (PropertyStyle)GUILayout.Toolbar((int)SettingPropertyStyle, Styles.propertyStyleString, EditorStyles.miniButton);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorPrefs.SetInt(PrefKey_PropertyStyle, (int)SettingPropertyStyle);
                            VAW.VA.SetAnimationWindowSynchroSelection();
                        }
                        EditorGUILayout.EndHorizontal();
                        #endregion
                    }
                    {
                        #region AutorunFrameAll
                        EditorGUI.BeginChangeCheck();
                        SettingAutorunFrameAll = EditorGUILayout.Toggle(Language.GetContent(Language.Help.SettingsAutorunFrameAll), SettingAutorunFrameAll);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorPrefs.SetBool(PrefKey_AutorunFrameAll, SettingAutorunFrameAll);
                        }
                        #endregion
                    }
                    EditorGUI.indentLevel--;
                }
                mirrorFoldout = EditorGUILayout.Foldout(mirrorFoldout, "Mirror", true);
                if (mirrorFoldout)
                {
                    EditorGUI.indentLevel++;
                    {
                        EditorGUI.BeginChangeCheck();
                        SettingGenericMirrorScale = EditorGUILayout.Toggle(Language.GetContent(Language.Help.SettingsMirrorScale), SettingGenericMirrorScale);
                        if (EditorGUI.EndChangeCheck())
                        {
                            EditorPrefs.SetBool(PrefKey_GenericMirrorScale, SettingGenericMirrorScale);
                        }
                    }

                    mirrorAutomapFoldout = EditorGUILayout.Foldout(mirrorAutomapFoldout, "Automap", true);
                    if (mirrorAutomapFoldout)
                    {
                        EditorGUI.indentLevel++;
                        {
                            EditorGUILayout.LabelField("Generic");
                            EditorGUI.indentLevel++;
                            {
                                #region settingGenericMirrorName
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingGenericMirrorName = EditorGUILayout.Toggle(Language.GetContent(Language.Help.SettingsSearchByName), SettingGenericMirrorName);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        EditorPrefs.SetBool(PrefKey_GenericMirrorName, SettingGenericMirrorName);
                                    }
                                    if (SettingGenericMirrorName)
                                    {
                                        EditorGUI.indentLevel++;
                                        #region settingGenericMirrorNameDifferentCharacters
                                        {
                                            EditorGUI.BeginChangeCheck();
                                            SettingGenericMirrorNameDifferentCharacters = EditorGUILayout.TextField(Styles.guiContentDifferentCharacters, SettingGenericMirrorNameDifferentCharacters);
                                            if (EditorGUI.EndChangeCheck())
                                            {
                                                EditorPrefs.SetString(PrefKey_GenericMirrorNameDifferentCharacters, SettingGenericMirrorNameDifferentCharacters);
                                            }
                                        }
                                        #endregion
                                        #region settingGenericMirrorNameIgnoreCharacter
                                        {
                                            EditorGUILayout.BeginHorizontal();
                                            {
                                                EditorGUI.BeginChangeCheck();
                                                SettingGenericMirrorNameIgnoreCharacter = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.SettingsIgnoreUpToTheSpecifiedCharacter), SettingGenericMirrorNameIgnoreCharacter);
                                                if (EditorGUI.EndChangeCheck())
                                                {
                                                    EditorPrefs.SetBool(PrefKey_GenericMirrorNameIgnoreCharacter, SettingGenericMirrorNameIgnoreCharacter);
                                                }
                                            }
                                            if (SettingGenericMirrorNameIgnoreCharacter)
                                            {
                                                EditorGUI.BeginChangeCheck();
                                                SettingGenericMirrorNameIgnoreCharacterString = EditorGUILayout.TextField(SettingGenericMirrorNameIgnoreCharacterString, GUILayout.Width(100));
                                                if (EditorGUI.EndChangeCheck())
                                                {
                                                    EditorPrefs.SetString(PrefKey_GenericMirrorNameIgnoreCharacterString, SettingGenericMirrorNameIgnoreCharacterString);
                                                }
                                            }
                                            EditorGUILayout.EndHorizontal();
                                        }
                                        #endregion
                                        EditorGUI.indentLevel--;
                                    }
                                }
                                #endregion
                            }
                            EditorGUI.indentLevel--;

                            EditorGUILayout.LabelField("Blend Shape");
                            EditorGUI.indentLevel++;
                            {
                                #region settingBlendShapeMirrorName
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingBlendShapeMirrorName = EditorGUILayout.Toggle(Language.GetContent(Language.Help.SettingsSearchByName), SettingBlendShapeMirrorName);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        EditorPrefs.SetBool(PrefKey_BlendShapeMirrorName, SettingBlendShapeMirrorName);
                                    }
                                    if (SettingBlendShapeMirrorName)
                                    {
                                        EditorGUI.indentLevel++;
                                        #region settingBlendShapeMirrorNameDifferentCharacters
                                        {
                                            EditorGUI.BeginChangeCheck();
                                            SettingBlendShapeMirrorNameDifferentCharacters = EditorGUILayout.TextField(Styles.guiContentDifferentCharacters, SettingBlendShapeMirrorNameDifferentCharacters);
                                            if (EditorGUI.EndChangeCheck())
                                            {
                                                EditorPrefs.SetString(PrefKey_BlendShapeMirrorNameDifferentCharacters, SettingBlendShapeMirrorNameDifferentCharacters);
                                            }
                                        }
                                        #endregion
                                        EditorGUI.indentLevel--;
                                    }
                                }
                                #endregion
                            }
                            EditorGUI.indentLevel--;
                        }
                        EditorGUI.indentLevel--;
                    }

                    EditorGUI.indentLevel--;
                }
                extraFoldout = EditorGUILayout.Foldout(extraFoldout, "Extra functions", true);
                if (extraFoldout)
                {
                    EditorGUI.indentLevel++;
                    {
                        #region SynchronizeAnimation
                        {
                            var enable = !EditorApplication.isPlaying && !VAW.VA.UAw.GetLinkedWithTimeline();
                            EditorGUI.BeginDisabledGroup(!enable);
                            EditorGUI.BeginChangeCheck();
                            var flag = EditorGUILayout.ToggleLeft(Language.GetContent(Language.Help.SettingsSynchronizeAnimation), VAW.VA.extraOptionsSynchronizeAnimation);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(VAW, "Change Synchronize Animation");
                                VAW.VA.extraOptionsSynchronizeAnimation = flag;
                                VAW.VA.SetSynchronizeAnimation(VAW.VA.extraOptionsSynchronizeAnimation);
                                VAE.Repaint();
                                SceneView.RepaintAll();
                            }
                            EditorGUI.EndDisabledGroup();
                        }
                        #endregion
                        #region OnionSkin
                        {
                            EditorGUILayout.BeginHorizontal();
                            {
                                {
                                    EditorGUI.BeginChangeCheck();
                                    var flag = EditorGUILayout.ToggleLeft("", VAW.VA.extraOptionsOnionSkin, GUILayout.Width(28f));
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        Undo.RecordObject(VAW, "Change Onion Skin");
                                        VAW.VA.extraOptionsOnionSkin = flag;
                                        VAW.VA.OnionSkin.Update();
                                        VAE.Repaint();
                                        SceneView.RepaintAll();
                                    }
                                }
                                {
                                    var saveLevel = EditorGUI.indentLevel;
                                    EditorGUI.indentLevel = 0;
                                    extraOnionSkinningFoldout = EditorGUILayout.Foldout(extraOnionSkinningFoldout, Language.GetContent(Language.Help.SettingsOnionSkin), true);
                                    EditorGUI.indentLevel = saveLevel;
                                    if (!VAW.VA.extraOptionsOnionSkin)
                                        extraOnionSkinningFoldout = false;
                                }
                            }
                            EditorGUILayout.EndHorizontal();
                            if (extraOnionSkinningFoldout)
                            {
                                EditorGUI.indentLevel++;
                                #region settingExtraOnionSkinMode
                                {
                                    EditorGUILayout.BeginHorizontal();
                                    EditorGUILayout.PrefixLabel("Mode");
                                    EditorGUI.BeginChangeCheck();
                                    SettingExtraOnionSkinMode = (OnionSkinMode)GUILayout.Toolbar((int)SettingExtraOnionSkinMode, Styles.onionSkinModeStrings, EditorStyles.miniButton);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        EditorPrefs.SetInt(PrefKey_ExtraOnionSkinMode, (int)(SettingExtraOnionSkinMode));
                                        VAW.VA.OnionSkin.Update();
                                        SceneView.RepaintAll();
                                    }
                                    EditorGUILayout.EndHorizontal();

                                    EditorGUI.indentLevel++;
                                    #region settingExtraOnionSkinFrameIncrement
                                    if (SettingExtraOnionSkinMode == OnionSkinMode.Frames)
                                    {
                                        EditorGUI.BeginChangeCheck();
                                        SettingExtraOnionSkinFrameIncrement = EditorGUILayout.IntSlider("Frame Increment", SettingExtraOnionSkinFrameIncrement, 1, 60);
                                        if (EditorGUI.EndChangeCheck())
                                        {
                                            EditorPrefs.SetInt(PrefKey_ExtraOnionSkinFrameIncrement, SettingExtraOnionSkinFrameIncrement);
                                            VAW.VA.OnionSkin.Update();
                                            SceneView.RepaintAll();
                                        }
                                    }
                                    #endregion
                                    EditorGUI.indentLevel--;
                                }
                                #endregion
                                #region Next
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingExtraOnionSkinNextCount = EditorGUILayout.IntSlider("Next", SettingExtraOnionSkinNextCount, 0, 10);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        EditorPrefs.SetInt(PrefKey_ExtraOnionSkinNextCount, SettingExtraOnionSkinNextCount);
                                        VAW.VA.OnionSkin.Update();
                                        SceneView.RepaintAll();
                                    }
                                    EditorGUI.indentLevel++;
                                    {
                                        EditorGUILayout.BeginHorizontal();
                                        EditorGUILayout.PrefixLabel(Styles.guiContentColor);
                                        {
                                            EditorGUI.BeginChangeCheck();
                                            SettingExtraOnionSkinNextColor = EditorGUILayout.ColorField(SettingExtraOnionSkinNextColor, GUILayout.Width(80f));
                                            if (EditorGUI.EndChangeCheck())
                                            {
                                                SetEditorPrefsColor(PrefKey_ExtraOnionSkinNextColor, SettingExtraOnionSkinNextColor);
                                                VAW.VA.OnionSkin.Update();
                                                SceneView.RepaintAll();
                                            }
                                        }
                                        {
                                            EditorGUI.BeginChangeCheck();
                                            SettingExtraOnionSkinNextMinAlpha = EditorGUILayout.Slider(SettingExtraOnionSkinNextMinAlpha, 0f, 1f);
                                            if (EditorGUI.EndChangeCheck())
                                            {
                                                EditorPrefs.SetFloat(PrefKey_ExtraOnionSkinNextMinAlpha, SettingExtraOnionSkinNextMinAlpha);
                                                VAW.VA.OnionSkin.Update();
                                                SceneView.RepaintAll();
                                            }
                                        }
                                        EditorGUILayout.EndHorizontal();
                                    }
                                    EditorGUI.indentLevel--;
                                }
                                #endregion
                                #region Prev
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingExtraOnionSkinPrevCount = EditorGUILayout.IntSlider("Previous", SettingExtraOnionSkinPrevCount, 0, 10);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        EditorPrefs.SetInt(PrefKey_ExtraOnionSkinPrevCount, SettingExtraOnionSkinPrevCount);
                                        VAW.VA.OnionSkin.Update();
                                        SceneView.RepaintAll();
                                    }
                                    EditorGUI.indentLevel++;
                                    {
                                        EditorGUILayout.BeginHorizontal();
                                        EditorGUILayout.PrefixLabel(Styles.guiContentColor);
                                        {
                                            EditorGUI.BeginChangeCheck();
                                            SettingExtraOnionSkinPrevColor = EditorGUILayout.ColorField(SettingExtraOnionSkinPrevColor, GUILayout.Width(80f));
                                            if (EditorGUI.EndChangeCheck())
                                            {
                                                SetEditorPrefsColor(PrefKey_ExtraOnionSkinPrevColor, SettingExtraOnionSkinPrevColor);
                                                VAW.VA.OnionSkin.Update();
                                                SceneView.RepaintAll();
                                            }
                                        }
                                        {
                                            EditorGUI.BeginChangeCheck();
                                            SettingExtraOnionSkinPrevMinAlpha = EditorGUILayout.Slider(SettingExtraOnionSkinPrevMinAlpha, 0f, 1f);
                                            if (EditorGUI.EndChangeCheck())
                                            {
                                                EditorPrefs.SetFloat(PrefKey_ExtraOnionSkinPrevMinAlpha, SettingExtraOnionSkinPrevMinAlpha);
                                                VAW.VA.OnionSkin.Update();
                                                SceneView.RepaintAll();
                                            }
                                        }
                                        EditorGUILayout.EndHorizontal();
                                    }
                                    EditorGUI.indentLevel--;
                                }
                                #endregion
                                EditorGUI.indentLevel--;
                            }
                        }
                        #endregion
                        #region RootTrail
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUI.BeginDisabledGroup(!VAW.VA.IsHuman);
                            {
                                {
                                    EditorGUI.BeginChangeCheck();
                                    var flag = EditorGUILayout.ToggleLeft("", VAW.VA.extraOptionsRootTrail, GUILayout.Width(28f));
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        Undo.RecordObject(VAW, "Change Root Trail");
                                        VAW.VA.extraOptionsRootTrail = flag;
                                        VAE.Repaint();
                                        SceneView.RepaintAll();
                                    }
                                }
                                {
                                    var saveLevel = EditorGUI.indentLevel;
                                    EditorGUI.indentLevel = 0;
                                    extraRootTrailFoldout = EditorGUILayout.Foldout(extraRootTrailFoldout, Language.GetContent(Language.Help.SettingsRootTrail), true);
                                    EditorGUI.indentLevel = saveLevel;
                                    if (!VAW.VA.extraOptionsRootTrail)
                                        extraRootTrailFoldout = false;
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                            if (extraRootTrailFoldout)
                            {
                                EditorGUI.indentLevel++;
                                {
                                    EditorGUI.BeginChangeCheck();
                                    SettingExtraRootTrailColor = EditorGUILayout.ColorField("Color", SettingExtraRootTrailColor);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        SetEditorPrefsColor(PrefKey_ExtraRootTrailColor, SettingExtraRootTrailColor);
                                        SceneView.RepaintAll();
                                    }
                                }
                                EditorGUI.indentLevel--;
                            }
                            EditorGUI.EndDisabledGroup();
                        }
                        #endregion
                    }
                    EditorGUI.indentLevel--;
                }

                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.Space();
                    if (GUILayout.Button("Reset"))
                    {
                        Reset();
                    }
                    EditorGUILayout.Space();
                    EditorGUILayout.EndHorizontal();

                    GUILayout.Space(4);
                }

                #region RestartOnly
                if (settingEditorWindowStyleBefore != SettingEditorWindowStyle)
                {
                    EditorGUILayout.HelpBox(Language.GetText(Language.Help.SettingsRestartOnly), MessageType.Warning);
                }
                #endregion
            }
            EditorGUILayout.EndVertical();
        }

        public float GetSkeletonTypeLinesRadius(Vector3 position)
        {
            return HandleUtility.GetHandleSize(position) * (SettingBoneButtonSize / 200f);
        }

        private Color GetEditorPrefsColor(string name, Color defcolor)
        {
            return new(EditorPrefs.GetFloat($"{name}_r", defcolor.r),
                        EditorPrefs.GetFloat($"{name}_g", defcolor.g),
                        EditorPrefs.GetFloat($"{name}_b", defcolor.b),
                        EditorPrefs.GetFloat($"{name}_a", defcolor.a));
        }
        private void SetEditorPrefsColor(string name, Color color)
        {
            EditorPrefs.SetFloat($"{name}_r", color.r);
            EditorPrefs.SetFloat($"{name}_g", color.g);
            EditorPrefs.SetFloat($"{name}_b", color.b);
            EditorPrefs.SetFloat($"{name}_a", color.a);
        }
    }
}
