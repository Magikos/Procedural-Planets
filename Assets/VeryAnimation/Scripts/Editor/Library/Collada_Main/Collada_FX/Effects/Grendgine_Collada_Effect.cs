using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "effect", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Effect
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "annotate")]
        public Grendgine_Collada_Annotate[] Annotate { get; set; }

        [XmlElement(ElementName = "newparam")]
        public Grendgine_Collada_New_Param[] New_Param { get; set; }

        [XmlElement(ElementName = "profile_BRIDGE")]
        public Grendgine_Collada_Profile_BRIDGE[] Profile_BRIDGE { get; set; }

        [XmlElement(ElementName = "profile_CG")]
        public Grendgine_Collada_Profile_CG[] Profile_CG { get; set; }

        [XmlElement(ElementName = "profile_GLES")]
        public Grendgine_Collada_Profile_GLES[] Profile_GLES { get; set; }

        [XmlElement(ElementName = "profile_GLES2")]
        public Grendgine_Collada_Profile_GLES2[] Profile_GLES2 { get; set; }

        [XmlElement(ElementName = "profile_GLSL")]
        public Grendgine_Collada_Profile_GLSL[] Profile_GLSL { get; set; }

        [XmlElement(ElementName = "profile_COMMON")]
        public Grendgine_Collada_Profile_COMMON[] Profile_COMMON { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done