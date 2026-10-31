using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Animation states. Soldiers: Idle..Victory (+ Work for medics/engineers/mine clearers).
    /// Vehicles: Idle/Move(Walk,Run)/Aim(turret)/Fire/Hit/Death(destroyed).
    /// Aircraft: Fly(Walk/Run)/Aim/Fire/Death(crash)/Exit. Buildings: Construct/Idle/Fire/Damaged/Death.
    /// The int values are what MecanimAnimatorDriver writes to the Animator "State" parameter.
    /// </summary>
    public enum UnitAnimState
    {
        Idle = 0, Walk = 1, Run = 2, Aim = 3, Fire = 4, Reload = 5, Hit = 6, Death = 7, Ability = 8, Victory = 9,
        Work = 10, Construct = 11, Damaged = 12, Dropping = 13, Exit = 14,
    }

    /// <summary>Per-frame context the animators need.</summary>
    public struct AnimContext
    {
        public float Speed;          // m/s
        public float MaxSpeed;
        public float TurretYawLocal; // degrees relative to body
        public float StateTime;      // seconds since state entered
        public float Dt;
        public float Time;
        public float HpRatio;
        public float BuildProgress;
        public float DeathTime;      // seconds since death
        public bool FiredThisFrame;
        public float Distance;       // track/wheel distance travelled this frame
        public AbilityType Ability;
        public bool IsMoving => Speed > 0.25f;
    }

    /// <summary>
    /// Pure state selection from simulation data. Priority: Death > Victory > Dropping > Construct > Fire > Hit > Reload >
    /// Work/Ability > Run/Walk > Aim > Damaged > Idle.
    /// </summary>
    public static class UnitAnimStateMachine
    {
        public static UnitAnimState Resolve(SimUnit u, float simTime, float speed)
        {
            if (!u.Alive) return UnitAnimState.Death;
            if (u.Exiting) return UnitAnimState.Exit;
            switch (u.Activity)
            {
                case UnitActivity.Victory: return UnitAnimState.Victory;
                case UnitActivity.Dropping: return UnitAnimState.Dropping;
                case UnitActivity.Constructing: return UnitAnimState.Construct;
            }
            if (simTime - u.FireFlashTime < 0.16f) return UnitAnimState.Fire;
            if (simTime - u.LastHitTime < 0.22f && u.IsInfantry) return UnitAnimState.Hit;
            if (u.ReloadTimer > 0f) return u.IsInfantry ? UnitAnimState.Reload : UnitAnimState.Aim;
            if (u.Activity == UnitActivity.Ability)
            {
                switch (u.Def.Ability)
                {
                    case AbilityType.Heal:
                    case AbilityType.Repair:
                    case AbilityType.MineSweep:
                    case AbilityType.LayMines:
                        return UnitAnimState.Work;
                    default:
                        return UnitAnimState.Ability;
                }
            }
            if (speed > 0.25f)
            {
                float max = Mathf.Max(0.1f, u.Def.MoveSpeed);
                return speed > max * 0.95f || u.ChargeTimer > 0f ? UnitAnimState.Run : UnitAnimState.Walk;
            }
            if (u.Activity == UnitActivity.Aiming || u.TargetId != 0) return UnitAnimState.Aim;
            if (u.IsStructure && u.HpRatio < 0.5f) return UnitAnimState.Damaged;
            return UnitAnimState.Idle;
        }
    }

    public interface IUnitAnimator
    {
        void OnStateEnter(UnitAnimState state);
        void Animate(UnitAnimState state, ref AnimContext ctx);
    }

    // ------------------------------------------------------------------ procedural (placeholder) animators

    public sealed class SoldierAnimator : IUnitAnimator
    {
        private readonly UnitRig _r;
        private float _phase;
        private float _recoil;
        private readonly float _seed;

        public SoldierAnimator(UnitRig rig) { _r = rig; _seed = Random.value * 10f; }

        public void OnStateEnter(UnitAnimState s)
        {
            if (s == UnitAnimState.Fire) _recoil = 1f;
            if (_r.Extra != null && _r.Extra.name == "Parachute") _r.Extra.gameObject.SetActive(s == UnitAnimState.Dropping);
        }

        public void Animate(UnitAnimState s, ref AnimContext c)
        {
            float t = c.Time + _seed;
            float legSwing = 0f, armPitch = 25f, torsoPitch = 0f, bodyY = 0f, bodyPitch = 0f, bodyRoll = 0f, armYaw = 0f, armRoll = 0f;
            float stride = 0f;
            switch (s)
            {
                case UnitAnimState.Idle:
                    torsoPitch = Mathf.Sin(t * 1.6f) * 1.5f; // breathing
                    armPitch = 28f + Mathf.Sin(t * 1.6f) * 2f;
                    break;
                case UnitAnimState.Walk:
                case UnitAnimState.Run:
                {
                    bool run = s == UnitAnimState.Run;
                    stride = run ? 7.5f : 5.2f;
                    _phase += c.Speed / Mathf.Max(0.5f, run ? 1.9f : 1.4f) * Mathf.PI * c.Dt;
                    legSwing = Mathf.Sin(_phase) * (run ? 42f : 28f);
                    bodyY = Mathf.Abs(Mathf.Cos(_phase)) * (run ? 0.07f : 0.035f);
                    torsoPitch = run ? 14f : 4f;
                    armPitch = run ? 38f : 30f;
                    armYaw = Mathf.Sin(_phase) * (run ? 8f : 4f);
                    break;
                }
                case UnitAnimState.Aim:
                    armPitch = 0f;
                    torsoPitch = 2f;
                    legSwing = 8f; // staggered stance
                    break;
                case UnitAnimState.Fire:
                    armPitch = -2f;
                    torsoPitch = 2f;
                    legSwing = 8f;
                    break;
                case UnitAnimState.Reload:
                    armPitch = 48f;
                    armRoll = -25f + Mathf.Sin(c.StateTime * 9f) * 6f;
                    torsoPitch = 10f;
                    legSwing = 6f;
                    break;
                case UnitAnimState.Hit:
                    torsoPitch = -16f * (1f - Mathf.Clamp01(c.StateTime / 0.22f));
                    armPitch = 30f;
                    break;
                case UnitAnimState.Death:
                {
                    float k = Mathf.Clamp01(c.DeathTime / 0.7f);
                    k = 1f - (1f - k) * (1f - k);
                    bodyPitch = -88f * k;
                    bodyRoll = 8f * k;
                    torsoPitch = 10f * k;
                    armPitch = 70f * k;
                    legSwing = -10f * k;
                    bodyY = -Mathf.Clamp01((c.DeathTime - 3f) / 2f) * 0.6f; // sink
                    break;
                }
                case UnitAnimState.Ability:
                    armPitch = -55f + Mathf.Sin(c.StateTime * 6f) * 10f; // signalling / throwing / charging gesture
                    torsoPitch = 6f;
                    break;
                case UnitAnimState.Work:
                    // Kneel and work (heal / repair / sweep for mines).
                    bodyY = -0.38f;
                    torsoPitch = 32f;
                    armPitch = 55f + Mathf.Sin(c.StateTime * (c.Ability == AbilityType.Repair ? 12f : 4f)) * (c.Ability == AbilityType.Repair ? 25f : 10f);
                    armYaw = c.Ability == AbilityType.MineSweep ? Mathf.Sin(c.StateTime * 2.5f) * 35f : 0f;
                    legSwing = -70f;
                    break;
                case UnitAnimState.Victory:
                    armPitch = -95f + Mathf.Sin(t * 5f) * 12f;
                    bodyY = Mathf.Abs(Mathf.Sin(t * 4f)) * 0.15f;
                    torsoPitch = -8f;
                    break;
                case UnitAnimState.Dropping:
                    legSwing = Mathf.Sin(t * 3f) * 10f;
                    armPitch = -60f;
                    break;
            }

            _recoil = Mathf.MoveTowards(_recoil, 0f, c.Dt * 8f);
            if (_r.LegL != null) _r.LegL.localRotation = Quaternion.Euler(s == UnitAnimState.Work ? -80f : legSwing, 0, 0);
            if (_r.LegR != null) _r.LegR.localRotation = Quaternion.Euler(s == UnitAnimState.Work ? 10f : -legSwing, 0, 0);
            if (_r.Torso != null) _r.Torso.localRotation = Quaternion.Euler(torsoPitch, 0, 0);
            if (_r.Arms != null)
            {
                _r.Arms.localRotation = Quaternion.Euler(armPitch - _recoil * 6f, armYaw, armRoll);
                _r.Arms.localPosition = new Vector3(0f, 0.47f, 0.06f - _recoil * 0.06f);
            }
            if (_r.Body != null)
            {
                _r.Body.localPosition = new Vector3(0f, bodyY, 0f);
                _r.Body.localRotation = Quaternion.Euler(bodyPitch, 0f, bodyRoll);
            }
            if (_r.Extra != null && _r.Extra.name == "MortarDeployed")
                _r.Extra.gameObject.SetActive(s == UnitAnimState.Aim || s == UnitAnimState.Fire || s == UnitAnimState.Reload);
            _ = stride;
        }
    }

    public sealed class VehicleAnimator : IUnitAnimator
    {
        private readonly UnitRig _r;
        private float _treadOffset;
        private float _recoil;
        private float _turretYaw;

        public VehicleAnimator(UnitRig rig) { _r = rig; }

        public void OnStateEnter(UnitAnimState s)
        {
            if (s == UnitAnimState.Fire) _recoil = 1f;
        }

        public void Animate(UnitAnimState s, ref AnimContext c)
        {
            // Turret rotation follows the simulation's turret yaw.
            if (_r.Turret != null)
            {
                _turretYaw = Mathf.LerpAngle(_turretYaw, c.TurretYawLocal, 1f - Mathf.Exp(-c.Dt * 10f));
                _r.Turret.localRotation = Quaternion.Euler(0f, _turretYaw, 0f);
            }
            // Barrel recoil.
            _recoil = Mathf.MoveTowards(_recoil, 0f, c.Dt * 3f);
            if (_r.Barrel != null)
            {
                var p = _r.Barrel.localPosition;
                p.z = _r.BarrelRestZ - _recoil * _recoil * 0.45f;
                _r.Barrel.localPosition = p;
            }
            // Track / wheel motion.
            if (_r.TreadL != null)
            {
                _treadOffset = Mathf.Repeat(_treadOffset + c.Distance, _r.TreadSpacing);
                var pl = _r.TreadL.localPosition; pl.z = _treadOffset; _r.TreadL.localPosition = pl;
                var pr = _r.TreadR.localPosition; pr.z = _treadOffset; _r.TreadR.localPosition = pr;
            }
            if (_r.Wheels.Length > 0)
            {
                float deg = c.Distance / Mathf.Max(0.1f, _r.WheelRadius) * Mathf.Rad2Deg;
                foreach (var w in _r.Wheels) w.Rotate(deg, 0f, 0f, Space.Self);
            }
            // Body: engine idle shake, acceleration pitch, hit rock, destruction tilt.
            if (_r.Body != null)
            {
                float shake = c.IsMoving ? Mathf.Sin(c.Time * 31f) * 0.012f : Mathf.Sin(c.Time * 47f) * 0.004f;
                float pitch = -_recoil * 2.5f;
                float roll = 0f;
                float y = shake;
                if (s == UnitAnimState.Hit) roll = Mathf.Sin(c.StateTime * 30f) * 2f;
                if (s == UnitAnimState.Death)
                {
                    float k = Mathf.Clamp01(c.DeathTime / 0.4f);
                    pitch = -6f * k;
                    roll = 7f * k;
                    y = -Mathf.Clamp01((c.DeathTime - 8f) / 4f) * 3f;
                }
                if (s == UnitAnimState.Victory) pitch = Mathf.Sin(c.Time * 3f) * 1f;
                _r.Body.localPosition = new Vector3(0f, y, 0f);
                _r.Body.localRotation = Quaternion.Euler(pitch, 0f, roll);
            }
        }
    }

    public sealed class AircraftAnimator : IUnitAnimator
    {
        private readonly UnitRig _r;
        private float _bank;
        private float _lastYaw;
        private float _turretYaw;

        public AircraftAnimator(UnitRig rig) { _r = rig; _lastYaw = rig.transform.eulerAngles.y; }

        public void OnStateEnter(UnitAnimState s) { }

        public void Animate(UnitAnimState s, ref AnimContext c)
        {
            for (int i = 0; i < _r.Spinners.Length; i++)
            {
                float spd = s == UnitAnimState.Death ? _r.SpinSpeeds[i] * Mathf.Max(0f, 1f - c.DeathTime) : _r.SpinSpeeds[i];
                _r.Spinners[i].Rotate(_r.SpinAxes[i] * spd * c.Dt, Space.Self);
            }
            float yaw = _r.transform.eulerAngles.y;
            float turnRate = Mathf.DeltaAngle(_lastYaw, yaw) / Mathf.Max(0.0001f, c.Dt);
            _lastYaw = yaw;
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-turnRate * 0.35f, -35f, 35f), 1f - Mathf.Exp(-c.Dt * 4f));
            float pitch = c.IsMoving ? Mathf.Min(12f, c.Speed * 0.6f) : 0f; // nose down when flying forward
            if (s == UnitAnimState.Fire) pitch += 3f;
            float y = Mathf.Sin(c.Time * 1.7f) * 0.25f; // hover bob
            if (s == UnitAnimState.Death)
            {
                pitch = 25f + c.DeathTime * 30f;
                _bank += c.Dt * 220f;
            }
            if (_r.Body != null)
            {
                _r.Body.localRotation = Quaternion.Euler(pitch, 0f, _bank);
                _r.Body.localPosition = new Vector3(0f, y, 0f);
            }
            if (_r.Turret != null)
            {
                _turretYaw = Mathf.LerpAngle(_turretYaw, c.TurretYawLocal, 1f - Mathf.Exp(-c.Dt * 8f));
                _r.Turret.localRotation = Quaternion.Euler(0f, _turretYaw, 0f);
            }
        }
    }

    public sealed class StructureAnimator : IUnitAnimator
    {
        private readonly UnitRig _r;
        private float _recoil;
        private float _turretYaw;

        public StructureAnimator(UnitRig rig) { _r = rig; }

        public void OnStateEnter(UnitAnimState s)
        {
            if (s == UnitAnimState.Fire) _recoil = 1f;
        }

        public void Animate(UnitAnimState s, ref AnimContext c)
        {
            if (_r.Body != null)
            {
                float y = 0f, tilt = 0f;
                if (s == UnitAnimState.Construct) y = -(1f - c.BuildProgress) * _r.Height * 0.9f;
                if (s == UnitAnimState.Death)
                {
                    float k = Mathf.Clamp01(c.DeathTime / 1.5f);
                    y = -k * _r.Height * 0.8f;
                    tilt = k * 9f;
                }
                if (s == UnitAnimState.Damaged) tilt = 1.5f;
                _r.Body.localPosition = new Vector3(0f, y, 0f);
                _r.Body.localRotation = Quaternion.Euler(tilt, 0f, tilt * 0.6f);
            }
            if (_r.Turret != null && s != UnitAnimState.Construct)
            {
                _turretYaw = Mathf.LerpAngle(_turretYaw, c.TurretYawLocal, 1f - Mathf.Exp(-c.Dt * 8f));
                _r.Turret.localRotation = Quaternion.Euler(0f, _turretYaw, 0f);
            }
            _recoil = Mathf.MoveTowards(_recoil, 0f, c.Dt * 4f);
            if (_r.Barrel != null)
            {
                var p = _r.Barrel.localPosition;
                p.z = _r.BarrelRestZ - _recoil * 0.3f;
                _r.Barrel.localPosition = p;
            }
            bool active = s != UnitAnimState.Construct && s != UnitAnimState.Death;
            for (int i = 0; i < _r.Spinners.Length; i++)
                if (active) _r.Spinners[i].Rotate(_r.SpinAxes[i] * _r.SpinSpeeds[i] * c.Dt, Space.World);
            if (_r.Extra != null && active)
            {
                if (_r.Extra.name == "PumpBeam") _r.Extra.localRotation = Quaternion.Euler(Mathf.Sin(c.Time * 1.8f) * 14f, 0f, 0f);
                if (_r.Extra.name == "Coil") _r.Extra.localScale = Vector3.one * (1f + Mathf.Sin(c.Time * 12f) * 0.06f + (s == UnitAnimState.Fire ? 0.25f : 0f));
            }
        }
    }

    /// <summary>
    /// Drives a real Animator once production models arrive. Expected Animator parameters:
    /// int "State" (UnitAnimState value), float "Speed", triggers "Fire", "Hit", "Death", "Reload", "Ability", "Victory".
    /// Turret/Barrel transforms (if named) are still rotated procedurally.
    /// </summary>
    public sealed class MecanimAnimatorDriver : IUnitAnimator
    {
        private static readonly int StateId = Animator.StringToHash("State");
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private readonly Animator _anim;
        private readonly UnitRig _rig;
        private readonly bool _hasState, _hasSpeed;

        public MecanimAnimatorDriver(UnitRig rig)
        {
            _rig = rig;
            _anim = rig.Animator;
            foreach (var p in _anim.parameters)
            {
                if (p.nameHash == StateId) _hasState = true;
                if (p.nameHash == SpeedId) _hasSpeed = true;
            }
        }

        public void OnStateEnter(UnitAnimState s)
        {
            if (_hasState) _anim.SetInteger(StateId, (int)s);
            string trigger = null;
            switch (s)
            {
                case UnitAnimState.Fire: trigger = "Fire"; break;
                case UnitAnimState.Hit: trigger = "Hit"; break;
                case UnitAnimState.Death: trigger = "Death"; break;
                case UnitAnimState.Reload: trigger = "Reload"; break;
                case UnitAnimState.Ability: case UnitAnimState.Work: trigger = "Ability"; break;
                case UnitAnimState.Victory: trigger = "Victory"; break;
            }
            if (trigger == null) return;
            foreach (var p in _anim.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == trigger) { _anim.SetTrigger(trigger); break; }
        }

        public void Animate(UnitAnimState s, ref AnimContext c)
        {
            if (_hasSpeed) _anim.SetFloat(SpeedId, c.Speed);
            if (_rig.Turret != null) _rig.Turret.localRotation = Quaternion.Euler(0f, c.TurretYawLocal, 0f);
        }
    }
}
