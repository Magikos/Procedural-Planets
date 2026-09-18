using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "profile_GLES2", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Profile_GLES2 : Grendgine_Collada_Profile
    {
        [XmlAttribute("platform")]
        public string Platform { get; set; }

        [XmlAttribute("language")]
        public string Language { get; set; }

        [XmlElement(ElementName = "newparam")]
        public Grendgine_Collada_New_Param[] New_Param { get; set; }

        [XmlElement(ElementName = "technique")]
        public Grendgine_Collada_Technique_GLES2[] Technique { get; set; }

        [XmlElement(ElementName = "code")]
        public Grendgine_Collada_Code[] Code { get; set; }

        [XmlElement(ElementName = "include")]
        public Grendgine_Collada_Include[] Include { get; set; }
    }
}

