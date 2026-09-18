using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{

    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Technique_Common_Rigid_Body : Grendgine_Collada_Technique_Common
    {

        [XmlElement(ElementName = "dynamic")]
        public Grendgine_Collada_SID_Bool Dynamic { get; set; }

        [XmlElement(ElementName = "mass")]
        public Grendgine_Collada_SID_Float Mass { get; set; }

        [XmlElement(ElementName = "inertia")]
        public Grendgine_Collada_SID_Float_Array_String Inertia { get; set; }

        [XmlElement(ElementName = "mass_frame")]
        public Grendgine_Collada_Mass_Frame Mass_Frame { get; set; }


        [XmlElement(ElementName = "physics_material")]
        public Grendgine_Collada_Physics_Material Physics_Material { get; set; }

        [XmlElement(ElementName = "instance_physics_material")]
        public Grendgine_Collada_Instance_Physics_Material Instance_Physics_Material { get; set; }


        [XmlElement(ElementName = "shape")]
        public Grendgine_Collada_Shape[] Shape { get; set; }
    }
}

