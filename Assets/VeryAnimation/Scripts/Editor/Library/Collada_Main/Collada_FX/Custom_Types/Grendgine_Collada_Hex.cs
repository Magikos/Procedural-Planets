using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{

    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Hex
    {
        [XmlAttribute("format")]
        public string Format { get; set; }

        [XmlTextAttribute()]
        public string Value { get; set; }
        //TODO: this is a hex array
    }
}

