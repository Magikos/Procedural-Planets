using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using VeryAnimation.grendgine_collada;
#if VERYANIMATION_ANIMATIONRIGGING
using UnityEngine.Animations.Rigging;
using UnityEditor.Animations.Rigging;
#endif

namespace VeryAnimation
{
    internal class DaeExporter
    {
        private static readonly string ContributorAuthoring_Tool = typeof(DaeExporter).Namespace;
        private const string ContributorComments = "https://assetstore.unity.com/packages/tools/animation/very-animation-96826";     //VA
        //private const string ContributorComments = "https://assetstore.unity.com/packages/tools/modeling/voxel-importer-62914";   //VI

        public bool Export(string path, Transform[] transforms, AnimationClip[] clips = null)
        {
            if (transforms == null || transforms.Length == 0)
                return false;
            int progressTotal = 1 + (clips?.Length ?? 0);
            int progressIndex = 0;
            EditorUtility.DisplayProgressBar("Exporting Collada(dae) File...", Path.GetFileName(path), (progressIndex++ / (float)progressTotal));

            #region TransformSave
            var transformsSave = new Dictionary<Transform, TransformSave>(transforms.Length);
            foreach (var t in transforms)
            {
                transformsSave.TryAdd(t, new TransformSave(t));
            }
            var transformsSet = new HashSet<Transform>(transforms);
            #endregion
            #region FixTransform
            foreach (var t in transforms)
            {
                #region Do not allow scale zero
                {
                    bool update = false;
                    var scale = t.localScale;
                    for (int si = 0; si < 3; si++)
                    {
                        if (scale[si] == 0f)
                        {
                            scale[si] = Mathf.Epsilon;
                            update = true;
                        }
                    }
                    if (update)
                        t.localScale = scale;
                }
                #endregion
            }
            #endregion

            var numberFormatInfo = CultureInfo.InvariantCulture.NumberFormat;

            try
            {
                exportedFiles.Clear();

                Dictionary<string, UnityEngine.Object> sourceObjects = new();

                var rootObject = transforms[0].gameObject;

                Grendgine_Collada gCollada = new();

                string MakeID(UnityEngine.Object o)
                {
#if UNITY_6000_4_OR_NEWER
                    return EntityId.ToULong(o.GetEntityId()).ToString(numberFormatInfo);
#else
                    return o.GetInstanceID().ToString(numberFormatInfo).Replace('-', 'n');
#endif
                }
                static (Mesh mesh, Material[] mats) GetMeshAndMaterials(Transform t)
                {
                    if (t.TryGetComponent<SkinnedMeshRenderer>(out var smr) &&
                        smr.sharedMesh != null && smr.sharedMaterials != null && smr.enabled)
                        return (smr.sharedMesh, smr.sharedMaterials);
                    if (t.TryGetComponent<MeshFilter>(out var mf) &&
                        t.TryGetComponent<MeshRenderer>(out var mr) &&
                        mf.sharedMesh != null && mr.sharedMaterials != null && mr.enabled)
                        return (mf.sharedMesh, mr.sharedMaterials);
                    return (null, null);
                }
                static Mesh MeshFromTransform(Transform t) => GetMeshAndMaterials(t).mesh;
                static Material[] MaterialsFromTransform(Transform t) => GetMeshAndMaterials(t).mats;
                void MakeFromTransform(Action<Transform, Mesh, Material[]> action)
                {
                    foreach (var t in transforms)
                    {
                        if (settings_activeOnly && !t.gameObject.activeInHierarchy) continue;
                        var (mesh, mats) = GetMeshAndMaterials(t);
                        if (mesh != null && mats != null)
                            action(t, mesh, mats);
                    }
                }

                Matrix4x4 matMirrorX = Matrix4x4.identity;
                matMirrorX.m00 = -matMirrorX.m00;

                var MatrixIdentity = new Grendgine_Collada_Matrix()
                {
                    sID = "transform",
                };
                {
                    var mat = Matrix4x4.identity;
                    var sb = new StringBuilder();
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++)
                            sb.AppendFormat(numberFormatInfo, "{0} ", mat[r, c]);
                    sb.Remove(sb.Length - 1, 1);
                    MatrixIdentity.Value_As_String = sb.ToString();
                }

                bool makeJoint = rootObject.GetComponentInChildren<SkinnedMeshRenderer>() != null;

                #region Header
                {
                    gCollada.Collada_Version = "1.4.1";     //for Blender

                    gCollada.Asset = new Grendgine_Collada_Asset()
                    {
                        Created = DateTime.Now,
                        Modified = DateTime.Now,
                        Contributor = new Grendgine_Collada_Asset_Contributor[]
                        {
                            new()
                            {
                                Authoring_Tool = ContributorAuthoring_Tool,
                                Comments = ContributorComments,
                            },
                        },
                        Revision = "1.0",
                        Title = Path.GetFileNameWithoutExtension(path),
                        Unit = new Grendgine_Collada_Asset_Unit()
                        {
                            Name = "meter",
                            Meter = 1.0f,
                        },
                    };
                }
                #endregion

                #region Images
                var imagesDic = new Dictionary<Texture, Grendgine_Collada_Image>();
                if (settings_exportMesh)
                {
                    var li = gCollada.Library_Images = new Grendgine_Collada_Library_Images()
                    {
                        ID = $"Images_{MakeID(rootObject)}",
                        Name = $"Images_{rootObject.name}",
                    };

                    bool singleImage;
                    {
                        var texList = new HashSet<Texture>();
                        MakeFromTransform((t, mesh, materials) =>
                        {
                            foreach (var material in materials)
                            {
                                if (material == null) continue;
                                if (material.HasProperty("_MainTex") && material.mainTexture != null)
                                    texList.Add(material.mainTexture);
                            }
                        });
                        singleImage = texList.Count <= 1;
                    }

                    string ExportTexture(Texture tex)
                    {
                        string texpath;
                        var exported = true;
                        var exportDir = (Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/');
                        if (AssetDatabase.Contains(tex) && AssetDatabase.IsMainAsset(tex))
                        {
                            if (path.StartsWith(Application.dataPath, StringComparison.Ordinal))
                            {
                                var assetPath = AssetDatabase.GetAssetPath(tex);
                                var dstAssetPath = $"{Path.GetDirectoryName(FileUtil.GetProjectRelativePath(path))}/{Path.GetFileName(assetPath)}";
                                texpath = $"{exportDir}/{Path.GetFileName(assetPath)}";
                                if (AssetDatabase.LoadAssetAtPath<Texture2D>(dstAssetPath) == null)
                                    AssetDatabase.CopyAsset(assetPath, dstAssetPath);
                                else
                                {
                                    var srcFullPath = Application.dataPath + assetPath["Assets".Length..];
                                    if (srcFullPath != texpath)
                                        File.Copy(srcFullPath, texpath, true);
                                }
                            }
                            else
                            {
                                var assetPath = Application.dataPath + AssetDatabase.GetAssetPath(tex)["Assets".Length..];
                                texpath = $"{exportDir}/{Path.GetFileName(assetPath)}";
                                if (assetPath != texpath)
                                    File.Copy(assetPath, texpath, true);
                            }
                        }
                        else
                        {
                            const string EXT = ".png";
                            if (singleImage)
                                texpath = path[..^EXT.Length] + EXT;
                            else
                                texpath = $"{exportDir}/{Path.GetFileNameWithoutExtension(path)}_tex{imagesDic.Count}{EXT}";

                            Texture2D tex2D = null;
                            bool created = false;
                            if (tex is Texture2D t && t.isReadable)
                            {
                                tex2D = t;
                            }
                            else
                            {
                                var currentRT = RenderTexture.active;
                                RenderTexture rt = null;
                                try
                                {
                                    rt = new RenderTexture(tex.width, tex.height, 32);
                                    Graphics.Blit(tex, rt);
                                    RenderTexture.active = rt;
                                    tex2D = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                                    tex2D.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                                    tex2D.Apply();
                                    created = true;
                                }
                                finally
                                {
                                    RenderTexture.active = currentRT;
                                    if (rt != null)
                                    {
                                        rt.Release();
                                        RenderTexture.DestroyImmediate(rt);
                                    }
                                    if (!created && tex2D != null)
                                    {
                                        Texture2D.DestroyImmediate(tex2D);
                                        tex2D = null;
                                    }
                                }
                            }
                            if (tex2D != null)
                            {
                                try
                                {
                                    File.WriteAllBytes(texpath, tex2D.EncodeToPNG());
                                }
                                catch
                                {
                                    exported = false;
                                    Debug.LogWarning($"<color=green>[{ContributorAuthoring_Tool}]</color> Texture Export Error. '{tex.name}'");
                                }
                            }
                            else
                            {
                                exported = false;
                                Debug.LogWarning($"<color=green>[{ContributorAuthoring_Tool}]</color> Texture Export Error. '{tex.name}'");
                            }
                            if (created && tex2D)
                            {
                                Texture2D.DestroyImmediate(tex2D);
                            }
                        }
                        texpath = texpath.Replace('\\', '/');
                        if (exported)
                        {
                            exportedFiles.Add(texpath);
                            if (!sourceObjects.TryAdd(texpath, tex))
                                Debug.LogWarning($"<color=green>[{ContributorAuthoring_Tool}]</color> It was overwritten because there is a texture with the same name. : {tex.name}");
                        }
                        return Path.GetFileName(texpath);
                    }

                    MakeFromTransform((t, mesh, materials) =>
                    {
                        foreach (var material in materials)
                        {
                            if (material == null) continue;
                            if (!material.HasProperty("_MainTex"))
                                continue;
                            var tex = material.mainTexture;
                            if (tex == null || imagesDic.ContainsKey(tex))
                                continue;
                            var image = new Grendgine_Collada_Image()
                            {
                                ID = $"Image_{MakeID(tex)}",
                                Name = tex.name,
                                Init_From = Uri.EscapeDataString(ExportTexture(tex)),
                            };
                            imagesDic.Add(tex, image);
                        }
                    });
                    li.Image = imagesDic.Values.ToArray();
                }
                #endregion

                #region Effects
                var effectsDic = new Dictionary<Material, Grendgine_Collada_Effect>();
                if (settings_exportMesh)
                {
                    var le = gCollada.Library_Effects = new Grendgine_Collada_Library_Effects()
                    {
                        ID = $"Effects_{MakeID(rootObject)}",
                        Name = $"Effects_{rootObject.name}",
                    };
                    MakeFromTransform((t, mesh, materials) =>
                    {
                        foreach (var material in materials)
                        {
                            if (material == null) continue;
                            if (!material.HasProperty("_MainTex"))
                                continue;
                            var tex = material.mainTexture;
                            if (effectsDic.ContainsKey(material))
                                continue;
                            Grendgine_Collada_New_Param[] New_Param = null;
                            Grendgine_Collada_FX_Common_Color_Or_Texture_Type Diffuse = null;
                            if (tex != null && imagesDic.TryGetValue(tex, out var imageEntry))
                            {
                                Grendgine_Collada_New_Param surfaceParam = new()
                                {
                                    sID = $"Surface_{MakeID(material)}",
                                    Surface = new Grendgine_Collada_Surface_1_4_1()
                                    {
                                        Type = Grendgine_Collada_FX_Surface_Type._2D,
                                        Init_From = imageEntry.ID,
                                    },
                                };
                                Grendgine_Collada_New_Param sampler2DParam = new()
                                {
                                    sID = $"Sampler2D_{MakeID(material)}",
                                    Sampler2D = new Grendgine_Collada_Sampler2D()
                                    {
                                        Source = surfaceParam.sID,
                                    },
                                };
                                New_Param = new Grendgine_Collada_New_Param[]
                                {
                                    surfaceParam,
                                    sampler2DParam,
                                };
                                Diffuse = new Grendgine_Collada_FX_Common_Color_Or_Texture_Type()
                                {
                                    Texture = new Grendgine_Collada_Texture()
                                    {
                                        TexCoord = $"TexCoord_{MakeID(tex)}",
                                        Texture = sampler2DParam.sID,
                                    },
                                };
                            }
                            else
                            {
                                Color color = Color.white;
                                if (material.HasProperty("_Color"))
                                    color = material.color;
                                Diffuse = new Grendgine_Collada_FX_Common_Color_Or_Texture_Type()
                                {
                                    Color = new Grendgine_Collada_Color()
                                    {
                                        sID = "diffuse",
                                        Value_As_String = string.Format(numberFormatInfo, "{0} {1} {2} {3}", color.r, color.g, color.b, color.a),
                                    },
                                };
                            }

                            var e = new Grendgine_Collada_Effect()
                            {
                                ID = $"Effect_{MakeID(material)}",
                                Name = material.name,
                                Profile_COMMON = new Grendgine_Collada_Profile_COMMON[]
                                {
                                    new()
                                    {
                                        ID = $"Profile_{MakeID(material)}",
                                        New_Param = New_Param,
                                        Technique = new Grendgine_Collada_Effect_Technique_COMMON()
                                        {
                                            sID = $"Technique_{MakeID(material)}",
                                            Phong = new Grendgine_Collada_Phong()
                                            {
                                                Emission = new Grendgine_Collada_FX_Common_Color_Or_Texture_Type()
                                                {
                                                    Color = new Grendgine_Collada_Color()
                                                    {
                                                        sID = "emission",
                                                        Value_As_String = "0 0 0 1",
                                                    },
                                                },
                                                Ambient = new Grendgine_Collada_FX_Common_Color_Or_Texture_Type()
                                                {
                                                    Color = new Grendgine_Collada_Color()
                                                    {
                                                        sID = "ambient",
                                                        Value_As_String = "1 1 1 1",
                                                    },
                                                },
                                                Diffuse = Diffuse,
                                                Specular = new Grendgine_Collada_FX_Common_Color_Or_Texture_Type()
                                                {
                                                    Color = new Grendgine_Collada_Color()
                                                    {
                                                        sID = "specular",
                                                        Value_As_String = "0 0 0 1",
                                                    },
                                                },
                                                Shininess = new Grendgine_Collada_FX_Common_Float_Or_Param_Type()
                                                {
                                                    Float = new Grendgine_Collada_SID_Float()
                                                    {
                                                        sID = "shininess",
                                                        Value = 50,
                                                    },
                                                },
                                                Index_Of_Refraction = new Grendgine_Collada_FX_Common_Float_Or_Param_Type()
                                                {
                                                    Float = new Grendgine_Collada_SID_Float()
                                                    {
                                                        sID = "index_of_refraction",
                                                        Value = 1,
                                                    },
                                                },
                                            },
                                        },
                                    },
                                },
                            };
                            effectsDic.Add(material, e);
                        }
                    });
                    le.Effect = effectsDic.Values.ToArray();
                }
                #endregion

                #region Materials
                var materialsDic = new Dictionary<Material, Grendgine_Collada_Material>();
                if (settings_exportMesh)
                {
                    var lm = gCollada.Library_Materials = new Grendgine_Collada_Library_Materials()
                    {
                        ID = $"Materials_{MakeID(rootObject)}",
                        Name = $"Materials_{rootObject.name}",
                    };
                    MakeFromTransform((t, mesh, materials) =>
                    {
                        foreach (var material in materials)
                        {
                            if (material == null) continue;
                            if (!effectsDic.TryGetValue(material, out var effect)) continue;
                            if (materialsDic.ContainsKey(material)) continue;
                            var m = new Grendgine_Collada_Material()
                            {
                                ID = $"Material_{MakeID(material)}",
                                Name = material.name,
                                Instance_Effect = new Grendgine_Collada_Instance_Effect()
                                {
                                    URL = $"#{effect.ID}",
                                },
                            };
                            materialsDic.Add(material, m);
                        }
                    });
                    lm.Material = materialsDic.Values.ToArray();
                }
                #endregion

                #region Geometries
                var geometriesDic = new Dictionary<Transform, Grendgine_Collada_Geometry>();
                if (settings_exportMesh)
                {
                    var lg = gCollada.Library_Geometries = new Grendgine_Collada_Library_Geometries()
                    {
                        ID = $"Geometries_{MakeID(rootObject)}",
                        Name = $"Geometries_{rootObject.name}",
                    };
                    MakeFromTransform((t, mesh, materials) =>
                    {
                        #region Source
                        #region Vertex
                        Grendgine_Collada_Source vertexSource;
                        {
                            Grendgine_Collada_Float_Array array;
                            {
                                var sb = new StringBuilder();
                                foreach (var v in mesh.vertices)
                                {
                                    var mv = matMirrorX.MultiplyPoint(v);
                                    sb.AppendFormat(numberFormatInfo, "\n{0} {1} {2}", mv.x, mv.y, mv.z);
                                }
                                array = new Grendgine_Collada_Float_Array()
                                {
                                    ID = $"VertexArray_{MakeID(mesh)}",
                                    Name = $"{mesh.name}_vertex",
                                    Count = mesh.vertexCount * 3,
                                    Value_As_String = sb.ToString(),
                                };
                            }
                            vertexSource = new Grendgine_Collada_Source()
                            {
                                ID = $"VertexSource_{MakeID(mesh)}",
                                Name = $"{mesh.name}_vertex",
                                Float_Array = array,
                                Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                {
                                    Accessor = new Grendgine_Collada_Accessor()
                                    {
                                        Count = (uint)mesh.vertexCount,
                                        Source = $"#{array.ID}",
                                        Stride = 3,
                                        Param = new Grendgine_Collada_Param[]
                                        {
                                            new() { Name = "X", Type = "float", },
                                            new() { Name = "Y", Type = "float", },
                                            new() { Name = "Z", Type = "float", },
                                        },
                                    },
                                },
                            };
                        }
                        #endregion
                        #region UV
                        Grendgine_Collada_Source uvSource;
                        {
                            Grendgine_Collada_Float_Array array;
                            {
                                var sb = new StringBuilder();
                                foreach (var uv in mesh.uv)
                                    sb.AppendFormat(numberFormatInfo, "\n{0} {1}", uv.x, uv.y);
                                array = new Grendgine_Collada_Float_Array()
                                {
                                    ID = $"UVArray_{MakeID(mesh)}",
                                    Name = $"{mesh.name}_uv",
                                    Count = mesh.vertexCount * 2,
                                    Value_As_String = sb.ToString(),
                                };
                            }
                            uvSource = new Grendgine_Collada_Source()
                            {
                                ID = $"UVSource_{MakeID(mesh)}",
                                Name = $"{mesh.name}_uv",
                                Float_Array = array,
                                Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                {
                                    Accessor = new Grendgine_Collada_Accessor()
                                    {
                                        Count = (uint)mesh.vertexCount,
                                        Source = $"#{array.ID}",
                                        Stride = 2,
                                        Param = new Grendgine_Collada_Param[]
                                        {
                                            new() { Name = "S", Type = "float", },
                                            new() { Name = "T", Type = "float", },
                                        },
                                    },
                                },
                            };
                        }
                        #endregion
                        #region Normal
                        Grendgine_Collada_Source normalSource;
                        {
                            Grendgine_Collada_Float_Array array;
                            {
                                var sb = new StringBuilder();
                                foreach (var n in mesh.normals)
                                {
                                    var mn = matMirrorX.MultiplyVector(n);
                                    sb.AppendFormat(numberFormatInfo, "\n{0} {1} {2}", mn.x, mn.y, mn.z);
                                }
                                array = new Grendgine_Collada_Float_Array()
                                {
                                    ID = $"NormalArray_{MakeID(mesh)}",
                                    Name = $"{mesh.name}_normal",
                                    Count = mesh.vertexCount * 3,
                                    Value_As_String = sb.ToString(),
                                };
                            }
                            normalSource = new Grendgine_Collada_Source()
                            {
                                ID = $"NormalSource_{MakeID(mesh)}",
                                Name = $"{mesh.name}_normal",
                                Float_Array = array,
                                Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                {
                                    Accessor = new Grendgine_Collada_Accessor()
                                    {
                                        Count = (uint)mesh.vertexCount,
                                        Source = $"#{array.ID}",
                                        Stride = 3,
                                        Param = new Grendgine_Collada_Param[]
                                        {
                                            new() { Name = "X", Type = "float", },
                                            new() { Name = "Y", Type = "float", },
                                            new() { Name = "Z", Type = "float", },
                                        },
                                    },
                                },
                            };
                        }
                        #endregion
                        #endregion

                        #region Vertices
                        Grendgine_Collada_Vertices vertices;
                        {
                            vertices = new Grendgine_Collada_Vertices()
                            {
                                ID = $"Vertices_{MakeID(mesh)}",
                                Name = $"{mesh.name}_vertices",
                                Input = new Grendgine_Collada_Input_Unshared[]
                                {
                                    new()
                                    {
                                        Semantic = Grendgine_Collada_Input_Semantic.POSITION,
                                        source = $"#{vertexSource.ID}",
                                    },
                                },
                            };
                        }
                        #endregion

                        #region Triangles
                        Grendgine_Collada_Triangles[] triangles;
                        {
                            triangles = new Grendgine_Collada_Triangles[mesh.subMeshCount];
                            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                            {
                                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                                {
                                    Debug.LogWarning($"<color=green>[{ContributorAuthoring_Tool}]</color> MeshTopology is not Triangles. Mesh = {mesh.name} - {subMesh}, MeshTopology = {mesh.GetTopology(subMesh)}");
                                    continue;
                                }
                                Grendgine_Collada_Material material = null;
                                if (subMesh < materials.Length && materials[subMesh] != null)
                                    materialsDic.TryGetValue(materials[subMesh], out material);
                                var ts = mesh.GetTriangles(subMesh);
                                var sb = new StringBuilder();
                                {
                                    for (int i = 0; i < ts.Length; i += 3)
                                        sb.AppendFormat(numberFormatInfo, "\n{0} {0} {0} {1} {1} {1} {2} {2} {2}", ts[i + 0], ts[i + 2], ts[i + 1]);
                                }
                                triangles[subMesh] = new Grendgine_Collada_Triangles()
                                {
                                    Count = ts.Length / 3,
                                    Name = $"{mesh.name}_triangles",
                                    Material = material?.ID,
                                    Input = new Grendgine_Collada_Input_Shared[]
                                    {
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.VERTEX,
                                            source = $"#{vertices.ID}",
                                            Offset = 0,
                                        },
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.TEXCOORD,
                                            source = $"#{uvSource.ID}",
                                            Offset = 1,
                                        },
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.NORMAL,
                                            source = $"#{normalSource.ID}",
                                            Offset = 2,
                                        },
                                    },
                                    P = new Grendgine_Collada_Int_Array_String()
                                    {
                                        Value_As_String = sb.ToString(),
                                    },
                                };
                            }
                        }
                        #endregion

                        var g = new Grendgine_Collada_Geometry()
                        {
                            ID = $"Geometry_{MakeID(mesh)}",
                            Name = mesh.name,
                            Mesh = new Grendgine_Collada_Mesh()
                            {
                                Source = new Grendgine_Collada_Source[]
                                {
                                    vertexSource,
                                    uvSource,
                                    normalSource,
                                },
                                Vertices = vertices,
                                Triangles = triangles,
                            },
                        };
                        geometriesDic.Add(t, g);
                    });
                    lg.Geometry = geometriesDic.Values.ToArray();
                }
                #endregion

