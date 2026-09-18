using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Editor-only, deterministic deer comparison using the production presenter and its displayed graph.</summary>
public static class CreatureQualityReviewCapture
{
    const float Dt = 1f / 60f;
    static readonly Vector3 Origin = new(1000f, 0f, 1000f);

    [Serializable] sealed class Record
    {
        public string stageOrder;
        public string materialAdaptation = "A uses the production preview skin materials, matched by renderer hierarchy and identical mesh. Its original rig and animation remain unchanged.";
        public string convention = "World metres, Y-up. A has no authored root travel; all stages receive the same diagnostic trajectory plus 12 m column offsets. Cameras follow incoming root and facing. Floors retain world markings.";
        public string scope = "Dry male deer presentation on flat ground. No authority, collision, swimming, or behavioral validation. A loops walk during B/C idle and transitions; compare events without time warping A.";
        public string sourceRig, sourceClip, productionPrefab;
        public string runtimeAssemblyModuleId = typeof(CreatureAnimationView).Module.ModuleVersionId.ToString();
        public string captureAssemblyModuleId = typeof(CreatureQualityReviewCapture).Module.ModuleVersionId.ToString();
        public string sequence, sourceComparisonLimits;
        public string rootMotionConvention;
        public float clipLength, clipFrameRate, simulationHz = 60f, captureHz = 30f, height = 1.84f;
        public float walkMetersPerSecond, runMetersPerSecond, straightDuration, duration;
        public string[] clipNames, contactNames;
        public string[] secondaryBonePaths;
        public float[] soleOffsets, correctionLimits;
        public string contactConvention = "Contact transforms, or final articulation joint when no marker exists. Column offsets removed; world-space Y retained. sourceContactWeights are rig-authored WalkContact curves, not measured ground contact. Correction is C minus B; stance drift requires choosing contact intervals from frames.";
        public Vector3 origin = Origin;
        public Vector3 frontCameraOffset = new(3f, 1f, -4f), sideCameraOffset = new(5f, .3f, 0f);
        public float orthographicHalfHeight = 1.85f;
        public List<Sample> samples = new();
    }

    [Serializable] sealed class Sample
    {
        public int frame;
        public float seconds;
        public string action;
        public bool resting, sleeping, swimming, chainsEnabled;
        public Vector3 inputRoot, velocity;
        public Quaternion facing;
        public double sourceTime;
        public double[] clipTimes, clipSpeeds;
        public float[] clipWeights;
        public Vector3[] uncorrectedContacts, finalContacts, contactCorrections;
        public float[] sourceContactWeights;
        public float maximumCorrection, solverResidual;
        public Quaternion[] uncorrectedSecondaryRotations, finalSecondaryRotations;
        public int plantedFeet;
    }

    sealed class FlatGround : IGroundingProvider
    {
        public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
        {
            result = new GroundResult(new Vector3(position.x, offset, position.z), Vector3.up);
            return true;
        }
    }

