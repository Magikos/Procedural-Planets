using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]

    public partial class Grendgine_Collada_Axis_Info_Kinematics : Grendgine_Collada_Axis_Info
    {
        [XmlElement(ElementName = "newparam")]
        public Grendgine_Collada_New_Param[] New_Param { get; set; }

        [XmlElement(ElementName = "active")]
        public Grendgine_Collada_Common_Bool_Or_Param_Type Active { get; set; }

        [XmlElement(ElementName = "locked")]
        public Grendgine_Collada_Common_Bool_Or_Param_Type Locked { get; set; }

        [XmlElement(ElementName = "index")]
        public Grendgine_Collada_Kinematics_Axis_Info_Index[] Index { get; set; }

        [XmlElement(ElementName = "limits")]
        public Grendgine_Collada_Kinematics_Axis_Info_Limits Limits { get; set; }

        [XmlElement(ElementName = "formula")]
        public Grendgine_Collada_Formula[] Formula { get; set; }

        [XmlElement(ElementName = "instance_formula")]
        public Grendgine_Collada_Instance_Formula[] Instance_Formula { get; set; }
    }
}

