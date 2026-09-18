using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "texture_pipeline", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Texture_Pipeline
    {

        [XmlAttribute("sid")]
        public string sID { get; set; }


        [XmlElement(ElementName = "texcombiner")]
        public Grendgine_Collada_TexCombiner[] TexCombiner { get; set; }

        [XmlElement(ElementName = "texenv")]
        public Grendgine_Collada_TexEnv[] TexEnv { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }

    }
}

