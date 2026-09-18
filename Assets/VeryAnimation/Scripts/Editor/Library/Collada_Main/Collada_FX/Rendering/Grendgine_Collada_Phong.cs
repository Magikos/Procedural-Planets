using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "phong", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Phong
    {
        [XmlElement(ElementName = "emission")]
        public Grendgine_Collada_FX_Common_Color_Or_Texture_Type Emission { get; set; }

        [XmlElement(ElementName = "ambient")]
        public Grendgine_Collada_FX_Common_Color_Or_Texture_Type Ambient { get; set; }

        [XmlElement(ElementName = "diffuse")]
        public Grendgine_Collada_FX_Common_Color_Or_Texture_Type Diffuse { get; set; }

        [XmlElement(ElementName = "specular")]
        public Grendgine_Collada_FX_Common_Color_Or_Texture_Type Specular { get; set; }

        [XmlElement(ElementName = "shininess")]
        public Grendgine_Collada_FX_Common_Float_Or_Param_Type Shininess { get; set; }

        [XmlElement(ElementName = "reflective")]
        public Grendgine_Collada_FX_Common_Color_Or_Texture_Type Reflective { get; set; }

        [XmlElement(ElementName = "reflectivity")]
        public Grendgine_Collada_FX_Common_Float_Or_Param_Type Reflectivity { get; set; }

        [XmlElement(ElementName = "transparent")]
        public Grendgine_Collada_FX_Common_Color_Or_Texture_Type Transparent { get; set; }

        [XmlElement(ElementName = "transparency")]
        public Grendgine_Collada_FX_Common_Float_Or_Param_Type Transparency { get; set; }

        [XmlElement(ElementName = "index_of_refraction")]
        public Grendgine_Collada_FX_Common_Float_Or_Param_Type Index_Of_Refraction { get; set; }
    }
}

//check done