using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace ProceduralPlanets.Tests
{
    [TestFixture("Assets/Art/Characters/Baseline/Baseline.prefab", 13)]
    [TestFixture("Assets/Art/Characters/Human/Human.prefab", 1)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/BareBody.prefab", 22)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/SourcePartsBody.prefab", 22)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/FullBody/FullSourceBody.prefab", 20)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/Outfit02/Outfit02Body.prefab", 20)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/Townsfolk/Monk_01_Fit.prefab", 11)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/Townsfolk/Monk_01_Original.prefab", 1)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/Townsfolk/Peasant_Male_01_Fit.prefab", 11)]
    [TestFixture("Assets/Art/Characters/Human/BodyReview/Townsfolk/Peasant_Male_01_Original.prefab", 1)]
    public sealed class HumanoidAnimationTests
    {
        const string Folder = "Assets/Art/Characters";
        readonly string _prefabPath;
        readonly int _skinCount;
        public HumanoidAnimationTests(string prefabPath, int skinCount) { _prefabPath = prefabPath; _skinCount = skinCount; }
        GameObject _actor;
        HumanoidAnimationView _view;
        Animator _animator;
        ProceduralRigDefinition _rig;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath);
            Assert.IsNotNull(prefab, "Author the humanoid before running asset integration tests.");
            _actor = Object.Instantiate(prefab);
            _animator = _actor.GetComponent<Animator>();
            _rig = _actor.AddComponent<ProceduralRigDefinition>();
            HumanoidRigBinding.Bind(_animator, _rig);
            AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(Folder + "/Animations/" + name + ".fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            _view = new HumanoidAnimationView(_animator, _rig, Clip("HumanoidIdle"), Clip("HumanoidWalk"), Clip("HumanoidRun"));
        }

        [TearDown]
        public void TearDown() { _view?.Dispose(); if (_actor != null) Object.DestroyImmediate(_actor); }

        [Test]
        public void ReviewWalkDirectionsShareTheSameSupportPhase()
        {
            _view.Dispose();
            var reference = new float[64];
            foreach (string direction in new[] { "Forward", "Left", "Right", "Backward" })
            {
                var clip = LoadClip("Walk " + direction);
                using var graph = new ActorAnimationGraph(_animator, "Walk contact phases", 1);
                var motion = graph.AddBaseClip(0, clip);
                graph.BaseMixer.SetInputWeight(0, 1f);
                float error = 0f;
                for (int i = 0; i < reference.Length; i++)
                {
                    motion.SetTime(clip.length * i / reference.Length);
                    graph.Evaluate();
                    float difference = _animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y -
                        _animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y;
                    if (direction == "Forward") reference[i] = difference;
                    error += Mathf.Pow(difference - reference[i], 2f);
                }
                Assert.Less(Mathf.Sqrt(error / reference.Length), .05f, direction + " must preserve the reference support phase.");
            }
        }

        [Test]
        public void SelectedOutfitBindsAndLocomotionCannotMoveAuthorityRoot()
        {
            Assert.IsTrue(_animator.isHuman);
            Assert.AreEqual(_skinCount, _actor.GetComponentsInChildren<SkinnedMeshRenderer>().Length);
            foreach (var skin in _actor.GetComponentsInChildren<SkinnedMeshRenderer>())
                Assert.IsTrue(skin.forceMatrixRecalculationPerRender);
            Assert.AreEqual(2, _rig.Feet.Length); Assert.AreEqual(2, _rig.Interactions.Length);
            Assert.IsNull(_animator.runtimeAnimatorController);
            Vector3 start = new(4f, 2f, 3f); _actor.transform.position = start;
            var leg = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Quaternion before = leg.localRotation;
            for (int i = 0; i < 120; i++) _view.Tick(Vector3.forward * 4f, Vector3.up, null, null, .02f);
            Assert.Less(Vector3.Distance(start, _actor.transform.position), .001f);
            Assert.Greater(Quaternion.Angle(before, leg.localRotation), 2f);
            _view.Dispose(); Assert.IsFalse(_view.Graph.IsValid());
        }

        [Test]
        public void SwimmingClipsReleaseFeetKeepHeadNearSurfaceAndReturnToLand()
        {
            _view.Dispose();
            AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(Folder + "/Animations/" + name + ".fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var clips = new[] { Clip("Surface Idle"), Clip("Slow Swim"), Clip("Underwater Idle"), Clip("Underwater Swim") };
            foreach (var clip in clips) { Assert.IsTrue(clip.isHumanMotion); Assert.IsEmpty(clip.events); }
            _view = new HumanoidAnimationView(_animator, _rig, Clip("HumanoidIdle"), Clip("HumanoidWalk"), Clip("HumanoidRun"), swimming: clips);
            for (int i = 0; i < 150; i++) _view.Tick(Vector3.forward * 1.5f, Vector3.up, null, null, .02f,
                swimming: true, waterDepth: 1.25f);
            Assert.AreEqual(1f, _view.SwimWeight);
            Assert.AreEqual(0, _view.Pose.PlantedFeet);
            Assert.Greater(_animator.GetBoneTransform(HumanBodyBones.Head).position.y, 1.15f);
            Assert.AreEqual(Vector3.zero, _actor.transform.position);
            for (int i = 0; i < 150; i++) _view.Tick(Vector3.down, Vector3.up, null, null, .02f,
                swimming: true, diving: true, waterDepth: 3f);
            Assert.IsTrue(CharacterMath.IsFinite(_animator.GetBoneTransform(HumanBodyBones.Head).position));
            for (int i = 0; i < 100; i++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.AreEqual(0f, _view.SwimWeight);
            Assert.IsTrue(_view.Pose.FeetEnabled);
        }

        [Test]
        public void SwimDirectionsSelectTheirClipsAndRemainNormalizedDuringReversals()
        {
            _view.Dispose();
            var swimming = new[] { "Surface Idle", "Slow Swim", "Underwater Idle", "Underwater Swim" }.Select(LoadClip).ToArray();
            var directions = new[] { "Swim Left", "Swim Right", "Swim Backward" }.Select(LoadClip).ToArray();
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("HumanoidWalk"),
                LoadClip("HumanoidRun"), swimming: swimming, swimDirectional: directions);
            var velocities = new[] { Vector3.left, Vector3.right, Vector3.back };
            for (int direction = 0; direction < velocities.Length; direction++)
            {
                for (int frame = 0; frame < 70; frame++)
                {
                    _view.Tick(velocities[direction] * 2f, Vector3.up, null, null, .02f, swimming: true, waterDepth: 1.25f);
                    float total = 0f;
                    for (int input = 0; input < _view.Graph.BaseMixer.GetInputCount(); input++)
                    {
                        float weight = _view.Graph.BaseMixer.GetInputWeight(input);
                        Assert.That(weight, Is.InRange(0f, 1f)); total += weight;
                    }
                    Assert.AreEqual(1f, total, .001f);
                }
                Assert.Greater(WeightOf(directions[direction]), .95f);
                Assert.Less(WeightOf(swimming[1]), .001f);
                Assert.AreEqual(Vector3.zero, _actor.transform.position);
            }
        }

        [Test]
        public void RunningDirectionsUseRunClipsAndNormalizeSpeedChanges()
        {
            _view.Dispose();
            var cardinal = new[] { "Walk Left", "Walk Right", "Walk Backward", "Crouch-Walk-Left", "Crouch-Walk-Right", "Crouch-Walk-Backward" }.Select(LoadClip).ToArray();
            var diagonals = new[] { "Walk ForwardLeft", "Walk ForwardRight", "Walk BackwardLeft", "Walk BackwardRight" }.Select(LoadClip).ToArray();
            var runs = new[] { "Left", "Right", "Backward", "ForwardLeft", "ForwardRight", "BackwardLeft", "BackwardRight" }.Select(n => LoadClip("Run " + n)).ToArray();
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("Walk Forward"),
                LoadClip("Run Forward"), directional: cardinal, walkDiagonals: diagonals, runDirectional: runs);
            var directions = new[] { Vector3.left, Vector3.right, Vector3.back, new Vector3(-1,0,1),
                new Vector3(1,0,1), new Vector3(-1,0,-1), new Vector3(1,0,-1) };
            for (int i = 0; i < directions.Length; i++)
            {
                for (int frame = 0; frame < 80; frame++)
                {
                    _view.Tick(directions[i].normalized * 4f, Vector3.up, null, null, .02f);
                    AssertNormalizedWeights();
                }
                Assert.Greater(WeightOf(runs[i]), .99f);
                foreach (var walk in cardinal.Take(3).Concat(diagonals)) Assert.Less(WeightOf(walk), .001f);
            }
            for (int frame = 0; frame < 80; frame++)
            {
                _view.Tick(Vector3.right * 1.4f, Vector3.up, null, null, .02f);
                AssertNormalizedWeights();
            }
            Assert.Greater(WeightOf(cardinal[1]), .99f);
            foreach (var run in runs) Assert.Less(WeightOf(run), .001f);
        }

        [Test]
        public void DedicatedDiagonalsReplaceCardinalMixturesAndNormalizeReversals()
        {
            _view.Dispose();
            var cardinal = new[] { "Strafe-Left", "Strafe-Right", "Strafe-Backward", "Crouch-Walk-Left", "Crouch-Walk-Right", "Crouch-Walk-Backward" }.Select(LoadClip).ToArray();
            var diagonals = new[] { "Walk ForwardLeft", "Walk ForwardRight", "Walk BackwardLeft", "Walk BackwardRight" }.Select(LoadClip).ToArray();
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("HumanoidWalk"),
                LoadClip("HumanoidRun"), directional: cardinal, walkDiagonals: diagonals);
            var velocities = new[] { new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f) };
            foreach (int direction in new[] { 0, 3, 1, 2 })
            {
                for (int frame = 0; frame < 70; frame++)
                {
                    _view.Tick(velocities[direction].normalized * 1.4f, Vector3.up, null, null, .02f);
                    AssertNormalizedWeights();
                }
                Assert.Greater(WeightOf(diagonals[direction]), .99f);
                for (int i = 0; i < 3; i++) Assert.Less(WeightOf(cardinal[i]), .001f);
                Assert.Less(WeightOf(LoadClip("HumanoidWalk")), .001f);
            }
        }

        [Test]
        public void FastSurfaceStrokeFadesAndPreservesSidewaysAndUnderwaterMotions()
        {
            _view.Dispose();
            var idle = LoadClip("Surface Idle");
            var swimming = new[] { idle, idle, LoadClip("Underwater Idle"), LoadClip("Underwater Swim") };
            var directions = new[] { "Swim Left", "Swim Right", "Swim Backward" }.Select(LoadClip).ToArray();
            var fast = LoadClip("Surface Paddle");
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("HumanoidWalk"),
                LoadClip("HumanoidRun"), swimming: swimming, swimDirectional: directions, fastSurfaceSwim: fast);
            foreach (bool fastRequested in new[] { false, true, false, true })
            {
                for (int frame = 0; frame < 70; frame++)
                {
                    _view.Tick(Vector3.forward * 2f, Vector3.up, null, null, .02f, swimming: true, fastSwimming: fastRequested);
                    AssertNormalizedWeights();
                }
                Assert.AreEqual(fastRequested ? 1f : 0f, WeightOf(fast), .001f);
            }
            for (int frame = 0; frame < 70; frame++)
            {
                _view.Tick(Vector3.left * 2f, Vector3.up, null, null, .02f, swimming: true, fastSwimming: true);
                AssertNormalizedWeights();
            }
            Assert.Greater(WeightOf(directions[0]), .99f);
            Assert.Less(WeightOf(fast), .001f);
            for (int frame = 0; frame < 70; frame++)
            {
                _view.Tick(Vector3.forward * 2f, Vector3.up, null, null, .02f, swimming: true, diving: true, fastSwimming: true);
                AssertNormalizedWeights();
            }
            Assert.Less(WeightOf(fast), .001f);
            Assert.Greater(WeightOf(swimming[3]), .99f);
        }

        void AssertNormalizedWeights()
        {
            float total = 0f;
            for (int i = 0; i < _view.Graph.BaseMixer.GetInputCount(); i++)
            {
                float weight = _view.Graph.BaseMixer.GetInputWeight(i);
                Assert.That(weight, Is.InRange(0f, 1f));
                total += weight;
            }
            Assert.AreEqual(1f, total, .001f);
        }

        [Test]
        public void CrawlTransitionsPlayInBothDirectionsAndBackwardMotionHasItsOwnClip()
        {
            _view.Dispose();
            var traversal = new[] { "Crouch Idle", "Crouch Walk", "Basic Crawl Idle", "Basic Crawl Forward", "Jump",
                "Vault", "Ledge Hang", "Basic Pull Up", "Step Up" }.Select(LoadClip).ToArray();
            var transitions = new[] { "Crawl Enter", "Crawl Exit" }.Select(LoadClip).ToArray();
            var backward = LoadClip("Crawl Backward");
            var sideways = new[] { "Crawl Left", "Crawl Right" }
                .Select(name => AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Animations/" + name + ".anim")).ToArray();
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("HumanoidWalk"),
                LoadClip("HumanoidRun"), traversal: traversal, crawlTransitions: transitions, crawlBackward: backward, crawlSideways: sideways);
            _view.Pose.FeetEnabled = false;
            _view.Pose.LookEnabled = false;
            for (int frame = 0; frame < 30; frame++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            float standingHead = head.position.y;
            _view.SetLocomotionState(ActorStance.Crawling, true, TraversalKind.None, 0f);
            Assert.IsTrue(_view.CrawlTransitionActive);
            for (int frame = 0; frame < 20; frame++)
            {
                _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
                AssertNormalizedWeights();
                if (frame == 0) Assert.Less(Mathf.Abs(head.position.y - standingHead), .08f, "Entry must start near the standing pose.");
                if (frame < 6) Assert.Less(WeightOf(traversal[2]), .0001f, "Prone idle must not leak into the entry fade.");
            }
            Assert.Greater(_view.CrawlTransitionWeight, .95f);
            float[] before = Enumerable.Range(0, _view.Graph.BaseMixer.GetInputCount()).Select(i => _view.Graph.BaseMixer.GetInputWeight(i)).ToArray();
            _view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.None, 0f);
            _view.Tick(Vector3.zero, Vector3.up, null, null, 0f);
            for (int i = 0; i < before.Length; i++) Assert.AreEqual(before[i], _view.Graph.BaseMixer.GetInputWeight(i), .0001f);
            for (int frame = 0; frame < 120; frame++)
            {
                _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
                AssertNormalizedWeights();
            }
            Assert.IsFalse(_view.CrawlTransitionActive);
            Assert.AreEqual(0f, _view.CrawlTransitionWeight);
            _view.SetLocomotionState(ActorStance.Crawling, true, TraversalKind.None, 0f);
            for (int frame = 0; frame < 120; frame++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.IsFalse(_view.CrawlTransitionActive);
            _view.SetLocomotionState(ActorStance.Crouching, true, TraversalKind.None, 0f);
            float highestCrouchExit = head.position.y;
            for (int frame = 0; frame < 160; frame++)
            {
                _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
                highestCrouchExit = Mathf.Max(highestCrouchExit, head.position.y);
                AssertNormalizedWeights();
            }
            Assert.IsFalse(_view.CrawlTransitionActive);
            Assert.Less(highestCrouchExit, standingHead - .15f, "Prone-to-crouch must not pass through standing.");
            _view.SetLocomotionState(ActorStance.Crawling, true, TraversalKind.None, 0f);
            for (int frame = 0; frame < 120; frame++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            for (int frame = 0; frame < 70; frame++)
            {
                _view.Tick(Vector3.back * .55f, Vector3.up, null, null, .02f);
                AssertNormalizedWeights();
            }
            Assert.Greater(WeightOf(backward), .99f);
            Assert.Less(WeightOf(traversal[3]), .001f);
            foreach (int side in new[] { 0, 1 })
            {
                for (int frame = 0; frame < 70; frame++)
                {
                    _view.Tick((side == 0 ? Vector3.left : Vector3.right) * .55f, Vector3.up, null, null, .02f);
                    AssertNormalizedWeights();
                }
                Assert.Greater(WeightOf(sideways[side]), .99f);
                Assert.Less(WeightOf(traversal[3]), .001f);
                Assert.Less(WeightOf(backward), .001f);
            }
            Assert.AreEqual(Vector3.zero, _actor.transform.position);
        }

        [Test]
        public void CrawlClockUsesTravelDistanceAndStopsAtRest()
        {
            _view.Dispose();
            var traversal = new[] { "Crouch Idle", "Crouch Walk", "Basic Crawl Idle", "Basic Crawl Forward", "Jump",
                "Vault", "Ledge Hang", "Basic Pull Up", "Step Up" }.Select(LoadClip).ToArray();
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("HumanoidWalk"),
                LoadClip("HumanoidRun"), traversal: traversal, crawlCycleDistances: Vector4.one * .9f);
            _view.SetLocomotionState(ActorStance.Crawling, true, TraversalKind.None, 0f);
            var crawl = _view.Graph.BaseMixer.GetInput(6);
            for (int i = 0; i < 50; i++) _view.Tick(Vector3.forward * .45f, Vector3.up, null, null, .02f);
            Assert.AreEqual(traversal[3].length * .5f, crawl.GetTime(), .0001d);
            for (int i = 0; i < 50; i++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.AreEqual(traversal[3].length * .5f, crawl.GetTime(), .0001d);
        }

        [Test]
        public void CrawlingKeepsLimbsAboveSlopedGroundWithoutStretching()
        {
            _view.Dispose();
            var traversal = new[] { "Crouch Idle", "Crouch Walk", "Basic Crawl Idle", "Basic Crawl Forward", "Jump",
                "Vault", "Ledge Hang", "Basic Pull Up", "Step Up" }.Select(LoadClip).ToArray();
            var sideways = new[] { "Crawl Left", "Crawl Right" }.Select(name =>
                AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Animations/" + name + ".anim")).ToArray();
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("HumanoidWalk"),
                LoadClip("HumanoidRun"), traversal: traversal, crawlBackward: LoadClip("Crawl Backward"), crawlSideways: sideways);
            var bones = new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightHand, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }.Select(_animator.GetBoneTransform).ToArray();
            var lengths = new float[bones.Length];
            var ground = new CrawlSlope();
            _view.SetLocomotionState(ActorStance.Crawling, true, TraversalKind.None, 0f);
            foreach (var direction in new[] { Vector3.zero, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
            {
                for (int frame = 0; frame < 150; frame++)
                {
                    _view.Tick(direction * .55f, Vector3.up, null, ground, .02f);
                    if (frame < 50) continue;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Assert.Greater(bones[i].position.y - CrawlSlope.Height(bones[i].position), .035f, bones[i].name);
                        lengths[i] = bones[i].localPosition.magnitude;
                    }
                    Assert.AreEqual(Vector3.zero, _actor.transform.position);
                    _view.Pose.RestoreAnimation();
                    for (int i = 0; i < bones.Length; i++)
                        Assert.AreEqual(lengths[i], bones[i].localPosition.magnitude, .0001f, "Ground support must not stretch sampled limbs.");
                }
            }
        }

        sealed class CrawlSlope : IGroundingProvider
        {
            public static float Height(Vector3 point) => point.z * .08f + point.x * .03f;
            public bool TryGround(Vector3 point, Vector3 down, float offset, out GroundResult result)
            {
                result = new GroundResult(new Vector3(point.x, Height(point) + offset, point.z), new Vector3(-.03f, 1f, -.08f).normalized);
                return true;
            }
        }

        [Test]
        public void CrawlTransitionEndpointsMatchIdleAndExitReturnsUpright()
        {
            _view.Dispose();
            _view = null;
            var enter = LoadClip("Crawl Enter");
            var exit = LoadClip("Crawl Exit");
            var idle = LoadClip("Basic Crawl Idle");
            var bones = new[] { HumanBodyBones.Head, HumanBodyBones.Hips, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
            var graph = PlayableGraph.Create("Crawl endpoint import regression");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                _animator.applyRootMotion = false;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var output = AnimationPlayableOutput.Create(graph, "Crawl endpoints", _animator);
                graph.Play();
                Vector3[] Sample(AnimationClip clip, double time)
                {
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetSpeed(0d);
                    output.SetSourcePlayable(playable);
                    playable.SetTime(time);
                    graph.Evaluate(0f);
                    var positions = bones.Select(bone => _actor.transform.InverseTransformPoint(_animator.GetBoneTransform(bone).position)).ToArray();
                    output.SetSourcePlayable(Playable.Null);
                    playable.Destroy();
                    return positions;
                }
                var idleStart = Sample(idle, 0d);
                var enterEnd = Sample(enter, enter.length);
                var exitStart = Sample(exit, 0d);
                for (int i = 0; i < bones.Length; i++)
                {
                    Assert.Less(Vector3.Distance(idleStart[i], enterEnd[i]), .04f, "Enter endpoint: " + bones[i]);
                    Assert.Less(Vector3.Distance(idleStart[i], exitStart[i]), .04f, "Exit start: " + bones[i]);
                }
                var exitEnd = Sample(exit, exit.length);
                Assert.Greater(exitEnd[0].y, 1.4f, "Exit must return the head to standing height.");
                Assert.Greater(exitEnd[1].y, .75f, "Exit must return the pelvis above the ground.");
                Assert.AreEqual(Vector3.zero, _actor.transform.position);
            }
            finally { graph.Destroy(); }
        }

        [Test]
        public void WadingRequiresGroundedWaterAndFadesOnSwimOrAir()
        {
            _view.Dispose();
            var swimming = new[] { "Surface Idle", "Slow Swim", "Underwater Idle", "Underwater Swim" }.Select(LoadClip).ToArray();
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("HumanoidWalk"),
                LoadClip("HumanoidRun"), swimming: swimming,
                wade: AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Animations/Basic Wade.anim"));
            foreach (bool enterSwimming in new[] { false, true })
            {
                _view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.None, 0f);
                for (int i = 0; i < 50; i++) _view.Tick(Vector3.forward * 2f, Vector3.up, null, null, .02f, waterDepth: .6f);
                Assert.Greater(_view.WadeWeight, .9f);
                _view.SetLocomotionState(ActorStance.Standing, false, TraversalKind.None, 0f);
                _view.Tick(Vector3.forward * 2f, Vector3.up, null, null, .02f, swimming: enterSwimming, waterDepth: .6f);
                Assert.Greater(_view.WadeWeight, 0f, "Wading must fade rather than disappear.");
                for (int i = 0; i < 30; i++) _view.Tick(Vector3.forward * 2f, Vector3.up, null, null, .02f,
                    swimming: enterSwimming, waterDepth: .6f);
                Assert.AreEqual(0f, _view.WadeWeight);
            }
            _view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.None, 0f);
            for (int i = 0; i < 30; i++) _view.Tick(Vector3.forward * 2f, Vector3.up, null, null, .02f, waterDepth: .05f);
            Assert.AreEqual(0f, _view.WadeWeight);
        }

        [Test]
        public void ReleasedHangStopsAnchoringHandsAndBodyToTheOldEdge()
        {
            _view.Dispose();
            var traversal = new[] { "Crouch Idle", "Crouch Walk", "Crawl Idle", "Crawl Forward", "Jump",
                "Vault", "Ledge Hang", "Basic Pull Up", "Step Up" }.Select(LoadClip).ToArray();
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("HumanoidWalk"),
                LoadClip("HumanoidRun"), traversal: traversal);
            _view.SetLocomotionState(ActorStance.Standing, false, TraversalKind.Hanging, 1f, new Vector3(0f, 1.4f, .4f));
            for (int i = 0; i < 30; i++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.Greater(_view.TraversalHandWeight, .95f);
            _view.SetLocomotionState(ActorStance.Standing, false, TraversalKind.None, 0f);
            for (int i = 0; i < 10; i++)
            {
                _actor.transform.position += Vector3.down * .04f;
                _view.Tick(Vector3.down * 2f, Vector3.up, null, null, .02f);
            }
            Assert.AreEqual(0f, _view.TraversalHandWeight);
            float hips = _animator.GetBoneTransform(HumanBodyBones.Hips).position.y;
            for (int i = 0; i < 10; i++)
            {
                _actor.transform.position += Vector3.down * .04f;
                _view.Tick(Vector3.down * 2f, Vector3.up, null, null, .02f);
            }
            Assert.Less(_animator.GetBoneTransform(HumanBodyBones.Hips).position.y, hips - .3f);
            Assert.AreEqual(-.8f, _actor.transform.position.y, .001f);
        }

        [Test]
        public void ProfiledVaultConsumesPlanarMotionOnceThroughCompletionFade()
        {
            _view.Dispose();
            var asset = AssetDatabase.LoadAssetAtPath<ActorTraversalMotionAsset>(Folder + "/Motion/Basic Vault Motion.asset");
            Assert.IsNotNull(asset);
            var motion = asset.CreateMotion();
            var traversal = new[] { "Crouch Idle", "Crouch Walk", "Basic Crawl Idle", "Basic Crawl Forward", "Jump",
                "Vault", "Ledge Hang", "Basic Pull Up", "Step Up" }.Select(LoadClip).ToArray();
            traversal[5] = asset.Clip;
            _view = new HumanoidAnimationView(_animator, _rig, LoadClip("HumanoidIdle"), LoadClip("HumanoidWalk"),
                LoadClip("HumanoidRun"), traversal: traversal, vaultMotion: motion, vaultContact: asset.SupportContact);
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            // The authority applies the root trajectory. Presentation must remove that same displacement from the clip.
            foreach (float phase in new[] { .5f, .8f, 1f })
            {
                Vector3 authorityPosition = motion.Sample(phase).Root;
                _actor.transform.position = authorityPosition;
                _view.SetLocomotionState(ActorStance.Standing, false, TraversalKind.Vault, phase);
                for (int i = 0; i < 30; i++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
                Assert.AreEqual(authorityPosition, _actor.transform.position, "Presentation must not move the authority root.");
                Vector3 localHips = _actor.transform.InverseTransformPoint(hips.position);
                Assert.Less(new Vector2(localHips.x, localHips.z).magnitude, .2f,
                    "The clip displacement must not be added a second time at phase " + phase);
            }
            Vector3 landing = _actor.transform.position;
            _view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.None, 1f);
            for (int i = 0; i < 40; i++)
            {
                _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
                Assert.AreEqual(landing, _actor.transform.position);
                Vector3 localHips = _actor.transform.InverseTransformPoint(hips.position);
                Assert.Less(new Vector2(localHips.x, localHips.z).magnitude, .2f,
                    "Outgoing vault motion must remain removed while its weight fades.");
            }
            Assert.Less(WeightOf(asset.Clip), .001f);
        }

        static AnimationClip LoadClip(string name) => AssetDatabase.LoadAllAssetsAtPath(Folder + "/Animations/" + name + ".fbx")
            .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

        float WeightOf(AnimationClip clip)
        {
            for (int i = 0; i < _view.Graph.BaseMixer.GetInputCount(); i++)
                if (((AnimationClipPlayable)_view.Graph.BaseMixer.GetInput(i)).GetAnimationClip() == clip)
                    return _view.Graph.BaseMixer.GetInputWeight(i);
            Assert.Fail("Clip was not bound to the graph: " + clip.name); return 0f;
        }

        [Test]
        public void DirectionalClipsBlendWithoutMovingAuthorityRoot()
        {
            _view.Dispose();
            AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(Folder + "/Animations/" + name + ".fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var directions = new[] { Clip("Strafe-Left"), Clip("Strafe-Right"), Clip("Strafe-Backward"),
                Clip("Crouch-Walk-Left"), Clip("Crouch-Walk-Right"), Clip("Crouch-Walk-Backward") };
            foreach (var clip in directions) Assert.IsEmpty(clip.events);
            _view = new HumanoidAnimationView(_animator, _rig, Clip("HumanoidIdle"), Clip("HumanoidWalk"), Clip("HumanoidRun"), directional: directions);
            foreach (var direction in new[] { Vector3.left, Vector3.right, Vector3.back, new Vector3(1f, 0f, 1f),
                new Vector3(-.012867f, 0f, -1.558586f), new Vector3(.012867f, 0f, -1.558586f) })
            {
                for (int i = 0; i < 60; i++)
                {
                    // Ground support adds small lateral displacements even during straight backward movement.
                    Vector3 velocity = direction * 1.4f;
                    if (direction.z < 0f) velocity.x += Mathf.Sin(i) * .0001f;
                    _view.Tick(velocity, Vector3.up, null, null, .02f);
                    float total = 0f;
                    for (int j = 0; j < _view.Graph.BaseMixer.GetInputCount(); j++)
                    {
                        float weight = _view.Graph.BaseMixer.GetInputWeight(j);
                        Assert.That(weight, Is.InRange(0f, 1f));
                        total += weight;
                    }
                    Assert.AreEqual(1f, total, .001f, "Every sampled blend must remain normalized.");
                    if (direction.z < 0f && i == 59)
                    {
                        Assert.AreEqual(0f, _view.Graph.BaseMixer.GetInputWeight(1), .00001f);
                        Assert.AreEqual(0f, _view.Graph.BaseMixer.GetInputWeight(2), .00001f);
                    }
                }
                Assert.Less(_actor.transform.position.magnitude, .001f);
                Assert.Less(Quaternion.Angle(Quaternion.identity, _actor.transform.rotation), .01f);
                float leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y;
                float rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y;
                Assert.Less(Mathf.Min(leftFoot, rightFoot), .3f, "Directional clips must not retain the source stage height.");
                float sum = 0f;
                for (int i = 0; i < _view.Graph.BaseMixer.GetInputCount(); i++) sum += _view.Graph.BaseMixer.GetInputWeight(i);
                Assert.AreEqual(1f, sum, .001f);
            }

            _view.Reset();
            for (int i = 0; i < 60; i++) _view.Tick(Vector3.forward * 1.4f, Vector3.up, null, null, .02f);
            _view.Tick(Vector3.back * 1.4f, Vector3.up, null, null, .02f);
            Assert.That(_view.Graph.BaseMixer.GetInputWeight(1), Is.InRange(.7f, .99f), "Reversal must retain the outgoing pose.");
            Assert.That(_view.Graph.BaseMixer.GetInputWeight(5), Is.InRange(.01f, .3f), "Reversal must introduce the incoming pose.");
            for (int i = 0; i < 60; i++) _view.Tick(Vector3.back * 1.4f, Vector3.up, null, null, .02f);
            float forwardWeight = _view.Graph.BaseMixer.GetInputWeight(1);
            _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.AreEqual(forwardWeight, _view.Graph.BaseMixer.GetInputWeight(1) / (1f - _view.Graph.BaseMixer.GetInputWeight(0)), .001f,
                "Stopping must retain the direction blend while locomotion fades to idle.");
            for (int i = 0; i < 15; i++) _view.Tick(Vector3.back * 1.4f, Vector3.up, null, null, .02f);
            Assert.Greater(_view.Graph.BaseMixer.GetInputWeight(5), .97f);
        }

        [Test]
        public void IdleToWalkRetainsIdlePoseDuringFirstMovementFrame()
        {
            _view.Pose.FeetEnabled = false;
            for (int i = 0; i < 40; i++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            var hip = _animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 before = hip.localPosition;
            _view.Tick(Vector3.forward * 1.4f, Vector3.up, null, null, .02f);
            Assert.That(_view.Graph.BaseMixer.GetInputWeight(0), Is.InRange(.7f, .99f));
            Assert.Less(Vector3.Distance(before, hip.localPosition), .035f);
            for (int i = 0; i < 30; i++) _view.Tick(Vector3.forward * 1.4f, Vector3.up, null, null, .02f);
            Assert.Less(_view.Graph.BaseMixer.GetInputWeight(0), .01f);
        }

        [Test]
        public void ShortDropsKeepArmsNeutralWhileLongFallsIntroduceFallClip()
        {
            _view.Dispose();
            AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(Folder + "/Animations/" + name + ".fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            _view = new HumanoidAnimationView(_animator, _rig, Clip("HumanoidIdle"), Clip("HumanoidWalk"), Clip("HumanoidRun"), fall: Clip("Fall"));
            _view.SetLocomotionState(ActorStance.Standing, false, TraversalKind.None, 0f);
            for (int i = 0; i < 10; i++) _view.Tick(Vector3.down, Vector3.up, null, null, .02f);
            Assert.AreEqual(0f, _view.FallWeight, "A 20 cm drop must not select the arms-up fall pose.");
            for (int i = 0; i < 30; i++) _view.Tick(Vector3.down * 3f, Vector3.up, null, null, .02f);
            Assert.Greater(_view.FallWeight, .95f);
            _view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.None, 0f);
            _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.That(_view.Graph.BaseMixer.GetInputWeight(3), Is.InRange(.5f, 1f), "Landing must fade out the fall pose.");
        }

        [Test]
        public void StairRiseIsAbsorbedByPresentationWithoutMovingAuthorityRoot()
        {
            var ground = new FlatGround();
            _view.Pose.FeetEnabled = false;
            for (int i = 0; i < 30; i++) _view.Tick(Vector3.zero, Vector3.up, null, ground, .02f);
            var hip = _animator.GetBoneTransform(HumanBodyBones.Hips);
            float before = hip.position.y;
            _actor.transform.position += Vector3.up * .2f;
            _view.Tick(Vector3.up * 10f, Vector3.up, null, ground, .02f);
            Assert.AreEqual(.2f, _actor.transform.position.y, .0001f);
            Assert.Less(hip.position.y - before, .07f);
        }

        sealed class FlatGround : IGroundingProvider
        {
            public bool TryGround(Vector3 position, Vector3 down, float offset, out GroundResult result)
            { result = new GroundResult(new Vector3(position.x, offset, position.z), Vector3.up); return true; }
        }

        [Test]
        public void BasicTraversalClipsNeverInvertTheCharacter()
        {
            _view.Dispose();
            AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(Folder + "/Animations/" + name + ".fbx")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var traversal = new[] { "Crouch Idle", "Crouch Walk", "Crawl Idle", "Crawl Forward", "Jump",
                "Vault", "Ledge Hang", "Basic Pull Up", "Step Up" }.Select(Clip).ToArray();
            _view = new HumanoidAnimationView(_animator, _rig, Clip("HumanoidIdle"), Clip("HumanoidWalk"), Clip("HumanoidRun"), traversal: traversal);
            foreach (var kind in new[] { TraversalKind.StepUp, TraversalKind.Vault, TraversalKind.ClimbUp })
            {
                _view.Reset();
                for (int i = 0; i < 80; i++)
                {
                    _view.SetLocomotionState(ActorStance.Standing, true, kind, Mathf.Clamp01((i - 10) / 60f));
                    _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
                    Vector3 torso = _animator.GetBoneTransform(HumanBodyBones.Head).position - _animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    Assert.Greater(Vector3.Dot(torso.normalized, Vector3.up), 0f, kind + " must not use a flip as its basic animation.");
                    Assert.Less(_actor.transform.position.magnitude, .001f);
                }
            }
            _view.Reset();
            Vector3 edge = new(0f, 1.4f, .4f);
            for (int i = 0; i < 20; i++)
            {
                _view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.Hanging, 1f, edge);
                _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            }
            for (int i = 0; i <= 80; i++)
            {
                _view.SetLocomotionState(ActorStance.Standing, true, TraversalKind.ClimbUp, i / 80f, edge);
                _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
                if (i <= 30)
                {
                    Assert.Less(Vector3.Distance(_animator.GetBoneTransform(HumanBodyBones.LeftHand).position, edge - Vector3.right * .22f), .01f);
                    Assert.Less(Vector3.Distance(_animator.GetBoneTransform(HumanBodyBones.RightHand).position, edge + Vector3.right * .22f), .01f);
                }
                if (i >= 70) Assert.AreEqual(0f, _view.TraversalHandWeight);
                Assert.Less(_actor.transform.position.magnitude, .001f);
            }
        }

        [Test]
        public void ReachPreservesArmLengthsAndReleaseReturnsToSampledPose()
        {
            _view.Pose.LookInfluence = 0f;
            _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            var arm = _rig.Interactions.Single(x => x.Id == "RightHand").Bones;
            float first = Vector3.Distance(arm[0].position, arm[1].position), second = Vector3.Distance(arm[1].position, arm[2].position);
            Vector3 target = Vector3.Lerp(arm[0].position, arm[2].position, .8f) + Vector3.forward * .1f;
            _view.Pose.SetInteractionTarget("RightHand", new InteractionPoseTarget(target, null, 1f));
            for (int i = 0; i < 30; i++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.Less(Vector3.Distance(target, arm[2].position), .035f);
            Assert.AreEqual(first, Vector3.Distance(arm[0].position, arm[1].position), .001f);
            Assert.AreEqual(second, Vector3.Distance(arm[1].position, arm[2].position), .001f);
            _view.Pose.SetInteractionTarget("RightHand", null);
            Vector3 heldPosition = arm[2].position;
            _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.Less(Vector3.Distance(heldPosition, arm[2].position), .03f,
                "The first release frame must retain the outgoing reach pose.");
            for (int i = 0; i < 30; i++) _view.Tick(Vector3.zero, Vector3.up, null, null, .02f);
            Assert.Greater(Vector3.Distance(target, arm[2].position), .03f);
        }
    }
}