                #region Nodes
                var nodesDic = new Dictionary<Transform, Grendgine_Collada_Node>();
                {
                    Grendgine_Collada_Node MakeNode(Transform t)
                    {
                        var node = new Grendgine_Collada_Node()
                        {
                            ID = $"Node_{MakeID(t)}",
                            Name = t.name,
                            sID = $"Node_{MakeID(t)}",
                            Type = Grendgine_Collada_Node_Type.NODE,
                        };
                        {
                            var mat = Matrix4x4.TRS(matMirrorX.MultiplyPoint3x4(t.localPosition),
                                                    new Quaternion(t.localRotation.x, -t.localRotation.y, -t.localRotation.z, t.localRotation.w), //mirrorX
                                                    t.localScale);
                            var sb = new StringBuilder();
                            for (int r = 0; r < 4; r++)
                                for (int c = 0; c < 4; c++)
                                    sb.AppendFormat(numberFormatInfo, "{0} ", mat[r, c]);
                            sb.Remove(sb.Length - 1, 1);
                            node.Matrix = new Grendgine_Collada_Matrix[]
                            {
                                new()
                                {
                                    sID = "transform",
                                    Value_As_String = sb.ToString(),
                                },
                            };
                        }
                        {
                            List<Grendgine_Collada_Node> nodes = new();
                            foreach (Transform ct in t)
                            {
                                if (!transformsSet.Contains(ct)) continue;
                                if (settings_activeOnly && !ct.gameObject.activeInHierarchy) continue;
                                var n = MakeNode(ct);
                                nodes.Add(n);
                                nodesDic.Add(ct, n);
                            }
                            node.node = nodes.ToArray();
                        }
                        if (geometriesDic.ContainsKey(t))
                        {
                            var mesh = MeshFromTransform(t);
                            if (mesh == null) return node;
                            var materials = MaterialsFromTransform(t);
                            if (materials == null) return node;
                            var Instance_Material = new Grendgine_Collada_Instance_Material_Geometry[materials.Length];
                            for (int j = 0; j < materials.Length; j++)
                            {
                                if (!materialsDic.TryGetValue(materials[j], out var mat)) continue;
                                Instance_Material[j] = new Grendgine_Collada_Instance_Material_Geometry()
                                {
                                    Target = $"#{mat.ID}",
                                    Symbol = mat.ID,
                                };
                                if (effectsDic[materials[j]].Profile_COMMON[0].Technique.Phong.Diffuse.Texture != null)
                                {
                                    Instance_Material[j].Bind_Vertex_Input = new Grendgine_Collada_Bind_Vertex_Input[]
                                    {
                                        new()
                                        {
                                            Input_Semantic = "TEXCOORD",
                                            Input_Set = 1,
                                            Semantic = effectsDic[materials[j]].Profile_COMMON[0].Technique.Phong.Diffuse.Texture.TexCoord,
                                        },
                                    };
                                }
                            }
                            node.Instance_Geometry = new Grendgine_Collada_Instance_Geometry[]
                            {
                                new()
                                {
                                    URL = $"#{geometriesDic[t].ID}",
                                    Bind_Material = new Grendgine_Collada_Bind_Material[]
                                    {
                                        new()
                                        {
                                            Technique_Common = new Grendgine_Collada_Technique_Common_Bind_Material()
                                            {
                                                Instance_Material = Instance_Material,
                                            },
                                        },
                                    },
                                },
                            };
                        }
                        return node;
                    }

                    nodesDic.Add(rootObject.transform, MakeNode(rootObject.transform));
                }
                #endregion

