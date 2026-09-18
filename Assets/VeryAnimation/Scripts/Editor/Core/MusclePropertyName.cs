using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeryAnimation
{
    internal class MusclePropertyName
    {
        public string[] Names { get; private set; }
        public string[] PropertyNames { get; private set; }
        public Dictionary<string, int> PropertyNameDic { get; private set; }

        public MusclePropertyName()
        {
            Names = HumanTrait.MuscleName;
            PropertyNames = new string[Names.Length];
            PropertyNameDic = new Dictionary<string, int>(Names.Length);
            for (int i = 0; i < Names.Length; i++)
            {
                if (Names[i].EndsWith("Stretched", StringComparison.Ordinal))
                {
                    var splits = Names[i].Split(' ');
                    PropertyNames[i] = $"{splits[0]}Hand.{splits[1]}.{splits[2]} {splits[3]}";
                }
                else if (Names[i].EndsWith("Spread", StringComparison.Ordinal))
                {
                    var splits = Names[i].Split(' ');
                    PropertyNames[i] = $"{splits[0]}Hand.{splits[1]}.{splits[2]}";
                }
                else
                {
                    PropertyNames[i] = Names[i];
                }
                PropertyNameDic.Add(PropertyNames[i], i);
            }
        }
    }
}
