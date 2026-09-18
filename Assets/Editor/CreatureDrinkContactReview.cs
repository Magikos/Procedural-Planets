using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CreatureDrinkContactReview
{
    public static string MeasureGoat()
    {
        var settings = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>("Assets/Art/Creatures/Goat/GoatVisuals.asset");
        var model = Object.Instantiate(settings.MalePrefab);
        try
        {
            var rig = model.GetComponent<ProceduralRigDefinition>();
            settings.Idle.SampleAnimation(model, 0f);
            var up = model.transform.up;
            float ground = rig.Feet.Min(f => Vector3.Dot((f.Contact != null ? f.Contact : f.Bones[^1]).position, up));
            var markers = rig.Look[^1].GetComponentsInChildren<Transform>();
            var minima = markers.Select(_ => float.PositiveInfinity).ToArray();
            var maxima = markers.Select(_ => float.NegativeInfinity).ToArray();
            var records = new List<object>();
            for (int sample = 0; sample < 180; sample++)
            {
                float time = settings.Drink.length * sample / 180f;
                settings.Drink.SampleAnimation(model, time);
                for (int i = 0; i < markers.Length; i++)
                {
                    float height = Vector3.Dot(markers[i].position, up) - ground;
                    minima[i] = Mathf.Min(minima[i], height); maxima[i] = Mathf.Max(maxima[i], height);
                }
            }
            for (int i = 0; i < markers.Length; i++)
                records.Add(new { marker = markers[i].name, local = new[] { markers[i].localPosition.x, markers[i].localPosition.y, markers[i].localPosition.z }, min = minima[i], max = maxima[i] });
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { settings.ModelHeightMeters, settings.Drink.length, ground, records }, Newtonsoft.Json.Formatting.Indented);
        }
        finally { Object.DestroyImmediate(model); }
    }
}