                #region Joints
                var jointsDic = new Dictionary<Transform, Grendgine_Collada_Node>();
                if (makeJoint)
                {
                    Grendgine_Collada_Node MakeJoint(Transform t)
                    {
                        var Doc = new System.Xml.XmlDocument();
                        var Data = new System.Xml.XmlElement[]
                        {
                            Doc.CreateElement("tip_x"),
                            Doc.CreateElement("tip_y"),
                            Doc.CreateElement("tip_z"),
                        };
                        bool enable = true;
                        {
                            Vector3 offset = Vector3.zero;
                            if (t.childCount > 0)
                            {
                                float dotMax = float.MinValue;
                                foreach (Transform childT in t)
                                {
                                    var vec = rootObject.transform.worldToLocalMatrix.MultiplyVector(childT.position - t.position);
                                    vec = matMirrorX.MultiplyVector(vec);
                                    var dot = Mathf.Abs(Vector3.Dot(vec, Vector3.up));
                                    if (dot > dotMax)
                                    {
                                        offset = vec;
                                        dotMax = dot;
                                    }
                                }
                            }
                            else
                            {
                                var vec = rootObject.transform.worldToLocalMatrix.MultiplyVector(t.position - t.parent.position);
                                if (vec.sqrMagnitude > 0)
                                {
                                    vec = vec.normalized * 0.0001f;
                                    offset = matMirrorX.MultiplyVector(vec);
                                }
                                else
                                {
                                    offset = new Vector3(0, 0, 0.0001f);
                                }
                            }
                            if (offset.sqrMagnitude <= 0f)
                                enable = false;
                            Data[0].InnerText = offset.x.ToString(numberFormatInfo);
                            Data[1].InnerText = offset.y.ToString(numberFormatInfo);
                            Data[2].InnerText = offset.z.ToString(numberFormatInfo);
                        }
                        Grendgine_Collada_Node joint = new()
                        {
                            ID = $"Joint_{MakeID(t)}",
                            Name = t.name,
                            sID = $"Joint_{MakeID(t)}",
                            Type = Grendgine_Collada_Node_Type.JOINT,
                            Matrix = nodesDic[t].Matrix,
                            Extra = enable ? new Grendgine_Collada_Extra[]
                            {
                                new()
                                {
                                    Technique = new Grendgine_Collada_Technique[]
                                    {
                                        new()
                                        {
                                            profile = "blender",
                                            Data = Data,
                                        },
                                    },
                                },
                            } : null,
                        };

                        List<Grendgine_Collada_Node> joints = new();
                        foreach (Transform ct in t)
                        {
                            if (!transformsSet.Contains(ct)) continue;
                            if (settings_activeOnly && !ct.gameObject.activeInHierarchy) continue;
                            var n = MakeJoint(ct);
                            joints.Add(n);
                            jointsDic.Add(ct, n);
                        }
                        joint.node = joints.ToArray();
                        return joint;
                    }

                    jointsDic.Add(rootObject.transform, MakeJoint(rootObject.transform));
                }
                #endregion

