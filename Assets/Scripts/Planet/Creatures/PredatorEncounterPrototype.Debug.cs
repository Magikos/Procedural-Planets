using System.Collections.Generic;
using UnityEngine;

public sealed partial class PredatorEncounterPrototype
{
    [Header("Perception drawings (Game view)")]
    public bool DrawSenses;
    public bool DrawSight = true, DrawHearing = true, DrawSmell = true, DrawSenseMemory = true;
    readonly List<Rect> _senseLabels = new();

    void DrawPerceptionDebug()
    {
        if (!DrawSenses || Event.current.type != EventType.Repaint || _actors.Count == 0) return;
        var camera = Camera.main;
        if (camera == null) return;
        var actor = _actors[_focusActor >= 0 && _focusActor < _actors.Count ? _focusActor : 0];
        var origin = actor.View.Root.position;
        var profile = actor.Perception.Profile;
        var oldColor = GUI.color;
        _senseLabels.Clear();
        if (DrawSight && Available(actor) && actor.Brain.Behaviour != CreatureBehaviour.Sleep)
            SenseArc(camera, origin, actor.View.Root.forward, Mathf.Lerp(profile.NightSight, profile.DaySight, PerceptionLight),
                profile.ViewAngle, Color.green);
        if (DrawHearing && Available(actor)) SenseArc(camera, origin, Vector3.forward, profile.HearingRange, 360, Color.cyan);
        if (DrawSmell && Available(actor)) SenseArc(camera, origin, Vector3.forward, profile.SmellRange, 360, new Color(1, .55f, .1f));
        if (DrawSenseMemory)
        {
            foreach (var target in _actors) DrawObservation(camera, actor, target.Id, ActorObservationKind.Actor);
            foreach (var source in _sources)
                DrawObservation(camera, actor, source.Id, source.Stock.ThirstPerUnit > 0 ? ActorObservationKind.Water : ActorObservationKind.Food);
        }
        GUI.color = oldColor;
    }

    void DrawObservation(Camera camera, Actor actor, ulong id, ActorObservationKind kind)
    {
        if (!actor.Knowledge.TryGet(id, kind, _elapsed, out var observation)) return;
        if (observation.Sense == ActorSense.Sight && !DrawSight || observation.Sense == ActorSense.Hearing && !DrawHearing ||
            observation.Sense == ActorSense.Smell && !DrawSmell) return;
        Color color = observation.Sense == ActorSense.Sight ? Color.green : observation.Sense == ActorSense.Hearing ? Color.cyan : new Color(1, .55f, .1f);
        color.a = Mathf.Lerp(.25f, 1, observation.Confidence);
        SenseLine(camera, actor.View.Root.position, observation.Position, color);
        SenseArc(camera, observation.Position, Vector3.forward, Mathf.Max(.15f, observation.Uncertainty), 360, color);
        var point = camera.WorldToScreenPoint(observation.Position);
        if (point.z <= camera.nearClipPlane) return;
        var rect = new Rect(point.x + 8, Screen.height - point.y, 290, 40);
        // Nearby sources often project onto the same pixels. Stack their labels without hiding the rings.
        for (int i = 0; i < _senseLabels.Count; i++)
            if (rect.Overlaps(_senseLabels[i])) { rect.y = _senseLabels[i].yMax + 2; i = -1; }
        _senseLabels.Add(rect);
        GUI.color = new Color(0, 0, 0, .85f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = color;
        GUI.Label(rect, $"{kind} {id}: {observation.Sense} {observation.Confidence:P0}\n±{observation.Uncertainty:F1}m | age {_elapsed - observation.Observed:F1}s");
    }

    static void SenseArc(Camera camera, Vector3 origin, Vector3 forward, float radius, float angle, Color color)
    {
        if (radius <= 0 || angle <= 0) return;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        Vector3 first = origin + Quaternion.AngleAxis(-angle * .5f, Vector3.up) * forward * radius;
        Vector3 previous = first;
        const int segments = 64;
        for (int i = 1; i <= segments; i++)
        {
            Vector3 next = origin + Quaternion.AngleAxis(-angle * .5f + angle * i / segments, Vector3.up) * forward * radius;
            SenseLine(camera, previous, next, color);
            previous = next;
        }
        if (angle < 360) { SenseLine(camera, origin, first, color); SenseLine(camera, origin, previous, color); }
    }

    // Screen overlay works in Game view without the Editor Gizmos switch or scene objects.
    static void SenseLine(Camera camera, Vector3 from, Vector3 to, Color color)
    {
        float near = camera.nearClipPlane + .001f;
        float a = Vector3.Dot(from - camera.transform.position, camera.transform.forward);
        float b = Vector3.Dot(to - camera.transform.position, camera.transform.forward);
        if (a < near && b < near) return;
        if (a < near) from = Vector3.Lerp(from, to, (near - a) / (b - a));
        else if (b < near) to = Vector3.Lerp(to, from, (near - b) / (a - b));
        Vector3 start = camera.WorldToScreenPoint(from), end = camera.WorldToScreenPoint(to);
        var p = new Vector2(start.x, Screen.height - start.y);
        var delta = new Vector2(end.x - start.x, start.y - end.y);
        var matrix = GUI.matrix;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, p);
        GUI.DrawTexture(new Rect(p.x, p.y, delta.magnitude, 1.5f), Texture2D.whiteTexture);
        GUI.matrix = matrix;
    }
}
