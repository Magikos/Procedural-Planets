using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

namespace VeryAnimation.grendgine_collada
{

    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "COLLADA", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = false)]
    public partial class Grendgine_Collada
    {

        [XmlAttribute("version")]
        public string Collada_Version { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }


        [XmlElement(ElementName = "library_animation_clips")]
        public Grendgine_Collada_Library_Animation_Clips Library_Animation_Clips { get; set; }

        [XmlElement(ElementName = "library_animations")]
        public Grendgine_Collada_Library_Animations Library_Animations { get; set; }

        [XmlElement(ElementName = "library_articulated_systems")]
        public Grendgine_Collada_Library_Articulated_Systems Library_Articulated_Systems { get; set; }

        [XmlElement(ElementName = "library_cameras")]
        public Grendgine_Collada_Library_Cameras Library_Cameras { get; set; }

        [XmlElement(ElementName = "library_controllers")]
        public Grendgine_Collada_Library_Controllers Library_Controllers { get; set; }

        [XmlElement(ElementName = "library_effects")]
        public Grendgine_Collada_Library_Effects Library_Effects { get; set; }

        [XmlElement(ElementName = "library_force_fields")]
        public Grendgine_Collada_Library_Force_Fields Library_Force_Fields { get; set; }

        [XmlElement(ElementName = "library_formulas")]
        public Grendgine_Collada_Library_Formulas Library_Formulas { get; set; }

        [XmlElement(ElementName = "library_geometries")]
        public Grendgine_Collada_Library_Geometries Library_Geometries { get; set; }

        [XmlElement(ElementName = "library_images")]
        public Grendgine_Collada_Library_Images Library_Images { get; set; }

        [XmlElement(ElementName = "library_joints")]
        public Grendgine_Collada_Library_Joints Library_Joints { get; set; }

        [XmlElement(ElementName = "library_kinematics_models")]
        public Grendgine_Collada_Library_Kinematics_Models Library_Kinematics_Models { get; set; }

        [XmlElement(ElementName = "library_kinematics_scenes")]
        public Grendgine_Collada_Library_Kinematics_Scene Library_Kinematics_Scene { get; set; }

        [XmlElement(ElementName = "library_lights")]
        public Grendgine_Collada_Library_Lights Library_Lights { get; set; }

        [XmlElement(ElementName = "library_materials")]
        public Grendgine_Collada_Library_Materials Library_Materials { get; set; }

        [XmlElement(ElementName = "library_nodes")]
        public Grendgine_Collada_Library_Nodes Library_Nodes { get; set; }

        [XmlElement(ElementName = "library_physics_materials")]
        public Grendgine_Collada_Library_Physics_Materials Library_Physics_Materials { get; set; }

        [XmlElement(ElementName = "library_physics_models")]
        public Grendgine_Collada_Library_Physics_Models Library_Physics_Models { get; set; }

        [XmlElement(ElementName = "library_physics_scenes")]
        public Grendgine_Collada_Library_Physics_Scenes Library_Physics_Scenes { get; set; }

        [XmlElement(ElementName = "library_visual_scenes")]
        public Grendgine_Collada_Library_Visual_Scenes Library_Visual_Scene { get; set; }


        [XmlElement(ElementName = "scene")]
        public Grendgine_Collada_Scene Scene { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }

        public static Grendgine_Collada Grendgine_Load_File(string file_name)
        {
            try
            {
                Grendgine_Collada col_scenes = null;

                XmlSerializer sr = new(typeof(Grendgine_Collada));
                TextReader tr = new StreamReader(file_name);
                col_scenes = (Grendgine_Collada)(sr.Deserialize(tr));

                tr.Close();

                return col_scenes;
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                return null;
            }
        }
    }
}

//check done