using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{

    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "technique_common", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Technique_Common_Kinematics_Model : Grendgine_Collada_Technique_Common
    {
        [XmlElement(ElementName = "newparam")]
        public Grendgine_Collada_New_Param[] New_Param { get; set; }

        [XmlElement(ElementName = "joint")]
        public Grendgine_Collada_Joint[] Joint { get; set; }

        [XmlElement(ElementName = "instance_joint")]
        public Grendgine_Collada_Instance_Joint[] Instance_Joint { get; set; }

        [XmlElement(ElementName = "link")]
        public Grendgine_Collada_Link[] Link { get; set; }

        [XmlElement(ElementName = "formula")]
        public Grendgine_Collada_Formula[] Formula { get; set; }

        [XmlElement(ElementName = "instance_formula")]
        public Grendgine_Collada_Instance_Formula[] Instance_Formula { get; set; }

    }
}