    /// <summary>Captures a fresh isolated fixture. Refuses to overwrite an existing review bundle.</summary>
    public static string Capture(string directory, string species = "Deer", string sequence = "gait")
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Creature review capture requires Play Mode.");
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Provide a capture directory.", nameof(directory));
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length > 0)
            throw new IOException("The capture directory must be empty: " + directory);
        if (species != "Deer" && species != "Wolf" && species != "Goat")
            throw new ArgumentException("Supported review species are Deer, Wolf, and Goat.", nameof(species));
        if (sequence != "gait" && sequence != "continuity" && sequence != "swim" && sequence != "feeding" && sequence != "drink")
            throw new ArgumentException("Supported review sequences are gait, continuity, swim, feeding (existing Eat candidate), and drink (focused action).", nameof(sequence));
        string folder = "Assets/Art/Creatures/" + species + "/";
        var visual = AssetDatabase.LoadAssetAtPath<CreatureVisualSettings>(folder + species + "Visuals.asset");
        var originalRig = AssetDatabase.LoadAssetAtPath<GameObject>(folder + species + (species == "Goat" ? "_Animations.fbx" : "_Rig.fbx"));
        if (visual == null || originalRig == null || visual.Walk == null)
            throw new InvalidOperationException("Missing original deer rig or production walk settings.");
        if (originalRig.GetComponentsInChildren<Renderer>(true).Length == 0)
            throw new InvalidOperationException("The original deer rig has no renderer; source comparison cannot be certified.");
        Directory.CreateDirectory(directory);
        var host = new GameObject("Temporary deer quality review") { hideFlags = HideFlags.HideAndDontSave };
        var materials = new List<Material>();
        CreatureAnimationView final = null;
        ActorAnimationGraph plain = null;
        AuthoredAnimationReference original = null;
        AnimationReviewFrames frames = null;
        try
        {
            var dto = visual.Snapshot();
            if (sequence == "feeding")
            {
                if (visual.Eat == null) throw new ArgumentException("The feeding candidate requires an existing Eat clip.");
                dto = dto with { Drink = visual.Eat };
            }
            float height = dto.ModelHeightMeters;
            final = new CreatureAnimationView(host.transform, 0UL, dto, height);
            var plainRoot = new GameObject("B production graph without corrections").transform;
            plainRoot.SetParent(host.transform, false);
            var model = UnityEngine.Object.Instantiate(dto.MalePrefab, plainRoot);
            model.transform.localPosition = Vector3.down * (height * .5f);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            foreach (var behaviour in model.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = behaviour is Animator;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            var mixer = final.Graph.BaseMixer;
            plain = new ActorAnimationGraph(model.GetComponent<Animator>(), "Deer production graph mirror", mixer.GetInputCount());
            var clips = new AnimationClipPlayable[mixer.GetInputCount()];
            for (int i = 0; i < clips.Length; i++)
            {
                var input = mixer.GetInput(i);
                if (!input.IsValid() || input.GetPlayableType() != typeof(AnimationClipPlayable))
                    throw new InvalidOperationException("Deer comparison requires populated clip inputs.");
                clips[i] = plain.AddBaseClip(i, ((AnimationClipPlayable)input).GetAnimationClip());
                clips[i].SetApplyFootIK(false);
                clips[i].SetApplyPlayableIK(false);
            }
            var sourceClip = sequence == "drink" ? dto.Drink ?? dto.Eat ?? dto.Idle : sequence == "feeding" ? visual.Eat : sequence == "gait" ? visual.Walk : species == "Goat" && sequence == "continuity" ? visual.Rest : visual.Idle;
            original = new AuthoredAnimationReference(originalRig, sourceClip, true);
            original.Root.transform.SetParent(host.transform, false);
            CreatureAnimationPrototype.ApplyPreviewMaterials(plainRoot, materials);
            CopyPreviewSkin(original.Animator.transform, model.transform);
            CreatureAnimationPrototype.ApplyPreviewMaterials(final.Root, materials);
            CreateFloor(host.transform, materials);
            var sun = new GameObject("Review light").AddComponent<Light>();
            sun.transform.SetParent(host.transform, false);
            sun.type = LightType.Directional; sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            var plainFeet = model.GetComponent<ProceduralRigDefinition>().Feet;
            var finalFeet = final.Root.GetComponentInChildren<ProceduralRigDefinition>().Feet;
            var secondaryBones = new List<Transform>();
            foreach (var chain in model.GetComponent<ProceduralRigDefinition>().Chains)
                foreach (var bone in chain.Bones) if (!secondaryBones.Contains(bone)) secondaryBones.Add(bone);
            var secondaryPaths = new string[secondaryBones.Count];
            var finalSecondary = new Transform[secondaryBones.Count];
            var finalModel = final.Root.GetComponentInChildren<ProceduralRigDefinition>().transform;
            for (int i = 0; i < secondaryBones.Count; i++)
            {
                secondaryPaths[i] = AnimationUtility.CalculateTransformPath(secondaryBones[i], model.transform);
                finalSecondary[i] = finalModel.Find(secondaryPaths[i]);
                if (finalSecondary[i] == null) throw new InvalidOperationException("Missing secondary comparison bone: " + secondaryPaths[i]);
            }
            if (plainFeet.Length != finalFeet.Length) throw new InvalidOperationException("Comparison contact bindings differ.");
            var record = new Record
            {
                sourceRig = original.RigAssetPath, sourceClip = original.ClipAssetPath,
                productionPrefab = AssetDatabase.GetAssetPath(dto.MalePrefab), rootMotionConvention = original.RootMotionConvention,
                clipLength = sourceClip.length, clipFrameRate = sourceClip.frameRate, height = height,
                sequence = sequence,
                sourceComparisonLimits = sequence == "drink" ? "A: selected Drink clip on native rig at 1x; Goat uses artist Goat_Eating, Wolf uses a project variant. A holds its first frame during idle lead-in, then runs independently. B/C: idle, two complete drink cycles, idle recovery. No rest/sleep commands."
                    : sequence == "feeding" ? "Preview only: existing Eat replaces Drink in the temporary DTO. A loops that clip at 1x; rest/idle segments are not source matched. Goat Eat is original artist motion; Wolf Eat is a generated project variant. No asset changed."
                    : sequence == "gait" ? "A is independent original walk; compare matched events rather than equal elapsed frames."
                    : sequence == "swim" ? "A shows original idle, NOT original swim. Species has no selected authored swim; C uses its existing fallback."
                    : species == "Goat" ? "A shows original rest clip continuously. Other action segments require their own source comparison."
                    : "A shows original idle. Wolf/deer rest and drink are project variants, not independent artist originals.",
                walkMetersPerSecond = dto.WalkMetersPerSecond, runMetersPerSecond = dto.RunMetersPerSecond,
                straightDuration = Mathf.Max(2f * visual.Walk.length, 2f),
                clipNames = new string[clips.Length], contactNames = new string[plainFeet.Length],
                secondaryBonePaths = secondaryPaths,
                soleOffsets = new float[plainFeet.Length], correctionLimits = new float[plainFeet.Length]
            };
            for (int i = 0; i < clips.Length; i++) record.clipNames[i] = clips[i].GetAnimationClip().name;
            for (int i = 0; i < plainFeet.Length; i++)
            {
                record.contactNames[i] = AnimationUtility.CalculateTransformPath(Contact(plainFeet[i]), model.transform);
                record.soleOffsets[i] = plainFeet[i].SoleOffset; record.correctionLimits[i] = plainFeet[i].MaxCorrection;
            }
            record.duration = 1f + record.straightDuration + 1f + 1f;
            if (sequence != "gait") record.duration = sequence == "swim" ? 6f : sequence == "drink" ? 2.5f + 2f * sourceClip.length : 8f;
            record.stageOrder = $"Columns A {sourceClip.name} on original rig at 1x (clip provenance: see sourceComparisonLimits); B production graph without corrections; C production presenter. Rows front-oblique, side.";
            record.scope = species + " " + sequence + " presentation on flat ground. No authority/collision validation. " + record.sourceComparisonLimits;
            record.orthographicHalfHeight = species == "Deer" ? 1.85f : Mathf.Max(1f, height * 1.01f);
            frames = new AnimationReviewFrames(360);
            var root = Origin + Vector3.up * (height * .5f);
            Quaternion facing = Quaternion.identity;
            var ground = new FlatGround();
            int steps = Mathf.CeilToInt(record.duration / Dt);
            for (int frame = 0; frame <= steps; frame++)
            {
                float time = frame * Dt;
                bool walking = sequence == "gait" && time >= 1f && time < 2f + record.straightDuration;
                bool turning = walking && time >= 1f + record.straightDuration;
                if (turning) facing = Quaternion.AngleAxis(30f * Dt, Vector3.up) * facing;
                Vector3 velocity = walking ? facing * Vector3.forward * dto.WalkMetersPerSecond : Vector3.zero;
                if (frame > 0) root += velocity * Dt;
                final.Root.SetPositionAndRotation(root + Vector3.right * 24f, facing);
                plainRoot.SetPositionAndRotation(root + Vector3.right * 12f, facing);
                original.Root.transform.SetPositionAndRotation(root - Vector3.up * (height * .5f), facing);
                string action = sequence == "gait" ? !walking ? time < 1f ? "idle lead-in" : "stop recovery" : turning ? "walk turn" : "straight walk"
                    : sequence == "drink" ? ConfigureDrink(final, time, sourceClip.length)
                    : CreatureContinuityReviewCapture.Configure(final, time, sequence == "swim");
                original.Sample(sequence == "drink" ? Mathf.Max(0f, time - 1f) : time);
                final.Tick(velocity, Vector3.up, frame == 0 ? 0f : Dt, ground);
                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i].SetTime(mixer.GetInput(i).GetTime());
                    plain.BaseMixer.SetInputWeight(i, mixer.GetInputWeight(i));
                }
                plain.Evaluate();
                var sample = new Sample
                {
                    frame = frame, seconds = time, action = action,
                    resting = final.Resting, sleeping = final.Sleeping, swimming = final.Swimming, chainsEnabled = final.Pose?.ChainsEnabled ?? false,
                    inputRoot = root, facing = facing, velocity = velocity, sourceTime = original.ClipTimeSeconds,
                    clipTimes = new double[clips.Length], clipSpeeds = new double[clips.Length], clipWeights = new float[clips.Length],
                    uncorrectedContacts = new Vector3[plainFeet.Length], finalContacts = new Vector3[plainFeet.Length],
                    contactCorrections = new Vector3[plainFeet.Length], sourceContactWeights = new float[plainFeet.Length],
                    plantedFeet = final.Pose?.PlantedFeet ?? 0, solverResidual = final.Pose?.MaxFootError ?? 0f
                };
                sample.uncorrectedSecondaryRotations = new Quaternion[secondaryBones.Count];
                sample.finalSecondaryRotations = new Quaternion[secondaryBones.Count];
                for (int i = 0; i < secondaryBones.Count; i++)
                {
                    sample.uncorrectedSecondaryRotations[i] = secondaryBones[i].localRotation;
                    sample.finalSecondaryRotations[i] = finalSecondary[i].localRotation;
                }
                for (int i = 0; i < clips.Length; i++)
                {
                    sample.clipTimes[i] = mixer.GetInput(i).GetTime(); sample.clipSpeeds[i] = mixer.GetInput(i).GetSpeed();
                    sample.clipWeights[i] = mixer.GetInputWeight(i);
                }
                for (int i = 0; i < plainFeet.Length; i++)
                {
                    sample.uncorrectedContacts[i] = Contact(plainFeet[i]).position - Vector3.right * 12f;
                    sample.finalContacts[i] = Contact(finalFeet[i]).position - Vector3.right * 24f;
                    sample.contactCorrections[i] = sample.finalContacts[i] - sample.uncorrectedContacts[i];
                    sample.maximumCorrection = Mathf.Max(sample.maximumCorrection, sample.contactCorrections[i].magnitude);
                    sample.sourceContactWeights[i] = plainFeet[i].WalkContact.Evaluate(Mathf.Repeat((float)(sample.clipTimes[1] / visual.Walk.length), 1f));
                }
                record.samples.Add(sample);
                if (frame % 2 == 0)
                    frames.Capture(Path.Combine(directory, $"frame-{frame / 2:D4}.png"),
                        new[] { root, root + Vector3.right * 12f, root + Vector3.right * 24f }, facing, record.orthographicHalfHeight);
            }
            File.WriteAllText(Path.Combine(directory, "metrics.json"), JsonUtility.ToJson(record, true));
            return $"Captured {record.samples.Count} simulation samples and {(steps / 2) + 1} frames to {directory}";
        }
        finally
        {
            frames?.Dispose(); original?.Dispose(); plain?.Dispose(); final?.Dispose();
            UnityEngine.Object.DestroyImmediate(host);
            foreach (var material in materials) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    static string ConfigureDrink(CreatureAnimationView view, float time, float clipLength)
    {
        view.Resting = view.Sleeping = view.Swimming = false;
        view.Drinking = time >= 1f && time < 1f + 2f * clipLength;
        view.LookTarget = null;
        return time < 1f ? "idle lead-in" : view.Drinking ? "drink: two authored cycles" : "idle recovery";
    }

    static Transform Contact(FootDefinition foot) => foot.Contact != null ? foot.Contact : foot.Bones[^1];

    static void CopyPreviewSkin(Transform original, Transform production)
    {
        foreach (var renderer in original.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, original);
            var matched = (path.Length == 0 ? production : production.Find(path))?.GetComponent<SkinnedMeshRenderer>();
            if (matched == null || matched.sharedMesh != renderer.sharedMesh)
                throw new InvalidOperationException("Cannot match original deer skin renderer: " + path);
            renderer.sharedMaterials = matched.sharedMaterials;
        }
    }

    static void CreateFloor(Transform parent, List<Material> materials)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        for (int i = 0; i < 3; i++)
        for (int z = -2; z < 10; z++)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = "World-space metre marking";
            tile.transform.SetParent(parent, false);
            tile.transform.position = Origin + new Vector3(i * 12f, -.025f, z + .5f);
            tile.transform.localScale = new Vector3(7f, .05f, 1f);
            UnityEngine.Object.DestroyImmediate(tile.GetComponent<Collider>());
            var material = new Material(shader);
            material.SetColor("_BaseColor", z % 2 == 0 ? new Color(.25f, .29f, .25f) : new Color(.32f, .36f, .32f));
            tile.GetComponent<Renderer>().sharedMaterial = material;
            materials.Add(material);
        }
    }
}
