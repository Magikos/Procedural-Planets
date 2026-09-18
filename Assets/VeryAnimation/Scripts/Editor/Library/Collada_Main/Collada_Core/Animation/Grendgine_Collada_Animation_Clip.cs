using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Animation_Clip
    {

        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("start")]
        public double Start { get; set; }

        [XmlAttribute("end")]
        public double End { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "instance_animation")]
        public Grendgine_Collada_Instance_Animation[] Instance_Animation { get; set; }

        //1.5
        //[XmlElement(ElementName = "instance_formula")]
        //public Grendgine_Collada_Instance_Formula[] Instance_Formula;	

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done