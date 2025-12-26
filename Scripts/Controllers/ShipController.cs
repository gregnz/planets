using System;
using System.Diagnostics;
using Godot;
using static utils.Util;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Environment;
using Planetsgodot.Scripts.Missions;
using Planetsgodot.Scripts.AI;

namespace Planetsgodot.Scripts.Controllers;

public class ShipController
{
    public int heat;
    private float t = 0;

    public ShipFactory.ShipSpec spec;
    public ShieldSpec Shield { get; private set; }
    public ArmorSpec Armor { get; private set; }
    private FireSystem fireSystem;
    public FireSystem FireSys => fireSystem;
    public double LastHitTime { get; private set; }

    public float HealthPercent
    {
        get
        {
            if (Armor == null || Armor.maxStrength == null) return 1.0f;
            float current = 0;
            float max = 0;
            for (int i = 0; i < Armor.strength.Length; i++)
            {
                current += Armor.strength[i];
                max += Armor.maxStrength[i];
            }

            return max > 0 ? current / max : 0f;
        }
    }

    private IDamageable ship;

    public float MovementX;
    public float MovementY;
    public float dragCoefficient = 1.0f;
    public float angularDragCoefficient = 0.5f;
    public float boostCoefficient = 2f;
    float sidewaysdragmultiplier = 50f;
    float turnSmoothV;
    float rollSmoothV;

    public bool boosting;
    float boostTimeRemaining;
    float boostTimeToAdd;
    float baseTargetSpeed;
    public float currentSpeed;
    private float currentBankAngle = 0.0f;

    public bool InWarp = false;
    public const float WarpSpeed = 5000.0f;


    [ExportGroup("Banking Control (Optional)")] [Export]
    public bool EnableBanking = true;

    [Export(PropertyHint.Range, "0,5,0.1")]
    public float BankFactorLateralVel = 1.5f;

    // Alternative: How strongly yaw rate influences bank.
    // [Export(PropertyHint.Range, "0,5,0.1")] public float BankFactorYawRate = 0.5f;
    [Export(PropertyHint.Range, "0,90,1")] public float MaxBankAngleDegrees = 45.0f;

    // How quickly the visual mesh banks towards the target angle (higher is faster)
    [Export(PropertyHint.Range, "1,20,0.5")]
    public float BankSmoothingFactor = 8.0f;

    [Export] public float MaxBankTorque = 50.0f;

    private bool slowing = false;

    private enum State
    {
        Idle,
        Accelerating,
        Moving,
        Decelerating
    }

    State _currentState = State.Idle;
    public RigidBody3D Rb { get; set; }


    public void initialiseFromSpec(ShipFactory.ShipSpec shipSpecification, IDamageable playerController,
        FireSystem fireSystem)
    {
        ship = playerController;
        if (ship is RigidBody3D rb)
        {
            Rb = rb;
            // Enforce 2D rotation constraints to prevent tumbling
            Rb.AxisLockAngularX = true;
            Rb.AxisLockAngularZ = true;
        }

        spec = shipSpecification;
        Shield = new ShieldSpec(spec.shield); // copy from ship spec
        Armor = new ArmorSpec(spec.armor); // copy from ship spec
        this.fireSystem = fireSystem;
        AnalyzeWeaponCapabilities();
    }

    public struct WeaponCapabilities
    {
        public bool HasFixedWeapons;
        public bool HasTurrets;
        public bool HasMissiles;
        public bool HasPointDefence;
        public float MaxRange;
        public float MinRange; // E.g. don't get closer than this if possible
    }

    public WeaponCapabilities Capabilities { get; private set; }

