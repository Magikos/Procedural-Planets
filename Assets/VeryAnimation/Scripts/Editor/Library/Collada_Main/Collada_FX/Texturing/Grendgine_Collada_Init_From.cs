using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "init_from", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Init_From
    {
        [XmlAttribute("mips_generate")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        public bool Mips_Generate { get; set; }

        [XmlAttribute("array_index")]
        [System.ComponentModel.DefaultValueAttribute(0)]
        public int Array_Index { get; set; }

        [XmlAttribute("mip_index")]
        [System.ComponentModel.DefaultValueAttribute(0)]
        public int Mip_Index { get; set; }

        [XmlAttribute("depth")]
        [System.ComponentModel.DefaultValueAttribute(0)]
        public int Depth { get; set; }

        [XmlAttribute("face")]
        [System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_Face.POSITIVE_X)]
        public Grendgine_Collada_Face Face { get; set; }

        [XmlElement(ElementName = "ref")]
        public string Ref { get; set; }

        [XmlElement(ElementName = "hex")]
        public Grendgine_Collada_Hex Hex { get; set; }
    }
}

