using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Int_Array : Grendgine_Collada_Int_Array_String
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("count")]
        public int Count { get; set; }

        [XmlAttribute("minInclusive")]
        [System.ComponentModel.DefaultValueAttribute(typeof(int), "-2147483648")]
        public int Min_Inclusive { get; set; }

        [XmlAttribute("maxInclusive")]
        [System.ComponentModel.DefaultValueAttribute(typeof(int), "2147483647")]
        public int Max_Inclusive { get; set; }

    }
}

