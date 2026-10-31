using UnityEngine;

namespace RoadToValor.Game
{
    public enum RigKind { Soldier, Vehicle, Aircraft, Structure }

    /// <summary>
    /// Named transforms of a unit model that animators drive. Procedural placeholders fill all of them;
    /// a real prefab (Resources/RoadToValor/Models/U{id:00}) only needs children named
    /// "Body", "Turret", "Barrel", "Muzzle" (optional) plus an Animator.
    /// </summary>
    public sealed class UnitRig : MonoBehaviour
    {
        public RigKind Kind;
        public Transform Body;
        public Transform Torso;
        public Transform Arms;
        public Transform LegL;
        public Transform LegR;
        public Transform Turret;
        public Transform Barrel;
        public Transform Muzzle;
        public Transform Extra;     // mortar on the ground, parachute, pump-jack beam...
        public Transform Extra2;    // carried mortar tube, etc.
        public Transform[] Spinners = new Transform[0];
        public Vector3[] SpinAxes = new Vector3[0];
        public float[] SpinSpeeds = new float[0];
        public Transform[] Wheels = new Transform[0];
        public float WheelRadius = 0.45f;
        public Transform TreadL;
        public Transform TreadR;
        public float TreadSpacing = 0.36f;
        public bool Tracked;
        public float Height = 2f;
        public float BarrelRestZ;
        public Animator Animator;   // set when a real prefab with Animator is used
        public Renderer[] Renderers = new Renderer[0];
        public bool IsPrefab;
    }
}
