using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Geographic_Location
    {


        [XmlElement(ElementName = "longitude")]
        public float Longitude { get; set; }

        [XmlElement(ElementName = "latitude")]
        public float Latitude { get; set; }

        [XmlElement(ElementName = "altitude")]
        public Grendgine_Collada_Geographic_Location_Altitude Altitude { get; set; }

    }
}

