using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{

    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_SID_Name_Float
    {
        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlTextAttribute()]
        public float Value { get; set; }
    }
}

