using UnityEditor;
using UnityEngine;

/// <summary>Adds a visible, explicit two-hand rig fixture to the caller's chosen review scene.</summary>
public static class ActorInteractionReviewAuthor
{
    public static GameObject BuildFixture(Vector3 position)
    {
        var root = new GameObject("Shared actor hand and gaze review");
        root.transform.position = position;
        var rig = root.AddComponent<ProceduralRigDefinition>();
        var review = root.AddComponent<ActorInteractionReview>(); review.Rig = rig;
        Transform Joint(string name, Transform parent, Vector3 local)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(parent, false); joint.localPosition = local;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Joint marker"; marker.transform.SetParent(joint, false);
            marker.transform.localScale = Vector3.one * .09f;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            return joint;
        }
        Transform Target(string name, Vector3 local)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = name; target.transform.SetParent(root.transform, false);
            target.transform.localPosition = local; target.transform.localScale = new Vector3(.12f, .12f, .25f);
            Object.DestroyImmediate(target.GetComponent<Collider>());
            return target.transform;
        }
        var chest = Joint("Chest", root.transform, Vector3.up * 1.3f);
        var neck = Joint("Neck", chest, Vector3.up * .25f);
        var head = Joint("Head", neck, Vector3.up * .18f);
        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Gaze direction"; nose.transform.SetParent(head, false);
        nose.transform.localPosition = Vector3.forward * .15f;
        nose.transform.localScale = new Vector3(.05f, .05f, .25f);
        Object.DestroyImmediate(nose.GetComponent<Collider>());
        rig.Look = new[] { neck, head }; rig.LookWeights = new[] { 1f, 2f };
        rig.Interactions = new InteractionLimbDefinition[2];
        review.Targets = new ActorInteractionReview.HandTarget[2];
        for (int i = 0; i < 2; i++)
        {
            string id = i == 0 ? "LeftHand" : "RightHand"; float side = i == 0 ? -1f : 1f;
            var shoulder = Joint(id + " Shoulder", chest, Vector3.right * side * .25f);
            var elbow = Joint(id + " Elbow", shoulder, new Vector3(side * .25f, -.2f, .15f));
            var hand = Joint(id + " Palm", elbow, new Vector3(0f, -.15f, .3f));
            var palm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            palm.transform.SetParent(hand, false); palm.transform.localScale = new Vector3(.1f, .03f, .16f);
            Object.DestroyImmediate(palm.GetComponent<Collider>());
            rig.Interactions[i] = new InteractionLimbDefinition { Id = id,
                Bones = new[] { shoulder, elbow, hand }, JointLimit = 160f };
            review.Targets[i] = new ActorInteractionReview.HandTarget { Id = id,
                Target = Target(id + " target - drag and rotate", new Vector3(side * .45f, 1.2f, .5f)) };
        }
        review.GazeTarget = Target("Gaze target - drag", new Vector3(.6f, 1.8f, 1.5f));
        var label = new GameObject("Instructions").AddComponent<TextMesh>();
        label.transform.SetParent(root.transform, false); label.transform.localPosition = new Vector3(-1f, 2.25f, 0f);
        label.text = "Shared hand IK + gaze fixture\nDrag/rotate hand targets. Adjust Weight to release.\nDrag gaze target. Adjust Look Influence to fade.";
        label.characterSize = .07f; label.fontSize = 32;
        EditorUtility.SetDirty(root);
        return root;
    }
}