                #region Controllers
                var controllersDic = new Dictionary<Transform, Grendgine_Collada_Controller>();
                if (makeJoint && settings_exportMesh)
                {
                    var lc = gCollada.Library_Controllers = new Grendgine_Collada_Library_Controllers()
                    {
                        ID = $"Controllers_{MakeID(rootObject)}",
                        Name = $"Controllers_{rootObject.name}",
                    };

                    foreach (var t in transforms)
                    {
                        if (settings_activeOnly && !t.gameObject.activeInHierarchy) continue;
                        Mesh mesh = null;
                        Material[] materials = null;
                        #region SkinnedMeshRenderer
                        t.TryGetComponent<SkinnedMeshRenderer>(out var skinnedMeshRenderer);
                        {
                            if (skinnedMeshRenderer != null)
                            {
                                var smrMesh = skinnedMeshRenderer.sharedMesh;
                                var smrMats = skinnedMeshRenderer.sharedMaterials;
                                if (smrMesh != null && smrMats != null && skinnedMeshRenderer.enabled)
                                {
                                    mesh = smrMesh;
                                    materials = smrMats;
                                }
                            }
                        }
                        #endregion
                        #region MeshFilter
                        t.TryGetComponent<MeshFilter>(out var meshFilter);
                        t.TryGetComponent<MeshRenderer>(out var meshRenderer);
                        {
                            if (meshFilter != null && meshRenderer != null)
                            {
                                var mfMesh = meshFilter.sharedMesh;
                                var mrMats = meshRenderer.sharedMaterials;
                                if (mfMesh != null && mrMats != null && meshRenderer.enabled)
                                {
                                    mesh = mfMesh;
                                    materials = mrMats;
                                }
                            }
                        }
                        #endregion

                        Grendgine_Collada_Controller c;
                        if (mesh != null && materials != null && skinnedMeshRenderer != null && mesh.boneWeights.Length > 0)
                        {
                            #region SkinnedMeshRenderer
                            var bones = skinnedMeshRenderer.bones;
                            var boneWeights = mesh.boneWeights;

                            #region ErrorCheck
                            {
                                var checkBones = new List<Transform>(bones.Distinct());
                                if (checkBones.Count != bones.Length)
                                {
                                    Debug.LogWarning($"<color=green>[{ContributorAuthoring_Tool}]</color> There are two or more same Transforms in SkinnedMeshRenderer.bones. This is not supported.");
                                }
                            }
                            #endregion

                            #region Joints_Source
                            Grendgine_Collada_Source Joints_Source;
                            {
                                var Joints_Name_Array = new Grendgine_Collada_Name_Array()
                                {
                                    ID = $"Joints_Name_Array_{MakeID(t)}",
                                    Count = bones.Length,
                                };
                                {
                                    var names = new StringBuilder();
                                    foreach (var bone in bones)
                                    {
                                        if (bone != null && jointsDic.TryGetValue(bone, out var joint))
                                            names.AppendFormat(numberFormatInfo, "\n{0}", joint.ID);
                                        else
                                            names.AppendFormat(numberFormatInfo, "\n{0}", 0);
                                    }
                                    Joints_Name_Array.Value_Pre_Parse = names.ToString();
                                }
                                Joints_Source = new Grendgine_Collada_Source()
                                {
                                    ID = $"Joints_{MakeID(t)}",
                                    Name_Array = Joints_Name_Array,
                                    Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                    {
                                        Accessor = new Grendgine_Collada_Accessor()
                                        {
                                            Count = (uint)Joints_Name_Array.Count,
                                            Source = $"#{Joints_Name_Array.ID}",
                                            Param = new Grendgine_Collada_Param[]
                                            {
                                                new()
                                                {
                                                    Type = "name",
                                                },
                                            },
                                        },
                                    },
                                };
                            }
                            #endregion
                            #region Weights_Source
                            Grendgine_Collada_Source Weights_Source;
                            StringBuilder weightsVCountString = new();
                            StringBuilder weightsVString = new();
                            Dictionary<float, int> weightIndexTable = new();
                            {
                                var Weights_Float_Array = new Grendgine_Collada_Float_Array()
                                {
                                    ID = $"Weights_Float_Array_{MakeID(t)}",
                                };
                                {
                                    var sb = new StringBuilder();
                                    for (int i = 0; i < boneWeights.Length; i++)
                                    {
                                        var bw = boneWeights[i];
                                        int count = 0;
                                        {
                                            if (!weightIndexTable.TryGetValue(bw.weight0, out int idx0))
                                            {
                                                idx0 = weightIndexTable.Count;
                                                weightIndexTable.Add(bw.weight0, idx0);
                                                sb.AppendFormat(numberFormatInfo, "\n{0}", bw.weight0);
                                            }
                                            weightsVString.AppendFormat(numberFormatInfo, "\n{0} {1}", bw.boneIndex0, idx0);
                                            count++;
                                        }
                                        if (bw.weight1 > 0f)
                                        {
                                            if (!weightIndexTable.TryGetValue(bw.weight1, out int idx1))
                                            {
                                                idx1 = weightIndexTable.Count;
                                                weightIndexTable.Add(bw.weight1, idx1);
                                                sb.AppendFormat(numberFormatInfo, "\n{0}", bw.weight1);
                                            }
                                            weightsVString.AppendFormat(numberFormatInfo, " {0} {1}", bw.boneIndex1, idx1);
                                            count++;
                                        }
                                        if (bw.weight2 > 0f)
                                        {
                                            if (!weightIndexTable.TryGetValue(bw.weight2, out int idx2))
                                            {
                                                idx2 = weightIndexTable.Count;
                                                weightIndexTable.Add(bw.weight2, idx2);
                                                sb.AppendFormat(numberFormatInfo, "\n{0}", bw.weight2);
                                            }
                                            weightsVString.AppendFormat(numberFormatInfo, " {0} {1}", bw.boneIndex2, idx2);
                                            count++;
                                        }
                                        if (bw.weight3 > 0f)
                                        {
                                            if (!weightIndexTable.TryGetValue(bw.weight3, out int idx3))
                                            {
                                                idx3 = weightIndexTable.Count;
                                                weightIndexTable.Add(bw.weight3, idx3);
                                                sb.AppendFormat(numberFormatInfo, "\n{0}", bw.weight3);
                                            }
                                            weightsVString.AppendFormat(numberFormatInfo, " {0} {1}", bw.boneIndex3, idx3);
                                            count++;
                                        }
                                        weightsVCountString.AppendFormat(numberFormatInfo, "\n{0}", count);
                                    }
                                    Weights_Float_Array.Count = weightIndexTable.Count;
                                    Weights_Float_Array.Value_As_String = sb.ToString();
                                }
                                Weights_Source = new Grendgine_Collada_Source()
                                {
                                    ID = $"Weights_{MakeID(t)}",
                                    Float_Array = Weights_Float_Array,
                                    Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                    {
                                        Accessor = new Grendgine_Collada_Accessor()
                                        {
                                            Count = (uint)Weights_Float_Array.Count,
                                            Source = $"#{Weights_Float_Array.ID}",
                                            Param = new Grendgine_Collada_Param[]
                                            {
                                                new()
                                                {
                                                    Type = "float",
                                                },
                                            },
                                        },
                                    },
                                };
                            }
                            #endregion
                            #region Inv_Bind_Mats_Source
                            Grendgine_Collada_Source Inv_Bind_Mats_Source;
                            {
                                var Inv_Bind_Mats_Float_Array = new Grendgine_Collada_Float_Array()
                                {
                                    ID = $"Inv_Bind_Mats_{MakeID(t)}",
                                };
                                {
                                    var bindposes = skinnedMeshRenderer.sharedMesh.bindposes;
                                    var sb = new StringBuilder();
                                    for (int i = 0; i < bindposes.Length; i++)
                                    {
                                        Matrix4x4 mat = bindposes[i];
                                        {
                                            var position = mat.GetColumn(3);
                                            var rotation = (mat.GetColumn(2).sqrMagnitude > 0f && mat.GetColumn(1).sqrMagnitude > 0f) ? Quaternion.LookRotation(mat.GetColumn(2), mat.GetColumn(1)) : Quaternion.identity;
                                            var scale = new Vector3(mat.GetColumn(0).magnitude, mat.GetColumn(1).magnitude, mat.GetColumn(2).magnitude);
                                            #region Do not allow scale zero
                                            {
                                                for (int si = 0; si < 3; si++)
                                                {
                                                    if (scale[si] == 0f)
                                                        scale[si] = Mathf.Epsilon;
                                                }
                                            }
                                            #endregion
                                            mat = Matrix4x4.TRS(matMirrorX.MultiplyPoint3x4(position),
                                                                new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w), //mirrorX
                                                                scale);
                                        }
                                        for (int r = 0; r < 4; r++)
                                            sb.AppendFormat(numberFormatInfo, "\n{0} {1} {2} {3}", mat[r, 0], mat[r, 1], mat[r, 2], mat[r, 3]);
                                    }
                                    Inv_Bind_Mats_Float_Array.Count = bindposes.Length * 16;
                                    Inv_Bind_Mats_Float_Array.Value_As_String = sb.ToString();
                                }
                                Inv_Bind_Mats_Source = new Grendgine_Collada_Source()
                                {
                                    ID = $"Inv_Bind_Mats_{MakeID(t)}",
                                    Float_Array = Inv_Bind_Mats_Float_Array,
                                    Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                    {
                                        Accessor = new Grendgine_Collada_Accessor()
                                        {
                                            Count = (uint)(Inv_Bind_Mats_Float_Array.Count / 16),
                                            Source = $"#{Inv_Bind_Mats_Float_Array.ID}",
                                            Stride = 16,
                                            Param = new Grendgine_Collada_Param[]
                                            {
                                                new()
                                                {
                                                    Type = "float4x4",
                                                },
                                            },
                                        },
                                    },
                                };
                            }
                            #endregion

                            c = new Grendgine_Collada_Controller()
                            {
                                ID = $"Controller_{MakeID(t)}",
                                Skin = new Grendgine_Collada_Skin()
                                {
                                    SourceAt = $"#{geometriesDic[t].ID}",
                                    Source = new Grendgine_Collada_Source[]
                                    {
                                        Joints_Source,
                                        Weights_Source,
                                        Inv_Bind_Mats_Source,
                                    },
                                    Joints = new Grendgine_Collada_Joints()
                                    {
                                        Input = new Grendgine_Collada_Input_Unshared[]
                                        {
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.JOINT,
                                            source = $"#{Joints_Source.ID}",
                                        },
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.INV_BIND_MATRIX,
                                            source = $"#{Inv_Bind_Mats_Source.ID}",
                                        },
                                        },
                                    },
                                    Vertex_Weights = new Grendgine_Collada_Vertex_Weights()
                                    {
                                        Count = (uint)boneWeights.Length,
                                        Input = new Grendgine_Collada_Input_Shared[]
                                        {
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.JOINT,
                                            source = $"#{Joints_Source.ID}",
                                            Offset = 0,
                                            Set = 0,
                                        },
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.WEIGHT,
                                            source = $"#{Weights_Source.ID}",
                                            Offset = 1,
                                            Set = 0,
                                        },
                                        },
                                        VCount = new Grendgine_Collada_Int_Array_String()
                                        {
                                            Value_As_String = weightsVCountString.ToString(),
                                        },
                                        V = new Grendgine_Collada_Int_Array_String()
                                        {
                                            Value_As_String = weightsVString.ToString(),
                                        },
                                    },
                                },
                            };
                            #endregion
                        }
                        else if (mesh != null && materials != null)
                        {
                            #region MeshRenderer
                            var vertexCount = mesh.vertexCount;
                            const int boneCount = 1;

                            #region Joints_Source
                            Grendgine_Collada_Source Joints_Source;
                            {
                                var Joints_Name_Array = new Grendgine_Collada_Name_Array()
                                {
                                    ID = $"Joints_Name_Array_{MakeID(t)}",
                                    Count = boneCount,
                                };
                                {
                                    var names = new StringBuilder();
                                    {
                                        names.AppendFormat(numberFormatInfo, "\n{0}", jointsDic[t].ID);
                                    }
                                    Joints_Name_Array.Value_Pre_Parse = names.ToString();
                                }
                                Joints_Source = new Grendgine_Collada_Source()
                                {
                                    ID = $"Joints_{MakeID(t)}",
                                    Name_Array = Joints_Name_Array,
                                    Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                    {
                                        Accessor = new Grendgine_Collada_Accessor()
                                        {
                                            Count = (uint)Joints_Name_Array.Count,
                                            Source = $"#{Joints_Name_Array.ID}",
                                            Param = new Grendgine_Collada_Param[]
                                            {
                                                new()
                                                {
                                                    Type = "name",
                                                },
                                            },
                                        },
                                    },
                                };
                            }
                            #endregion
                            #region Weights_Source
                            Grendgine_Collada_Source Weights_Source;
                            StringBuilder weightsVCountString = new();
                            StringBuilder weightsVString = new();
                            List<float> weightList = new();
                            {
                                var Weights_Float_Array = new Grendgine_Collada_Float_Array()
                                {
                                    ID = $"Weights_Float_Array_{MakeID(t)}",
                                };
                                {
                                    const float weight0 = 1f;
                                    const int index0 = 0;

                                    var sb = new StringBuilder();
                                    for (int i = 0; i < vertexCount; i++)
                                    {
                                        int count = 0;
                                        {
                                            if (!weightList.Contains(weight0))
                                            {
                                                weightList.Add(weight0);
                                                sb.AppendFormat(numberFormatInfo, "\n{0}", weight0);
                                            }
                                            weightsVString.AppendFormat(numberFormatInfo, "\n{0} {1}", 0, index0);
                                            count++;
                                        }
                                        weightsVCountString.AppendFormat(numberFormatInfo, "\n{0}", count);
                                    }
                                    Weights_Float_Array.Count = weightList.Count;
                                    Weights_Float_Array.Value_As_String = sb.ToString();
                                }
                                Weights_Source = new Grendgine_Collada_Source()
                                {
                                    ID = $"Weights_{MakeID(t)}",
                                    Float_Array = Weights_Float_Array,
                                    Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                    {
                                        Accessor = new Grendgine_Collada_Accessor()
                                        {
                                            Count = (uint)Weights_Float_Array.Count,
                                            Source = $"#{Weights_Float_Array.ID}",
                                            Param = new Grendgine_Collada_Param[]
                                            {
                                                new()
                                                {
                                                    Type = "float",
                                                },
                                            },
                                        },
                                    },
                                };
                            }
                            #endregion
                            #region Inv_Bind_Mats_Source
                            Grendgine_Collada_Source Inv_Bind_Mats_Source;
                            {
                                var Inv_Bind_Mats_Float_Array = new Grendgine_Collada_Float_Array()
                                {
                                    ID = $"Inv_Bind_Mats_{MakeID(t)}",
                                };
                                {
                                    var sb = new StringBuilder();
                                    for (int i = 0; i < boneCount; i++)
                                    {
                                        Matrix4x4 mat = Matrix4x4.identity;
                                        for (int r = 0; r < 4; r++)
                                            sb.AppendFormat(numberFormatInfo, "\n{0} {1} {2} {3}", mat[r, 0], mat[r, 1], mat[r, 2], mat[r, 3]);
                                    }
                                    Inv_Bind_Mats_Float_Array.Count = boneCount * 16;
                                    Inv_Bind_Mats_Float_Array.Value_As_String = sb.ToString();
                                }
                                Inv_Bind_Mats_Source = new Grendgine_Collada_Source()
                                {
                                    ID = $"Inv_Bind_Mats_{MakeID(t)}",
                                    Float_Array = Inv_Bind_Mats_Float_Array,
                                    Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                    {
                                        Accessor = new Grendgine_Collada_Accessor()
                                        {
                                            Count = (uint)(Inv_Bind_Mats_Float_Array.Count / 16),
                                            Source = $"#{Inv_Bind_Mats_Float_Array.ID}",
                                            Stride = 16,
                                            Param = new Grendgine_Collada_Param[]
                                            {
                                                new()
                                                {
                                                    Type = "float4x4",
                                                },
                                            },
                                        },
                                    },
                                };
                            }
                            #endregion

                            c = new Grendgine_Collada_Controller()
                            {
                                ID = $"Controller_{MakeID(t)}",
                                Skin = new Grendgine_Collada_Skin()
                                {
                                    SourceAt = $"#{geometriesDic[t].ID}",
                                    Source = new Grendgine_Collada_Source[]
                                    {
                                        Joints_Source,
                                        Weights_Source,
                                        Inv_Bind_Mats_Source,
                                    },
                                    Joints = new Grendgine_Collada_Joints()
                                    {
                                        Input = new Grendgine_Collada_Input_Unshared[]
                                        {
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.JOINT,
                                            source = $"#{Joints_Source.ID}",
                                        },
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.INV_BIND_MATRIX,
                                            source = $"#{Inv_Bind_Mats_Source.ID}",
                                        },
                                        },
                                    },
                                    Vertex_Weights = new Grendgine_Collada_Vertex_Weights()
                                    {
                                        Count = (uint)vertexCount,
                                        Input = new Grendgine_Collada_Input_Shared[]
                                        {
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.JOINT,
                                            source = $"#{Joints_Source.ID}",
                                            Offset = 0,
                                            Set = 0,
                                        },
                                        new()
                                        {
                                            Semantic = Grendgine_Collada_Input_Semantic.WEIGHT,
                                            source = $"#{Weights_Source.ID}",
                                            Offset = 1,
                                            Set = 0,
                                        },
                                        },
                                        VCount = new Grendgine_Collada_Int_Array_String()
                                        {
                                            Value_As_String = weightsVCountString.ToString(),
                                        },
                                        V = new Grendgine_Collada_Int_Array_String()
                                        {
                                            Value_As_String = weightsVString.ToString(),
                                        },
                                    },
                                },
                            };
                            #endregion
                        }
                        else
                        {
                            continue;
                        }

                        controllersDic.Add(t, c);
                        #region Node
                        nodesDic[t].Instance_Controller = new Grendgine_Collada_Instance_Controller[]
                        {
                            new()
                            {
                                URL = $"#{c.ID}",
                                Bind_Material = nodesDic[t].Instance_Geometry[0].Bind_Material,
                            },
                        };
                        nodesDic[t].Instance_Geometry = null;
                        #endregion
                    }
                    lc.Controller = controllersDic.Values.ToArray();
                }
                #endregion

                #region Scene
                {
                    gCollada.Library_Visual_Scene = new Grendgine_Collada_Library_Visual_Scenes()
                    {
                        Visual_Scene = new Grendgine_Collada_Visual_Scene[]
                        {
                            new()
                            {
                                ID = $"Scene_{MakeID(rootObject)}",
                                Name = "Scene",
                            },
                        },
                    };
                    {
                        List<Grendgine_Collada_Node> nodes = new();
                        if (makeJoint)
                        {
                            foreach (Transform t in rootObject.transform)
                            {
                                if (jointsDic.TryGetValue(t, out var jointVal))
                                    nodes.Add(jointVal);
                            }
                            {
                                string GetUniqueName(string name)
                                {
                                    foreach (var pair in jointsDic)
                                    {
                                        if (pair.Value.Name == name)
                                            return GetUniqueName($"{name}_");
                                    }
                                    foreach (var pair in nodesDic)
                                    {
                                        if (pair.Value.Name == name)
                                            return GetUniqueName($"{name}_");
                                    }
                                    return name;
                                }

                                List<Grendgine_Collada_Node> list = new();
                                foreach (var pair in nodesDic)
                                {
                                    if (pair.Value.Instance_Geometry != null ||
                                        pair.Value.Instance_Controller != null)
                                    {
                                        var node = pair.Value;
                                        node.Name = GetUniqueName($"Mesh_{node.Name}");
                                        node.node = null;
                                        node.Matrix = new Grendgine_Collada_Matrix[]
                                        {
                                            MatrixIdentity,
                                        };
                                        list.Add(node);
                                    }
                                }
                                nodes.AddRange(list);
                            }
                        }
                        else
                        {
                            nodes.Add(nodesDic[rootObject.transform]);
                        }
                        gCollada.Library_Visual_Scene.Visual_Scene[0].Node = nodes.ToArray();
                    }
                    gCollada.Scene = new Grendgine_Collada_Scene()
                    {
                        Visual_Scene = new Grendgine_Collada_Instance_Visual_Scene()
                        {
                            URL = $"#{gCollada.Library_Visual_Scene.Visual_Scene[0].ID}",
                        },
                    };
                }
                #endregion

                #region Write
                {
                    using var writer = new StreamWriter(path);
                    var xmlSerializer = new XmlSerializer(typeof(Grendgine_Collada));
                    xmlSerializer.Serialize(writer, gCollada);
                    exportedFiles.Add(path);
                }
                if (clips != null)
                {
                    var tmpObject = GameObject.Instantiate<GameObject>(rootObject);
                    tmpObject.hideFlags |= HideFlags.HideAndDontSave;
                    tmpObject.transform.SetParent(null);
                    tmpObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    tmpObject.transform.localScale = Vector3.one;
                    #region DisableOtherBehaviors
                    AnimationCommon.DisableBehaviors(tmpObject, static comp =>
                        comp is Animator or Animation
#if VERYANIMATION_ANIMATIONRIGGING
                        || comp is VeryAnimationRigBuilder or RigBuilder or VeryAnimationRig or Rig
#endif
                    );
                    #endregion

                    Avatar tmpAvatar = null;
                    AnimationClip tmpClip = null;
                    try
                    {
                        if (tmpObject.TryGetComponent<Animator>(out var animator) && animator.isHuman && animator.avatar != null)
                        {
                            tmpAvatar = Avatar.Instantiate<Avatar>(animator.avatar);
                            tmpAvatar.hideFlags |= HideFlags.HideAndDontSave;
                            #region InitializeSettings
                            {
                                var so = new UnityEditor.SerializedObject(tmpAvatar);
                                so.FindProperty("m_Avatar.m_Human.data.m_ArmTwist").floatValue = 0f;
                                so.FindProperty("m_Avatar.m_Human.data.m_ForeArmTwist").floatValue = 0f;
                                so.FindProperty("m_Avatar.m_Human.data.m_UpperLegTwist").floatValue = 0f;
                                so.FindProperty("m_Avatar.m_Human.data.m_LegTwist").floatValue = 0f;
                                so.FindProperty("m_Avatar.m_Human.data.m_ArmStretch").floatValue = 0.0001f;   //Since it is occasionally wrong value when it is 0
                                so.FindProperty("m_Avatar.m_Human.data.m_LegStretch").floatValue = 0.0001f;   //Since it is occasionally wrong value when it is 0
                                so.FindProperty("m_Avatar.m_Human.data.m_FeetSpacing").floatValue = 0f;
                                so.FindProperty("m_HumanDescription.m_ArmTwist").floatValue = 0f;
                                so.FindProperty("m_HumanDescription.m_ForeArmTwist").floatValue = 0f;
                                so.FindProperty("m_HumanDescription.m_UpperLegTwist").floatValue = 0f;
                                so.FindProperty("m_HumanDescription.m_LegTwist").floatValue = 0f;
                                so.FindProperty("m_HumanDescription.m_ArmStretch").floatValue = 0.0001f;   //Since it is occasionally wrong value when it is 0
                                so.FindProperty("m_HumanDescription.m_LegStretch").floatValue = 0.0001f;   //Since it is occasionally wrong value when it is 0
                                so.FindProperty("m_HumanDescription.m_FeetSpacing").floatValue = 0f;
                                so.ApplyModifiedProperties();
                            }
                            #endregion
                            animator.avatar = tmpAvatar;
                        }
                        var animation = tmpObject.GetComponent<Animation>();
                        if (animator != null || animation != null)
                        {
                            string[] paths = new string[transforms.Length];
                            Transform[] tmpTransforms = new Transform[transforms.Length];
                            Dictionary<string, int> pathIndexMap = new(transforms.Length);
                            Dictionary<Transform, int> tmpTransformIndexMap = new(transforms.Length);
                            {
                                Dictionary<string, Transform> tmpTransformPathMap = new(transforms.Length);
                                void AddPathTransform(Transform t)
                                {
                                    tmpTransformPathMap[AnimationUtility.CalculateTransformPath(t, tmpObject.transform)] = t;
                                    foreach (Transform child in t)
                                    {
                                        AddPathTransform(child);
                                    }
                                }
                                AddPathTransform(tmpObject.transform);

                                for (int i = 0; i < transforms.Length; i++)
                                {
                                    var transformPath = AnimationUtility.CalculateTransformPath(transforms[i], rootObject.transform);
                                    paths[i] = transformPath;
                                    pathIndexMap[transformPath] = i;
                                    if (tmpTransformPathMap.TryGetValue(transformPath, out var tmpTransform))
                                    {
                                        tmpTransforms[i] = tmpTransform;
                                        tmpTransformIndexMap[tmpTransform] = i;
                                    }
                                }
                            }
                            foreach (var clip in clips)
                            {
                                tmpClip = AnimationClip.Instantiate<AnimationClip>(clip);
                                tmpClip.hideFlags |= HideFlags.HideAndDontSave;
                                tmpClip.name = clip.name;
                                tmpClip.wrapMode = WrapMode.Default;

                                AnimationCommon.ResetAnimationClipSettings(tmpClip);

                                AnimationUtility.SetAnimationEvents(tmpClip, new AnimationEvent[0]);

                                #region RemoveMotionCurves
                                if (HasMotionCurve(tmpClip))
                                {
                                    AnimationCommon.SetEditorCurves(tmpClip, new Dictionary<EditorCurveBinding, AnimationCurve>
                                    {
                                        [EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionT.x")] = null,
                                        [EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionT.y")] = null,
                                        [EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionT.z")] = null,
                                        [EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionQ.x")] = null,
                                        [EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionQ.y")] = null,
                                        [EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionQ.z")] = null,
                                        [EditorCurveBinding.FloatCurve("", typeof(Animator), "MotionQ.w")] = null,
                                    });
                                }
                                #endregion

                                var pathAnim = path.Insert(path.LastIndexOf(".dae", StringComparison.Ordinal), $"@{EditorCommon.GetSafeFileName(tmpClip.name)}");
                                while (exportedFiles.Contains(pathAnim))
                                {
                                    pathAnim = pathAnim.Insert(pathAnim.LastIndexOf(".dae", StringComparison.Ordinal), "_");
                                }

                                EditorUtility.DisplayProgressBar("Exporting Collada(dae) File...", Path.GetFileName(pathAnim), (progressIndex++ / (float)progressTotal));
                                #region enableTransforms
                                bool[] enableTransforms = new bool[transforms.Length];
                                {
                                    foreach (var binding in AnimationUtility.GetCurveBindings(tmpClip))
                                    {
                                        if (binding.type == typeof(Transform))
                                        {
                                            if (pathIndexMap.TryGetValue(binding.path, out var index))
                                                enableTransforms[index] = true;
                                        }
                                    }
                                    if (animator != null && animator.isHuman)
                                    {
                                        if (!animator.isInitialized)
                                            animator.Rebind();
                                        for (HumanBodyBones hi = 0; hi < HumanBodyBones.LastBone; hi++)
                                        {
                                            var t = animator.GetBoneTransform(hi);
                                            while (t != null)
                                            {
                                                if (tmpTransformIndexMap.TryGetValue(t, out var index))
                                                    enableTransforms[index] = true;
                                                t = t.parent;
                                            }
                                        }
                                    }
                                }
                                #endregion
                                #region transformCurves
                                TransformCurves[] transformCurves = new TransformCurves[tmpTransforms.Length];
                                for (int i = 0; i < tmpTransforms.Length; i++)
                                {
                                    var transformPath = paths[i];
                                    transformCurves[i] = new TransformCurves()
                                    {
                                        position = new AnimationCurve[]
                                        {
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalPosition.x")),
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalPosition.y")),
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalPosition.z")),
                                        },
                                        rotation = new AnimationCurve[]
                                        {
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalRotation.x")),
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalRotation.y")),
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalRotation.z")),
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalRotation.w")),
                                        },
                                        scale = new AnimationCurve[]
                                        {
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalScale.x")),
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalScale.y")),
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "m_LocalScale.z")),
                                        },
                                    };
                                    if (transformCurves[i].rotation[0] == null)
                                    {
                                        transformCurves[i].rotation = new AnimationCurve[]
                                        {
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "localEulerAnglesRaw.x")),
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "localEulerAnglesRaw.y")),
                                            AnimationUtility.GetEditorCurve(tmpClip, EditorCurveBinding.FloatCurve(transformPath, typeof(Transform), "localEulerAnglesRaw.z")),
                                        };
                                    }
                                }
                                #endregion
                                AnimationClipSettings animationClipSettings = AnimationUtility.GetAnimationClipSettings(tmpClip);
                                var totalTime = animationClipSettings.stopTime - animationClipSettings.startTime;
                                #region frameTimes
                                float[] frameTimes;
                                {
                                    var lastFrame = Mathf.RoundToInt(totalTime * tmpClip.frameRate);
                                    frameTimes = new float[lastFrame + 1];
                                    for (int i = 0; i <= lastFrame; i++)
                                    {
                                        var time = i * (1f / tmpClip.frameRate);
                                        frameTimes[i] = Mathf.Round(time * tmpClip.frameRate) / tmpClip.frameRate;
                                    }
                                }
                                #endregion
                                #region Transforms
                                Matrix4x4[,] tmpTransformMatrices = new Matrix4x4[tmpTransforms.Length, frameTimes.Length];
                                if (animator != null)
                                {
                                    animator.enabled = true;
                                    animator.fireEvents = false;
                                    animator.applyRootMotion = false;
                                    animator.updateMode = AnimatorUpdateMode.Normal;
                                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                                    UnityEditor.Animations.AnimatorController.SetAnimatorController(animator, null);
                                    PlayableGraph playableGraph;
                                    AnimationClipPlayable animationClipPlayable;
#if VERYANIMATION_ANIMATIONRIGGING
                                    VeryAnimationRigBuilder vaRigBuilder;
                                    RigBuilder rigBuilder = null;
#endif
                                    playableGraph = PlayableGraph.Create($"Exporter.{tmpObject.name}");
                                    try
                                    {
                                        #region BuildPlayableGraph
                                        {
                                            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

                                            animationClipPlayable = AnimationClipPlayable.Create(playableGraph, tmpClip);
                                            animationClipPlayable.SetApplyPlayableIK(false);
                                            animationClipPlayable.SetApplyFootIK(settings_iKOnFeet);
                                            Playable rootPlayable = animationClipPlayable;

#if VERYANIMATION_ANIMATIONRIGGING
                                            if (settings_animationRigging)
                                            {
                                                vaRigBuilder = tmpObject.GetComponent<VeryAnimationRigBuilder>();
                                                rigBuilder = tmpObject.GetComponent<RigBuilder>();
                                                if (vaRigBuilder != null && rigBuilder != null)
                                                {
                                                    vaRigBuilder.StartPreview();
                                                    rigBuilder.StartPreview();
                                                    rootPlayable = vaRigBuilder.BuildPreviewGraph(playableGraph, rootPlayable);
                                                    rootPlayable = rigBuilder.BuildPreviewGraph(playableGraph, rootPlayable);
                                                }
                                            }
#endif

                                            var playableOutput = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);
                                            playableOutput.SetSourcePlayable(rootPlayable);
                                        }
                                        #endregion
                                        #region ResetTransform
                                        {
                                            tmpTransforms[0].SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                                            tmpTransforms[0].localScale = Vector3.one;
                                        }
                                        for (int i = 1; i < transforms.Length; i++)
                                        {
                                            tmpTransforms[i].SetLocalPositionAndRotation(transforms[i].localPosition, transforms[i].localRotation);
                                            tmpTransforms[i].localScale = transforms[i].localScale;
                                        }
                                        #endregion
                                        for (int i = 0; i < frameTimes.Length; i++)
                                        {
                                            var frameTime = frameTimes[i];
                                            animationClipPlayable.SetTime(frameTime);
                                            if (playableGraph.IsValid())
                                            {
#if VERYANIMATION_ANIMATIONRIGGING
                                                if (rigBuilder != null)
                                                    rigBuilder.UpdatePreviewGraph(playableGraph);
#endif
                                                playableGraph.Evaluate();
                                            }
                                            for (int j = 0; j < tmpTransforms.Length; j++)
                                            {
                                                var tc = transformCurves[j];
                                                var tr = tmpTransforms[j];
                                                tmpTransformMatrices[j, i] = Matrix4x4.TRS(
                                                    tc.GetPosition(frameTime) ?? tr.localPosition,
                                                    tc.GetRotation(frameTime) ?? tr.localRotation,
                                                    tc.GetScale(frameTime)    ?? tr.localScale);
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        if (playableGraph.IsValid())
                                            playableGraph.Destroy();
                                    }
                                }
                                else if (animation != null)
                                {
                                    #region Legacy
                                    for (int i = 0; i < frameTimes.Length; i++)
                                    {
                                        var frameTime = frameTimes[i];
                                        tmpClip.SampleAnimation(tmpObject, frameTime);

                                        for (int j = 0; j < tmpTransforms.Length; j++)
                                        {
                                            var tc = transformCurves[j];
                                            var tr = tmpTransforms[j];
                                            tmpTransformMatrices[j, i] = Matrix4x4.TRS(
                                                tc.GetPosition(frameTime) ?? tr.localPosition,
                                                tc.GetRotation(frameTime) ?? tr.localRotation,
                                                tc.GetScale(frameTime)    ?? tr.localScale);
                                        }
                                    }
                                    #endregion
                                }
                                #endregion

                                #region Animations
                                var animationsDic = new Dictionary<AnimationClip, Grendgine_Collada_Animation>();
                                {
                                    var la = gCollada.Library_Animations = new Grendgine_Collada_Library_Animations()
                                    {
                                        ID = $"Animations_{MakeID(rootObject)}",
                                        Name = $"Animations_{rootObject.name}",
                                    };
                                    {
                                        List<Grendgine_Collada_Animation> animations = new();
                                        for (int j = 0; j < tmpTransforms.Length; j++)
                                        {
                                            if (!enableTransforms[j])
                                                continue;
                                            if (makeJoint)
                                            {
                                                if (!jointsDic.TryGetValue(transforms[j], out var jointNode))
                                                    continue;
                                                if (jointNode.Type != Grendgine_Collada_Node_Type.JOINT)
                                                    continue;
                                            }
                                            else
                                            {
                                                if (!nodesDic.ContainsKey(transforms[j]))
                                                    continue;
                                            }

                                            #region InputSource
                                            Grendgine_Collada_Source InputSource;
                                            {
                                                var Input_Float_Array = new Grendgine_Collada_Float_Array()
                                                {
                                                    ID = $"Input_Float_Array_{MakeID(transforms[j])}",
                                                    Count = frameTimes.Length,
                                                };
                                                {
                                                    var sb = new StringBuilder();
                                                    for (int i = 0; i < frameTimes.Length; i++)
                                                    {
                                                        sb.AppendFormat(numberFormatInfo, "\n{0}", frameTimes[i]);
                                                    }
                                                    Input_Float_Array.Value_As_String = sb.ToString();
                                                }
                                                InputSource = new Grendgine_Collada_Source()
                                                {
                                                    ID = $"InputSource_{MakeID(transforms[j])}",
                                                    Float_Array = Input_Float_Array,
                                                    Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                                    {
                                                        Accessor = new Grendgine_Collada_Accessor()
                                                        {
                                                            Count = (uint)Input_Float_Array.Count,
                                                            Source = $"#{Input_Float_Array.ID}",
                                                            Param = new Grendgine_Collada_Param[]
                                                            {
                                                                new()
                                                                {
                                                                    Name = "TIME",
                                                                    Type = "float",
                                                                },
                                                            },
                                                        },
                                                    },
                                                };
                                            }
                                            #endregion
                                            #region OutputSource
                                            Grendgine_Collada_Source OutputSource;
                                            {
                                                var Output_Float_Array = new Grendgine_Collada_Float_Array()
                                                {
                                                    ID = $"Output_Float_Array_{MakeID(transforms[j])}",
                                                    Count = frameTimes.Length * 16,
                                                };
                                                {
                                                    var sb = new StringBuilder();
                                                    for (int i = 0; i < frameTimes.Length; i++)
                                                    {
                                                        Matrix4x4 mat = tmpTransformMatrices[j, i];
                                                        {
                                                            var position = mat.GetColumn(3);
                                                            var rotation = (mat.GetColumn(2).sqrMagnitude > 0f && mat.GetColumn(1).sqrMagnitude > 0f) ? Quaternion.LookRotation(mat.GetColumn(2), mat.GetColumn(1)) : Quaternion.identity;
                                                            var scale = new Vector3(mat.GetColumn(0).magnitude, mat.GetColumn(1).magnitude, mat.GetColumn(2).magnitude);
                                                            #region Do not allow scale zero
                                                            {
                                                                for (int si = 0; si < 3; si++)
                                                                {
                                                                    if (scale[si] == 0f)
                                                                        scale[si] = Mathf.Epsilon;
                                                                }
                                                            }
                                                            #endregion
                                                            mat = Matrix4x4.TRS(matMirrorX.MultiplyPoint3x4(position),
                                                                                new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w), //mirrorX
                                                                                scale);
                                                        }
                                                        for (int r = 0; r < 4; r++)
                                                            sb.AppendFormat(numberFormatInfo, "\n{0} {1} {2} {3}", mat[r, 0], mat[r, 1], mat[r, 2], mat[r, 3]);
                                                    }
                                                    Output_Float_Array.Value_As_String = sb.ToString();
                                                }
                                                OutputSource = new Grendgine_Collada_Source()
                                                {
                                                    ID = $"OutputSource_{MakeID(transforms[j])}",
                                                    Float_Array = Output_Float_Array,
                                                    Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                                    {
                                                        Accessor = new Grendgine_Collada_Accessor()
                                                        {
                                                            Count = (uint)(Output_Float_Array.Count / 16),
                                                            Source = $"#{Output_Float_Array.ID}",
                                                            Stride = 16,
                                                            Param = new Grendgine_Collada_Param[]
                                                            {
                                                                new()
                                                                {
                                                                    Name = "TRANSFORM",
                                                                    Type = "float4x4",
                                                                },
                                                            }
                                                        },
                                                    },
                                                };
                                            }
                                            #endregion
                                            #region InterpolationSource
                                            Grendgine_Collada_Source InterpolationSource;
                                            {
                                                var Interpolation_Name_Array = new Grendgine_Collada_Name_Array()
                                                {
                                                    ID = $"Interpolation_Name_Array{MakeID(transforms[j])}",
                                                    Count = frameTimes.Length,
                                                };
                                                {
                                                    var sb = new StringBuilder();
                                                    for (int i = 0; i < frameTimes.Length; i++)
                                                    {
                                                        sb.Append("\nLINEAR");
                                                    }
                                                    Interpolation_Name_Array.Value_Pre_Parse = sb.ToString();
                                                }
                                                InterpolationSource = new Grendgine_Collada_Source()
                                                {
                                                    ID = $"InterpolationSource_{MakeID(transforms[j])}",
                                                    Name_Array = Interpolation_Name_Array,
                                                    Technique_Common = new Grendgine_Collada_Technique_Common_Source()
                                                    {
                                                        Accessor = new Grendgine_Collada_Accessor()
                                                        {
                                                            Count = (uint)Interpolation_Name_Array.Count,
                                                            Source = $"#{Interpolation_Name_Array.ID}",
                                                            Param = new Grendgine_Collada_Param[]
                                                            {
                                                                new()
                                                                {
                                                                    Name = "INTERPOLATION",
                                                                    Type = "name",
                                                                },
                                                            },
                                                        },
                                                    },
                                                };
                                            }
                                            #endregion
                                            #region Sampler
                                            var Sampler = new Grendgine_Collada_Sampler[]
                                            {
                                                new()
                                                {
                                                    ID = $"Sampler_{MakeID(transforms[j])}",
                                                    Input = new Grendgine_Collada_Input_Unshared[]
                                                    {
                                                        new()
                                                        {
                                                            Semantic = Grendgine_Collada_Input_Semantic.INPUT,
                                                            source = $"#{InputSource.ID}",
                                                        },
                                                        new()
                                                        {
                                                            Semantic = Grendgine_Collada_Input_Semantic.OUTPUT,
                                                            source = $"#{OutputSource.ID}",
                                                        },
                                                        new()
                                                        {
                                                            Semantic = Grendgine_Collada_Input_Semantic.INTERPOLATION,
                                                            source = $"#{InterpolationSource.ID}",
                                                        },
                                                    },
                                                },
                                            };
                                            #endregion
                                            var a = new Grendgine_Collada_Animation()
                                            {
                                                ID = $"Animation_{MakeID(transforms[j])}",
                                                Source = new Grendgine_Collada_Source[]
                                                {
                                                    InputSource,
                                                    OutputSource,
                                                    InterpolationSource,
                                                },
                                                Sampler = Sampler,
                                                Channel = new Grendgine_Collada_Channel[]
                                                {
                                                    new()
                                                    {
                                                        Source = $"#{Sampler[0].ID}",
                                                        Target = $"{(makeJoint ? jointsDic[transforms[j]].sID : nodesDic[transforms[j]].sID)}/transform",
                                                    },
                                                },
                                            };
                                            animations.Add(a);
                                        }
                                        var ra = new Grendgine_Collada_Animation()
                                        {
                                            ID = $"Animation_{MakeID(tmpClip)}",
                                            Name = $"Animation_{tmpClip.name}",
                                            Animation = animations.ToArray(),
                                        };
                                        animationsDic.Add(tmpClip, ra);
                                    }
                                    la.Animation = animationsDic.Values.ToArray();
                                }
                                #endregion

                                #region AnimationClips
                                var animationClipsDic = new Dictionary<AnimationClip, Grendgine_Collada_Animation_Clip>();
                                {
                                    var la = gCollada.Library_Animation_Clips = new Grendgine_Collada_Library_Animation_Clips()
                                    {
                                        ID = $"Animation_Clips_{MakeID(rootObject)}",
                                        Name = $"Animation_Clips_{rootObject.name}",
                                    };
                                    {
                                        var ac = new Grendgine_Collada_Animation_Clip()
                                        {
                                            ID = $"Animation_Clips_{MakeID(tmpClip)}",
                                            Name = tmpClip.name,
                                            Start = 0f,
                                            End = totalTime,
                                            Instance_Animation = new Grendgine_Collada_Instance_Animation[1]
                                            {
                                                new()
                                                {
                                                    URL = $"#{animationsDic[tmpClip].ID}",
                                                },
                                            },
                                        };
                                        animationClipsDic.Add(tmpClip, ac);
                                    }
                                    la.Animation_Clip = animationClipsDic.Values.ToArray();
                                }
                                #endregion

                                #region Scene
                                {
                                    var Doc = new System.Xml.XmlDocument();
                                    var frame_rate = Doc.CreateElement("frame_rate");
                                    {
                                        frame_rate.InnerText = tmpClip.frameRate.ToString(numberFormatInfo);
                                    }
                                    var start_time = Doc.CreateElement("start_time");
                                    {
                                        start_time.InnerText = 0f.ToString(numberFormatInfo);
                                    }
                                    var end_time = Doc.CreateElement("end_time");
                                    {
                                        end_time.InnerText = totalTime.ToString(numberFormatInfo);
                                    }
                                    gCollada.Library_Visual_Scene.Visual_Scene[0].Extra = new Grendgine_Collada_Extra[]
                                    {
                                        new()
                                        {
                                            Technique = new Grendgine_Collada_Technique[]
                                            {
                                                new()
                                                {
                                                    profile = "MAX3D",
                                                    Data = new System.Xml.XmlElement[]
                                                    {
                                                        frame_rate,
                                                    },
                                                },
                                                new()
                                                {
                                                    profile = "FCOLLADA",
                                                    Data = new System.Xml.XmlElement[]
                                                    {
                                                        start_time,
                                                        end_time,
                                                    },
                                                },
                                            },
                                        },
                                    };
                                }
                                #endregion

                                {
                                    using var writer = new StreamWriter(pathAnim);
                                    var xmlSerializer = new XmlSerializer(typeof(Grendgine_Collada));
                                    xmlSerializer.Serialize(writer, gCollada);
                                }
                                exportedFiles.Add(pathAnim);
                                sourceObjects.Add(pathAnim, clip);

                                AnimationClip.DestroyImmediate(tmpClip);
                                tmpClip = null;
                            }
                        }
                    }
                    finally
                    {
                        GameObject.DestroyImmediate(tmpObject);
                        if (tmpAvatar != null)
                            Avatar.DestroyImmediate(tmpAvatar);
                        if (tmpClip != null)
                            AnimationClip.DestroyImmediate(tmpClip);
                    }
                }
                #endregion

                #region ImporterSettings
                if (settings_applyImporterSettings)
                {
                    Avatar sourceAvatar = null;
                    HumanDescription sourceHumanDescription = new();
                    for (int fileIndex = 0; fileIndex < exportedFiles.Count; fileIndex++)
                    {
                        var p = exportedFiles[fileIndex];
                        if (!p.StartsWith(Application.dataPath, StringComparison.Ordinal)) continue;
                        if (!File.Exists(p)) continue;
                        var assetPath = FileUtil.GetProjectRelativePath(p);
                        AssetDatabase.ImportAsset(assetPath);
                        var importer = AssetImporter.GetAtPath(assetPath);
                        if (importer is ModelImporter modelImporter)
                        {
                            #region ModelImporter
                            modelImporter.animationType = settings_animationType;
                            if (settings_animationType == ModelImporterAnimationType.Generic || settings_animationType == ModelImporterAnimationType.Human)
                            {
                                modelImporter.sourceAvatar = sourceAvatar;
                                modelImporter.humanDescription = sourceHumanDescription;
                            }
                            if (clips != null)
                            {
                                if (sourceObjects.TryGetValue(p, out var sourceObj))
                                {
                                    var sourceClip = sourceObj as AnimationClip;
                                    #region Event and ClipSettings
                                    {
                                        AnimationEvent[] events = new AnimationEvent[sourceClip.events.Length];
                                        {
                                            for (int i = 0; i < sourceClip.events.Length; i++)
                                            {
                                                var src = sourceClip.events[i];
                                                events[i] = new AnimationEvent()
                                                {
                                                    stringParameter = src.stringParameter,
                                                    floatParameter = src.floatParameter,
                                                    intParameter = src.intParameter,
                                                    objectReferenceParameter = src.objectReferenceParameter,
                                                    functionName = src.functionName,
                                                    time = src.time / sourceClip.length,       //It seems that this is not the time but the proportion of the whole
                                                    messageOptions = src.messageOptions,
                                                };
                                            }
                                        }
                                        var settings = AnimationUtility.GetAnimationClipSettings(sourceClip);
                                        var hasMotionCurve = HasMotionCurve(sourceClip);
                                        var setClips = modelImporter.defaultClipAnimations;
                                        foreach (var setClip in setClips)
                                        {
                                            setClip.name = sourceClip.name;
                                            setClip.wrapMode = sourceClip.wrapMode;
                                            setClip.events = events;

                                            setClip.loopTime = settings.loopTime;
                                            setClip.loopPose = settings.loopBlend;
                                            setClip.cycleOffset = settings.cycleOffset;
                                            setClip.heightFromFeet = !hasMotionCurve && settings.heightFromFeet;
                                            setClip.keepOriginalPositionXZ = hasMotionCurve || settings.keepOriginalPositionXZ;
                                            setClip.keepOriginalPositionY = hasMotionCurve || settings.keepOriginalPositionY;
                                            setClip.keepOriginalOrientation = hasMotionCurve || settings.keepOriginalOrientation;
                                            setClip.lockRootPositionXZ = hasMotionCurve || settings.loopBlendPositionXZ;
                                            setClip.lockRootHeightY = hasMotionCurve || settings.loopBlendPositionY;
                                            setClip.lockRootRotation = hasMotionCurve || settings.loopBlendOrientation;
                                            setClip.heightOffset = !hasMotionCurve ? settings.level : 0f;
                                            setClip.rotationOffset = !hasMotionCurve ? settings.orientationOffsetY : 0f;
                                            setClip.mirror = settings.mirror;
                                        }
                                        modelImporter.clipAnimations = setClips;
                                    }
                                    #endregion
                                    #region AvatarMask
                                    if (modelImporter.animationType == ModelImporterAnimationType.Human)
                                    {
                                        var avatarMask = new AvatarMask();
                                        avatarMask.hideFlags |= HideFlags.HideAndDontSave;
                                        {
                                            HashSet<string> transformPathSet = new(modelImporter.transformPaths);
                                            HashSet<string> addPaths = new();
                                            foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
                                            {
                                                if (binding.type != typeof(Transform))
                                                    continue;
                                                if (!transformPathSet.Contains(binding.path))
                                                    continue;
                                                addPaths.Add(binding.path);
                                            }
                                            if (addPaths.Count > 0)
                                            {
                                                avatarMask.transformCount = addPaths.Count;
                                                int i = 0;
                                                foreach (var transformPath in addPaths)
                                                {
                                                    avatarMask.SetTransformPath(i, transformPath);
                                                    avatarMask.SetTransformActive(i, true);
                                                    i++;
                                                }
                                            }
                                        }
                                        SerializedObject so = new(modelImporter);
                                        SerializedProperty spClips = so.FindProperty("m_ClipAnimations");
                                        for (int i = 0; i < spClips.arraySize; i++)
                                        {
                                            var spTransformMask = spClips.GetArrayElementAtIndex(i).FindPropertyRelative("transformMask");
                                            UpdateTransformMask(avatarMask, spTransformMask);
                                        }
                                        so.ApplyModifiedProperties();
                                        AvatarMask.DestroyImmediate(avatarMask);
                                    }
                                    #endregion
                                }
                            }
                            #region RootNode
                            if (modelImporter.animationType == ModelImporterAnimationType.Generic && !string.IsNullOrEmpty(settings_motionNodePath))
                            {
                                //Do not use modelImporter.motionNodeName
                                var so = new SerializedObject(modelImporter);
                                var sp = so.FindProperty("m_HumanDescription.m_RootMotionBoneName");
                                var splits = settings_motionNodePath.Split('/');
                                sp.stringValue = splits[^1];
                                so.ApplyModifiedProperties();
                            }
                            #endregion
                            modelImporter.SaveAndReimport();
                            if ((settings_animationType == ModelImporterAnimationType.Generic || settings_animationType == ModelImporterAnimationType.Human) &&
                                sourceAvatar == null)
                            {
                                if (settings_animationType == ModelImporterAnimationType.Human && settings_avatar != null)
                                {
                                    var so = new UnityEditor.SerializedObject(settings_avatar);
                                    var hd = modelImporter.humanDescription;
                                    var humanBoneIndexArray = so.FindProperty("m_Avatar.m_Human.data.m_HumanBoneIndex");
                                    var leftHandBoneIndexArray = so.FindProperty("m_Avatar.m_Human.data.m_LeftHand.data.m_HandBoneIndex");
                                    var rightHandBoneIndexArray = so.FindProperty("m_Avatar.m_Human.data.m_RightHand.data.m_HandBoneIndex");
                                    var skeletonIdArray = so.FindProperty("m_Avatar.m_Human.data.m_Skeleton.data.m_ID");
                                    var skeletonNodeArray = so.FindProperty("m_Avatar.m_Human.data.m_Skeleton.data.m_Node");
                                    var skeletonAxesArray = so.FindProperty("m_Avatar.m_Human.data.m_Skeleton.data.m_AxesArray");
                                    var tosArray = so.FindProperty("m_TOS");
                                    var avatarSkeletonPoseArray = so.FindProperty("m_Avatar.m_AvatarSkeletonPose.data.m_X");
                                    var avatarSkeletonIdArray = so.FindProperty("m_Avatar.m_AvatarSkeleton.data.m_ID");
                                    var armTwistProperty = so.FindProperty("m_Avatar.m_Human.data.m_ArmTwist");
                                    var foreArmTwistProperty = so.FindProperty("m_Avatar.m_Human.data.m_ForeArmTwist");
                                    var upperLegTwistProperty = so.FindProperty("m_Avatar.m_Human.data.m_UpperLegTwist");
                                    var legTwistProperty = so.FindProperty("m_Avatar.m_Human.data.m_LegTwist");
                                    var armStretchProperty = so.FindProperty("m_Avatar.m_Human.data.m_ArmStretch");
                                    var legStretchProperty = so.FindProperty("m_Avatar.m_Human.data.m_LegStretch");
                                    var feetSpacingProperty = so.FindProperty("m_Avatar.m_Human.data.m_FeetSpacing");
                                    var hasTranslationDoFProperty = so.FindProperty("m_Avatar.m_Human.data.m_HasTDoF");
                                    Dictionary<long, string> tosIdPaths = null;
                                    Dictionary<long, string> tosBoneNames = null;
                                    if (tosArray != null && tosArray.isArray)
                                    {
                                        int tosSize = tosArray.arraySize;
                                        tosIdPaths = new Dictionary<long, string>(tosSize);
                                        tosBoneNames = new Dictionary<long, string>(tosSize);
                                        for (int i = 0; i < tosSize; i++)
                                        {
                                            var pElement = tosArray.GetArrayElementAtIndex(i);
                                            if (pElement == null) continue;
                                            var pFirst = pElement.FindPropertyRelative("first");
                                            var pSecond = pElement.FindPropertyRelative("second");
                                            if (pFirst == null || pSecond == null) continue;
                                            var tosPath = pSecond.stringValue;
                                            tosIdPaths[pFirst.longValue] = tosPath;

                                            var index = tosPath.LastIndexOf('/');
                                            if (index >= 0)
                                                tosPath = tosPath[(index + 1)..];
                                            tosBoneNames[pFirst.longValue] = tosPath;
                                        }
                                    }
                                    {
                                        List<HumanBone> humanBones = new();
                                        for (HumanBodyBones humanoidIndex = 0; humanoidIndex < HumanBodyBones.LastBone; humanoidIndex++)
                                        {
                                            int skeletonIndex = -1;
                                            {
                                                if (humanoidIndex <= HumanBodyBones.Jaw || humanoidIndex == HumanBodyBones.UpperChest)
                                                {
                                                    int humanId = -1;
                                                    if (humanoidIndex <= HumanBodyBones.Chest) humanId = (int)humanoidIndex;
                                                    else if (humanoidIndex <= HumanBodyBones.Jaw) humanId = (int)humanoidIndex + 1;
                                                    else humanId = 9;
                                                    if (humanBoneIndexArray == null || !humanBoneIndexArray.isArray || humanId < 0 || humanId >= humanBoneIndexArray.arraySize)
                                                        continue;
                                                    skeletonIndex = humanBoneIndexArray.GetArrayElementAtIndex(humanId).intValue;
                                                }
                                                else if (humanoidIndex <= HumanBodyBones.LeftLittleDistal)
                                                {
                                                    int handId = (int)humanoidIndex - (int)HumanBodyBones.LeftThumbProximal;
                                                    if (leftHandBoneIndexArray == null || !leftHandBoneIndexArray.isArray || handId < 0 || handId >= leftHandBoneIndexArray.arraySize)
                                                        continue;
                                                    skeletonIndex = leftHandBoneIndexArray.GetArrayElementAtIndex(handId).intValue;
                                                }
                                                else if (humanoidIndex <= HumanBodyBones.RightLittleDistal)
                                                {
                                                    int handId = (int)humanoidIndex - (int)HumanBodyBones.RightThumbProximal;
                                                    if (rightHandBoneIndexArray == null || !rightHandBoneIndexArray.isArray || handId < 0 || handId >= rightHandBoneIndexArray.arraySize)
                                                        continue;
                                                    skeletonIndex = rightHandBoneIndexArray.GetArrayElementAtIndex(handId).intValue;
                                                }
                                                if (skeletonIndex < 0)
                                                    continue;
                                            }
                                            string boneName = null;
                                            {
                                                if (skeletonIdArray == null || !skeletonIdArray.isArray || skeletonIndex < 0 || skeletonIndex >= skeletonIdArray.arraySize)
                                                    continue;
                                                var pID = skeletonIdArray.GetArrayElementAtIndex(skeletonIndex);
                                                if (pID == null)
                                                    continue;
                                                var id = pID.longValue;
                                                if (tosBoneNames == null || !tosBoneNames.TryGetValue(id, out boneName) || string.IsNullOrEmpty(boneName))
                                                    continue;
                                            }
                                            Vector3 min, max, center;
                                            float length;
                                            {
                                                if (skeletonNodeArray == null || !skeletonNodeArray.isArray || skeletonIndex < 0 || skeletonIndex >= skeletonNodeArray.arraySize)
                                                    continue;
                                                var pNode = skeletonNodeArray.GetArrayElementAtIndex(skeletonIndex);
                                                if (pNode == null)
                                                    continue;
                                                var axesId = pNode.FindPropertyRelative("m_AxesId").intValue;
                                                if (axesId < 0)
                                                    continue;
                                                if (skeletonAxesArray == null || !skeletonAxesArray.isArray || axesId < 0 || axesId >= skeletonAxesArray.arraySize)
                                                    continue;
                                                var pAxes = skeletonAxesArray.GetArrayElementAtIndex(axesId);
                                                if (pAxes == null)
                                                    continue;
                                                min = new Vector3(pAxes.FindPropertyRelative("m_Limit.m_Min.x").floatValue,
                                                                    pAxes.FindPropertyRelative("m_Limit.m_Min.y").floatValue,
                                                                    pAxes.FindPropertyRelative("m_Limit.m_Min.z").floatValue) * Mathf.Rad2Deg;
                                                max = new Vector3(pAxes.FindPropertyRelative("m_Limit.m_Max.x").floatValue,
                                                                    pAxes.FindPropertyRelative("m_Limit.m_Max.y").floatValue,
                                                                    pAxes.FindPropertyRelative("m_Limit.m_Max.z").floatValue) * Mathf.Rad2Deg;
                                                center = new Vector3(pAxes.FindPropertyRelative("m_Sgn.x").floatValue,
                                                                        pAxes.FindPropertyRelative("m_Sgn.y").floatValue,
                                                                        pAxes.FindPropertyRelative("m_Sgn.z").floatValue);
                                                length = pAxes.FindPropertyRelative("m_Length").floatValue;
                                            }
                                            var humanBone = new HumanBone()
                                            {
                                                limit = new HumanLimit()
                                                {
                                                    useDefaultValues = false,
                                                    min = min,
                                                    max = max,
                                                    center = center,
                                                    axisLength = length,
                                                },
                                                boneName = boneName,
                                                humanName = HumanTrait.BoneName[(int)humanoidIndex],
                                            };
                                            humanBones.Add(humanBone);
                                        }
                                        hd.human = humanBones.ToArray();
                                    }
                                    {
                                        int skeletonPoseSize = 0;
                                        if (avatarSkeletonPoseArray != null && avatarSkeletonPoseArray.isArray &&
                                            avatarSkeletonIdArray != null && avatarSkeletonIdArray.isArray)
                                        {
                                            skeletonPoseSize = Mathf.Min(avatarSkeletonPoseArray.arraySize, avatarSkeletonIdArray.arraySize);
                                        }
                                        hd.skeleton = new SkeletonBone[skeletonPoseSize];
                                        for (int i = 0; i < skeletonPoseSize; i++)
                                        {
                                            var pData = avatarSkeletonPoseArray.GetArrayElementAtIndex(i);
                                            if (pData == null) continue;
                                            var position = new Vector3(pData.FindPropertyRelative("t.x").floatValue,
                                                                        pData.FindPropertyRelative("t.y").floatValue,
                                                                        pData.FindPropertyRelative("t.z").floatValue);
                                            var rotation = new Quaternion(pData.FindPropertyRelative("q.x").floatValue,
                                                                            pData.FindPropertyRelative("q.y").floatValue,
                                                                            pData.FindPropertyRelative("q.z").floatValue,
                                                                            pData.FindPropertyRelative("q.w").floatValue);
                                            var scale = new Vector3(pData.FindPropertyRelative("s.x").floatValue,
                                                                        pData.FindPropertyRelative("s.y").floatValue,
                                                                        pData.FindPropertyRelative("s.z").floatValue);
                                            if (tosIdPaths == null ||
                                                !tosIdPaths.TryGetValue(avatarSkeletonIdArray.GetArrayElementAtIndex(i).longValue, out string bpath))
                                                continue;
                                            {
                                                var index = bpath.LastIndexOf('/');
                                                if (index >= 0)
                                                    bpath = bpath[(index + 1)..];
                                            }
                                            hd.skeleton[i] = new SkeletonBone()
                                            {
                                                name = bpath,
                                                position = position,
                                                rotation = rotation,
                                                scale = scale,
                                            };
                                        }
                                    }
                                    hd.upperArmTwist = armTwistProperty.floatValue;
                                    hd.lowerArmTwist = foreArmTwistProperty.floatValue;
                                    hd.upperLegTwist = upperLegTwistProperty.floatValue;
                                    hd.lowerLegTwist = legTwistProperty.floatValue;
                                    hd.armStretch = armStretchProperty.floatValue;
                                    hd.legStretch = legStretchProperty.floatValue;
                                    hd.feetSpacing = feetSpacingProperty.floatValue;
                                    hd.hasTranslationDoF = hasTranslationDoFProperty.boolValue;
                                    modelImporter.humanDescription = hd;
                                    modelImporter.SaveAndReimport();
                                }
                                sourceAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(assetPath);
                                sourceHumanDescription = modelImporter.humanDescription;
                            }
                            #endregion
                        }
                        else if (importer is TextureImporter texImporter)
                        {
                            #region TextureImporter
                            var sourceTexture = sourceObjects[p] as Texture;
                            {
                                var srcTexImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sourceTexture)) as TextureImporter;
                                if (srcTexImporter == null)
                                {
                                    texImporter.mipMapBias = sourceTexture.mipMapBias;
                                    texImporter.wrapMode = sourceTexture.wrapMode;
                                    texImporter.filterMode = sourceTexture.filterMode;
                                    texImporter.anisoLevel = sourceTexture.anisoLevel;
                                    texImporter.wrapModeV = sourceTexture.wrapModeV;
                                    texImporter.wrapModeU = sourceTexture.wrapModeU;
                                    texImporter.wrapModeW = sourceTexture.wrapModeW;
                                }
                                else
                                {
                                    TextureImporterSettings settings = new();
                                    srcTexImporter.ReadTextureSettings(settings);
                                    texImporter.SetTextureSettings(settings);
                                }
                            }
                            texImporter.SaveAndReimport();
                            #endregion
                        }
                    }
                    AssetDatabase.Refresh();
                }
                #endregion
            }
            finally
            {
                #region TransformSave
                foreach (var item in transformsSave)
                {
                    item.Value.Load(item.Key);
                }
                #endregion

                EditorUtility.ClearProgressBar();
            }
            return true;
        }

        #region Settings
        public bool settings_activeOnly = true;
        public bool settings_exportMesh = true;
        public bool settings_iKOnFeet = true;
        public bool settings_animationRigging = false;

        public ModelImporterAnimationType settings_animationType;
        public Avatar settings_avatar;
        public string settings_motionNodePath;

        public bool settings_applyImporterSettings = true;
        #endregion

        public List<string> exportedFiles = new();

        private class TransformSave
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;

            public TransformSave(Transform t)
            {
                t.GetLocalPositionAndRotation(out localPosition, out localRotation);
                localScale = t.localScale;
            }
            public void Load(Transform t)
            {
                t.SetLocalPositionAndRotation(localPosition, localRotation);
                t.localScale = localScale;
            }
        }
        private class TransformCurves
        {
            public AnimationCurve[] position;
            public AnimationCurve[] rotation;
            public AnimationCurve[] scale;

            public Vector3? GetPosition(float time)
            {
                Vector3 result = Vector3.zero;
                int count = 0;
                for (int i = 0; i < position.Length; i++)
                {
                    if (position[i] == null) continue;
                    result[i] = position[i].Evaluate(time);
                    count++;
                }
                if (count == 3) return result;
                else return null;
            }
            public Quaternion? GetRotation(float time)
            {
                if (rotation.Length == 3)
                {
                    Vector3 result = Vector3.zero;
                    int count = 0;
                    for (int i = 0; i < rotation.Length; i++)
                    {
                        if (rotation[i] == null) continue;
                        result[i] = rotation[i].Evaluate(time);
                        count++;
                    }
                    if (count == 3) return Quaternion.Euler(result);
                    else return null;
                }
                else
                {
                    Vector4 result = new(0, 0, 0, 1);
                    int count = 0;
                    for (int i = 0; i < rotation.Length; i++)
                    {
                        if (rotation[i] == null) continue;
                        result[i] = rotation[i].Evaluate(time);
                        count++;
                    }
                    if (count == 4 && result.sqrMagnitude > 0)
                    {
                        result.Normalize();
                        return new Quaternion(result[0], result[1], result[2], result[3]);
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            public Vector3? GetScale(float time)
            {
                Vector3 result = Vector3.one;
                int count = 0;
                for (int i = 0; i < scale.Length; i++)
                {
                    if (scale[i] == null) continue;
                    result[i] = scale[i].Evaluate(time);
                    count++;
                }
                if (count == 3) return result;
                else return null;
            }
        }

        #region Reflection
        private static MethodInfo mi_UpdateTransformMask;
        private static MethodInfo mi_HasMotionCurves;
        private static void UpdateTransformMask(AvatarMask avatarMask, SerializedProperty spTransformMask)
        {
            mi_UpdateTransformMask ??= typeof(ModelImporter).GetMethod("UpdateTransformMask", BindingFlags.NonPublic | BindingFlags.Static);
            mi_UpdateTransformMask.Invoke(null, new object[] { avatarMask, spTransformMask });
        }
        private bool HasMotionCurve(AnimationClip clip)
        {
            mi_HasMotionCurves ??= typeof(AnimationUtility).GetMethod("HasMotionCurves", BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)mi_HasMotionCurves.Invoke(null, new object[] { clip });
        }
        #endregion
    }
}
