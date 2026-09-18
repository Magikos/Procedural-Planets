using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "bind_material", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Bind_Material
    {

        [XmlElement(ElementName = "param")]
        public Grendgine_Collada_Param[] Param { get; set; }

        [XmlElement(ElementName = "technique_common")]
        public Grendgine_Collada_Technique_Common_Bind_Material Technique_Common { get; set; }

        [XmlElement(ElementName = "technique")]
        public Grendgine_Collada_Technique[] Technique { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done