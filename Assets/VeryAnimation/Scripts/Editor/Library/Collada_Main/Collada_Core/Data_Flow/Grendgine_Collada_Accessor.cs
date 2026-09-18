using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Accessor
    {
        [XmlAttribute("count")]
        public uint Count { get; set; }

        [XmlAttribute("offset")]
        [System.ComponentModel.DefaultValueAttribute(typeof(uint), "0")]
        public uint Offset { get; set; }

        [XmlAttribute("source")]
        public string Source { get; set; }

        [XmlAttribute("stride")]
        [System.ComponentModel.DefaultValueAttribute(typeof(uint), "1")]
        public uint Stride { get; set; } = 1;

        [XmlElement(ElementName = "param")]
        public Grendgine_Collada_Param[] Param { get; set; }
    }
}

