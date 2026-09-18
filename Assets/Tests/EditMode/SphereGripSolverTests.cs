using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class SphereGripSolverTests
    {
        GameObject _root;
        Transform[] _bones;
        SphereGripSolver _solver;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Wrist");
            _bones = new Transform[3];
            var joints = new InteractionContactJoint[3];
            for (int i = 0; i < 3; i++)
            {
                _bones[i] = new GameObject("Joint").transform;
                _bones[i].SetParent(i == 0 ? _root.transform : _bones[i - 1], false);
                _bones[i].localPosition = Vector3.right * .03f;
                joints[i] = new InteractionContactJoint { Bone = _bones[i], LocalRotation = Quaternion.identity, MaxCorrectionDegrees = 90f };
            }
            _solver = new SphereGripSolver(_root.transform, joints, Quaternion.LookRotation(Vector3.up, Vector3.right));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void CurlKeepsSegmentsOutsideSphereAndPreservesLengths()
        {
            _solver.Solve(new Vector3(.07f, .045f, 0f), .025f, 1f);
            Assert.LessOrEqual(_solver.MaxPenetration, .0001f);
            for (int i = 0; i < _bones.Length; i++)
            {
                Assert.AreEqual(.03f, _bones[i].localPosition.magnitude, .00001f);
                Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, _bones[i].localRotation), 90.01f);
            }
        }

        [Test]
        public void CollisionChecksSegmentInteriorEvenWhenEndsAreOutside()
        {
            _solver.Solve(new Vector3(.045f, 0f, 0f), .006f, 0f);
            Assert.Greater(_solver.MaxPenetration, .01f);
            foreach (var bone in _bones) Assert.Less(Quaternion.Angle(Quaternion.identity, bone.localRotation), .001f);
        }
    }
}
