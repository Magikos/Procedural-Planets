using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Source
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "IDREF_array")]
        public Grendgine_Collada_IDREF_Array IDREF_Array { get; set; }

        [XmlElement(ElementName = "Name_array")]
        public Grendgine_Collada_Name_Array Name_Array { get; set; }

        [XmlElement(ElementName = "bool_array")]
        public Grendgine_Collada_Bool_Array Bool_Array { get; set; }

        [XmlElement(ElementName = "float_array")]
        public Grendgine_Collada_Float_Array Float_Array { get; set; }

        [XmlElement(ElementName = "int_array")]
        public Grendgine_Collada_Int_Array Int_Array { get; set; }

        [XmlElement(ElementName = "SIDREF_array")]
        public Grendgine_Collada_SIDREF_Array SIDREF_Array { get; set; }

        [XmlElement(ElementName = "token_array")]
        public Grendgine_Collada_Token_Array Token_Array { get; set; }

        [XmlElement(ElementName = "technique_common")]
        public Grendgine_Collada_Technique_Common_Source Technique_Common { get; set; }

        [XmlElement(ElementName = "technique")]
        public Grendgine_Collada_Technique[] Technique { get; set; }

    }
}

//check done