    public void AnalyzeWeaponCapabilities()
    {
        Capabilities = new WeaponCapabilities();
        float maxRange = 0f;
        float minRange = float.MaxValue;
        bool hasFixed = false;
        bool hasTurrets = false;
        bool hasMissiles = false;
        bool hasPD = false;

        foreach (var hpSpec in spec.shipsWeapons)
        {
            if (hpSpec.MaxRange > maxRange) maxRange = hpSpec.MaxRange;
            if (hpSpec.MinRange < minRange) minRange = hpSpec.MinRange;

            if (hpSpec is HardpointSpec.PointDefence)
            {
                hasPD = true;
                continue;
            }

            if (hpSpec is HardpointSpec.Missile)
            {
                hasMissiles = true;
            }
            else if (hpSpec.isTurret)
            {
                hasTurrets = true;
            }
            else
            {
                hasFixed = true;
            }
        }

        // Finalize struct (cannot modify struct properties directly after creation if not a variable)
        var caps = new WeaponCapabilities();
        caps.HasFixedWeapons = hasFixed;
        caps.HasTurrets = hasTurrets;
        caps.HasMissiles = hasMissiles;
        caps.HasPointDefence = hasPD;
        caps.MaxRange = maxRange;
        caps.MinRange = (minRange == float.MaxValue) ? 0 : minRange;

        Capabilities = caps;

        // GD.Print($"{spec.name} Capabilities: Fixed={hasFixed} Turrets={hasTurrets} Missiles={hasMissiles} Range={maxRange}");
    }

    public Godot.Collections.Dictionary ToUIDict()
    {
        var myDict = new Godot.Collections.Dictionary();
        myDict["name"] = spec.name;
        myDict["acceleration"] = spec.acceleration;
        myDict["max_speed"] = spec.maxSpeed;
        myDict["rotate_speed"] = spec.rotateSpeed;
        myDict["boost_coeff"] = spec.boostCoeff;
        myDict["shield"] = Shield.GetStrengthPercents();
        myDict["armor"] = Armor.GetStrengthPercents();
        myDict["weapons"] = spec.weaponsAsStringArray;
        return myDict;
    }

    /// <summary>
    /// Determines which quadrant (0=Front, 1=Back, 2=Left, 3=Right) a local position belongs to.
    /// Matches Shield.cs logic.
    /// </summary>
    private int GetQuadrantFromLocalPos(Vector3 localPos)
    {
        float absX = Mathf.Abs(localPos.X);
        float absZ = Mathf.Abs(localPos.Z);

        if (absZ >= absX)
            return localPos.Z < 0 ? 0 : 1; // -Z = FRONT, +Z = BACK
        else
            return localPos.X < 0 ? 2 : 3; // -X = LEFT, +X = RIGHT
    }

    public bool Damage(float damage, Vector3 normal, Vector3 hitPosition = default)
    {
        DamageDirection damageDir;

        if (hitPosition != default && Rb != null)
        {
            // Use precise hit position if available
            Vector3 localHit = Rb.ToLocal(hitPosition);
            int quadrant = GetQuadrantFromLocalPos(localHit);
            damageDir = (DamageDirection)quadrant;

            // GD.Print(
            //     $"ShipController Damage: HitPos={hitPosition} Local={localHit} Quadrant={quadrant} ({damageDir}) ShieldStr={Shield.strength[quadrant]}");

            // Trigger visual if shield is up in this quadrant
            if (Shield.strength[quadrant] > 0)
            {
                _shieldVisuals?.OnHit(hitPosition);
            }
        }
        else
        {
            // Fallback to normal/angle based approximation
            float angle = normal.AngleTo(Vector3.Forward);
            damageDir = DamageDirection.BACK;

            if (angle <= 45) damageDir = DamageDirection.FRONT;
            if (angle > 135 && angle < 225) damageDir = DamageDirection.BACK;

            if (Mathf.IsEqualApprox(angle, 0)) damageDir = DamageDirection.FRONT;
            if (Mathf.IsEqualApprox(angle, 180)) damageDir = DamageDirection.BACK;
            if (Mathf.IsEqualApprox(angle, 90))
            {
                Vector3 cross = Vector3.Forward.Cross(normal);
                if (cross.Y > 0) damageDir = DamageDirection.RIGHT;
                else damageDir = DamageDirection.LEFT;
            }
            // Cannot trigger precise visual hit without position
        }

        if (ApplyDamage(damage, damageDir)) return true;

        return false;
    }

