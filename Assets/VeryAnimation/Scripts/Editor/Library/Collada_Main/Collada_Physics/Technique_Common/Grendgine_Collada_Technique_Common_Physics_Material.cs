using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{

    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Technique_Common_Physics_Material : Grendgine_Collada_Technique_Common
    {

        [XmlElement(ElementName = "dynamic_friction")]
        public Grendgine_Collada_SID_Float Dynamic_Friction { get; set; }

        [XmlElement(ElementName = "restitution")]
        public Grendgine_Collada_SID_Float Restitution { get; set; }

        [XmlElement(ElementName = "static_friction")]
        public Grendgine_Collada_SID_Float Static_Friction { get; set; }
    }
}

