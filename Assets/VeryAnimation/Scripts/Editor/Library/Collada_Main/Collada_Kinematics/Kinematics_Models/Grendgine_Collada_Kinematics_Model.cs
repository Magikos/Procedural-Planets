using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "kinematics_model", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Kinematics_Model
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }


        [XmlElement(ElementName = "technique_common")]
        public Grendgine_Collada_Technique_Common_Kinematics_Model Technique_Common { get; set; }

        [XmlElement(ElementName = "technique")]
        public Grendgine_Collada_Technique[] Technique { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