    public bool Damage(HardpointSpec hardpoint, Vector3 hit, double deltaTime)
    {
        // hit is Global Position. Use it to determine quadrant.
        // If Rb is null (unlikely but possible during init?), we can't do local transform.
        if (Rb == null) return false;

        Vector3 localHit = Rb.ToLocal(hit);
        int quadrant = GetQuadrantFromLocalPos(localHit);
        DamageDirection damageDir = (DamageDirection)quadrant;

        // GD.Print(
        //     $"ShipController Damage(Hardpoint): HitPos={hit} Local={localHit} Quadrant={quadrant} ({damageDir}) ShieldStr={Shield.strength[quadrant]}");

        // Trigger visual if shield is up in this quadrant
        if (Shield.strength[quadrant] > 0)
        {
            _shieldVisuals?.OnHit(hit);
        }

        if (ApplyDamage((float)(hardpoint.Damage * deltaTime), damageDir)) return true;

        return false;
    }


    private Shield _shieldVisuals;

    public void SetShieldVisuals(Shield shieldVisuals)
    {
        _shieldVisuals = shieldVisuals;
    }

    private bool ApplyDamage(float damage, DamageDirection damageDir)
    {
        LastHitTime = Time.GetTicksMsec() / 1000.0;
        int damageSide = (int)Convert.ChangeType(damageDir, damageDir.GetTypeCode());

        float residualDamage;

        // Check if this shield quadrant is already depleted - bypass to armor
        if (Shield.strength[damageSide] <= 0)
        {
            residualDamage = -damage; // Full damage goes to armor
        }
        else
        {
            residualDamage = Shield.Deplete(damageSide, damage);
        }

        // Update visuals
        if (_shieldVisuals != null)
        {
            var strengths = Shield.GetStrengthPercents();
            _shieldVisuals.UpdateShieldStrengths(strengths[0], strengths[1], strengths[2], strengths[3]);
        }

        if (residualDamage < 0)
            residualDamage = Armor.Deplete(damageSide, -residualDamage);

        if (residualDamage < 0)
        {
            Destroy();
            return true;
        }

        return false;
    }


    private void Destroy()
    {
        ship.Destroy();
    }

    public float _rotationY = 0f;
    private double time = 0;
    public AIController.AIState currentState;


    internal void HandleMovement(ShipFactory.ShipSpec spec, RigidBody3D rb, double delta, bool dbg, bool isNpc)
    {
        time += delta;
        float maxSpeed = InWarp ? WarpSpeed : spec.maxSpeed;
        float radsPerSecond = spec.rotateSpeed;
        float radius = rb.LinearVelocity.Length() / radsPerSecond;
        // if (MovementX > 0.01f) MovementX = 1;
        // else if (MovementX < -0.01f) MovementX = -1;
        // else MovementX = 0;

        // Forward Thrust Only (No Reversing)
        float throttle = Math.Max(0, MovementY);

        // Disable thrust when overheated
        if (fireSystem?.IsOverheated == true)
        {
            throttle = 0;
        }

        var acc = throttle * (spec.acceleration * (boosting ? boostCoefficient : 1f));

        // Massive boost for Warp
        if (InWarp) acc *= 5.0f;

        Transform3D rbTransform = rb.Transform;
        Vector3 fce = -rbTransform.Basis.Z.Normalized() * rb.Mass * acc * 10;
        rb.ApplyForce(fce);

        // Drag / Airbrakes logic
        // If not thrusting forward (coasting or braking), apply drag
        if (MovementY < 0.1f && !InWarp)
        {
            float useDragCoeff = dragCoefficient;
            // If input is negative, deploy "Airbrakes" (High Drag)
            if (MovementY < -0.1f)
            {
                useDragCoeff *= 10.0f;
            }

            // GD: Insight: Masses in motion have kinetic energy (proportional to mass). Drag is a force that depletes that KE which is
            // not proportional to mass.
            // {F_{D}\,=\,{\tfrac {1}{2}}\,\rho \,v^{2}\,C_{D}\,A}
            CalcDrag(-rb.LinearVelocity, spec.CrossSectionalArea, rb.LinearVelocity.Length(), useDragCoeff,
                out var dragForce);
            // Debug.Print(
            // $"Drag force player: {Util.VP(rb.LinearVelocity)}{Util.VP(dragForce)} {spec.CrossSectionalArea} {rb.LinearVelocity.Length()} {dragForce.Length()}");
            rb.ApplyForce(dragForce);
        }


        if (!InWarp)
        {
            var rightVel = rb.GlobalTransform.Basis.X * rb.LinearVelocity.Dot(rb.GlobalTransform.Basis.X);
            DebugDraw3D.DrawLine(rb.Position, rb.Position + rightVel, Colors.Green);
            // This is interesting. Drag (above) will "drag" on the sideways component of linear velocity.
            // This will add an additional sideways drag component, operating at 'right'.
            CalcDrag(-rightVel, spec.CrossSectionalArea, rb.LinearVelocity.Length(), dragCoefficient,
                out var sideDragForce);
            rb.ApplyForce(sideDragForce);
        }


        // Calculate torque to reach target speed against drag
        // Steady state: Torque = Drag
        // Torque = rotationChange * Mass
        // Drag = w * Mass * dragCoeff
        // w_max = rotationChange / dragCoeff
        // We want w_max to be a bit higher than spec.rotateSpeed for responsiveness (e.g. 5x)
        float overdrive = 1.0f;
        float rotationChange = spec.rotateSpeed * -MovementX * angularDragCoefficient * overdrive;
        rb.ApplyTorque(rb.Transform.Basis.Y * rotationChange * rb.Mass);

        float brakingMultiplier = 1.0f;
        if (Mathf.Abs(MovementX) < 0.05f)
        {
            brakingMultiplier = 10.0f; // Active braking when not steering
        }

        // Massive rotational dampening during warp to prevent "spinning top" effect
        if (InWarp) brakingMultiplier = 20.0f;

        Vector3 angularDragForce = -rb.AngularVelocity * rb.Mass * angularDragCoefficient * brakingMultiplier;
        rb.ApplyTorque(angularDragForce);

        _rotationY += rotationChange;
        rb.LinearVelocity = rb.LinearVelocity.LimitLength(maxSpeed);
        ApplyCosmeticBank(rb, rb.Basis, rb.LinearVelocity, rb.AngularVelocity, (float)delta);
    }

    private void ApplyCosmeticBank(RigidBody3D rb, Basis currentBasis, Vector3 currentVel, Vector3 currentAngularVel,
        float delta)
    {
        Node3D VisualsNode = rb.GetChild(0) as Node3D;
        float targetBankAngleRad = 0f;
        float maxBankRad = Mathf.DegToRad(MaxBankAngleDegrees);

        // --- Calculate Target Bank Angle ---
        // Option 1: Based on Lateral Velocity
        Vector3 localVelocity = currentBasis.Inverse() * currentVel;
        float lateralVelX = localVelocity.X; // Sideways slide relative to ship's right
        targetBankAngleRad =
            -lateralVelX * BankFactorLateralVel * 0.1f; // Negative = bank left when sliding right. Tune multiplier.

        // Option 2: Based on Yaw Rate (Uncomment to use instead or combine)
        float yawRate = currentAngularVel.Y;
        targetBankAngleRad = yawRate * BankFactorLateralVel;

        // Clamp the target bank angle
        targetBankAngleRad = Mathf.Clamp(targetBankAngleRad, -maxBankRad, maxBankRad);


        // --- Smoothly Lerp Visual Rotation ---
        // Get current visual rotation (Euler Z component is bank if -Z is forward)
        // IMPORTANT: Use VisualsNode.Rotation, NOT ControlledBody.Rotation
        Vector3 currentVisualEuler = VisualsNode.Rotation;
        float currentBankRad = currentVisualEuler.Z; // Assuming default YXZ Euler order

        // Lerp towards the target angle
        float newBankRad = Mathf.LerpAngle(currentBankRad, targetBankAngleRad, BankSmoothingFactor * delta);

        // Apply the new rotation ONLY to the VisualsNode's Z rotation
        VisualsNode.Rotation = new Vector3(currentVisualEuler.X, currentVisualEuler.Y, newBankRad);
    }

    public bool IsMissile()
    {
        return spec.name == "Missile";
    }
}