using System;
using System.Collections.Generic;
using Godot;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Controllers;

namespace Planetsgodot.Scripts.AI;

/// <summary>
/// AI Pilot that outputs player-style controls (MovementX, MovementY, Boosting).
/// This ensures AI ships fly exactly like the player with no "cheating".
/// </summary>
public class AIPilot
{
    // === OUTPUT: Same controls as player ===
    // MovementX: -1 = turn left, +1 = turn right
    // MovementY: -1 = backward thrust, +1 = forward thrust
    public float MovementX { get; private set; }
    public float MovementY { get; private set; }
    public bool Boosting { get; private set; }
    public bool ForceBoost { get; set; }
    public bool CollisionAvoidanceEnabled { get; set; } = false;

    // === TARGET STATE ===
    public Vector3 TargetPosition { get; set; }
    public float TargetOrientation { get; set; } // Desired Y rotation in radians
    public float DesiredSpeed { get; set; } // Target speed (0 to ship's maxSpeed)
    public bool HasArrived { get; private set; }
    public bool OrientationMatched { get; private set; }
    
    // === EXCLUSIONS ===
    public List<Node> IgnoredColliders { get; set; } = new List<Node>();

    // === TUNING PARAMETERS ===
    public float ArrivalRadius = 5.0f; // Stop within this distance
    public float SlowdownRadius = 50.0f; // Start slowing at this distance (increased from 25)
    public float TurnThresholdDeg = 60.0f; // Degrees - proportional zone (increased for smoother turns)
    public float OrientationToleranceDeg = 2.0f; // Degrees - deadzone (reduced for precision)
    public float SpeedTolerance = 1.0f; // Units/sec tolerance for speed matching
    public float AngularDamping = 3.0f; // How much to damp based on turn rate (increased from 1.0 to reduce oscillation)

    // === DEBUG INFO ===
    public float DebugAngleToTarget { get; private set; }
    public float DebugDistanceToTarget { get; private set; }
    public float DebugStoppingDistance { get; private set; }
    public float DebugCurrentSpeed { get; private set; }
    public float DebugAngularVelocity { get; private set; }

    /// <summary>
    /// Main update method - calculates MovementX, MovementY, and Boosting based on current state and targets.
    /// Collision avoidance is applied as a priority layer on top of normal navigation.
    /// Call this every physics frame.
    /// </summary>
    /// <summary>
    /// Feed-forward velocity target (e.g. Leader velocity)
    /// </summary>
    public Vector3 MatchVelocity { get; set; } = Vector3.Zero;

    /// <summary>
    /// Main update method - 3-Layer Architecture (Steering -> Dynamics -> Actuation)
    /// 1. Steering: Calculate Desired Velocity (Arrive + Match)
    /// 2. Dynamics: Calculate Required Force to change Current Velocity -> Desired Velocity
    /// 3. Actuation: Map Force to Ship Controls (Thrust + Turn)
    /// </summary>
    public void UpdateControls(ShipController ship, RigidBody3D rb, float delta)
    {
        if (ship?.spec == null || rb == null)
        {
            MovementX = 0; MovementY = 0; Boosting = false; return;
        }

        // === 0. SENSING ===
        Vector3 currentPos = rb.GlobalPosition;
        Vector3 currentVel = rb.LinearVelocity;
        Vector3 toTarget = TargetPosition - currentPos;
        toTarget.Y = 0; // Top-down
        float dist = toTarget.Length();
        
        DebugDistanceToTarget = dist;
        DebugCurrentSpeed = currentVel.Length();
        DebugAngularVelocity = rb.AngularVelocity.Y;

        if (HasArrived && dist > ArrivalRadius * 2.0f) HasArrived = false;

        // === 1. STEERING: Desired Velocity ===
        float targetSpeed = ship.spec.maxSpeed;
        if (dist < SlowdownRadius)
        {
            targetSpeed = ship.spec.maxSpeed * (dist / SlowdownRadius);
        }
        
        if (DesiredSpeed > 0) targetSpeed = Mathf.Min(targetSpeed, DesiredSpeed);
        
        Vector3 desiredVel = toTarget.Normalized() * targetSpeed;
        
        // Feed-Forward: Match leader velocity
        if (MatchVelocity.LengthSquared() > 0.1f)
        {
            desiredVel += MatchVelocity;
            if (desiredVel.Length() > ship.spec.maxSpeed)
            {
                desiredVel = desiredVel.Normalized() * ship.spec.maxSpeed;
            }
        }
        
        // Collision Avoidance Override
        if (CollisionAvoidanceEnabled)
        {
            if (CalculateCollisionAvoidance(rb, -rb.GlobalTransform.Basis.Z, out Vector3 avoidDir, out float avoidUrgency))
            {
                if (avoidUrgency > 0.3f)
                {
                    desiredVel = avoidDir * ship.spec.maxSpeed;
                }
            }
        }
        
        // Arrival Stop
        if (dist < ArrivalRadius && currentVel.Length() < 2.0f && MatchVelocity.LengthSquared() < 1.0f)
        {
            HasArrived = true;
            desiredVel = Vector3.Zero;
        }

        // === 2. DYNAMICS: Required Force ===
        Vector3 velocityError = desiredVel - currentVel;
        
        float responseTime = 0.5f; 
        Vector3 requiredForceVector = (velocityError / responseTime) * rb.Mass;
        
        float maxAvailableForce = ship.spec.acceleration * 10.0f * rb.Mass;
        
        Boosting = requiredForceVector.Length() > maxAvailableForce * 1.5f || ForceBoost;
        if (Boosting) maxAvailableForce *= 2.0f; 
        
        if (requiredForceVector.Length() > maxAvailableForce)
        {
            requiredForceVector = requiredForceVector.Normalized() * maxAvailableForce;
        }

        // === 3. ACTUATION: Mapping Force to Controls ===
        Vector3 localForce = rb.GlobalTransform.Basis.Inverse() * requiredForceVector;
        
        // Forward/Back Thrust
        float forwardThrustDemand = -localForce.Z / (ship.spec.acceleration * 10.0f * rb.Mass);
        MovementY = Mathf.Clamp(forwardThrustDemand, -1f, 1f);

        // FIX: Prevent "Reversing" (flying backwards). 
        // Only allow negative thrust (braking) if we are actually moving forward.
        Vector3 localVel = rb.GlobalTransform.Basis.Inverse() * currentVel;
        if (MovementY < -0.01f && localVel.Z > -1.0f) // If trying to reverse... AND not moving forward (> 1m/s)
        {
            MovementY = 0f; // Coast instead of reverse
        }
        
        // Turning
        if (requiredForceVector.Length() > 100.0f)
        {
             // Face the force vector
            // FIX: Only use reverse steering if force is DOMINANTLY backward (braking).
            // This prevents sign flipping when force is largely sideways (drift correction).
            // Condition: Z component > 50% of total length (approx 60 cone to rear)
            bool preferReverse = localForce.Z > (localForce.Length() * 0.5f);
            
            Vector3 steeringVector = preferReverse ? -localForce : localForce;
            
            float angleToForce = Mathf.Atan2(steeringVector.X, -steeringVector.Z);
            
            // PID Controller
            float turnDemand = angleToForce * 1.5f; // Gain
            float turnDamping = rb.AngularVelocity.Y * AngularDamping; 
            
            // FIX: Damping was inverted! 
            // MoveX has inverse relationship to Torque (Pos MoveX -> Neg Torque).
            // To damp Left Spin (Pos Vel), we need Right Torque (Neg Torque), so Pos MoveX.
            // Therefor MoveX must be PROPORTIONAL (Adding) to Velocity, not subtracting.
            MovementX = Mathf.Clamp(turnDemand + turnDamping, -1f, 1f);

            // DEBUG STEERING
            if (rb.Name == "ImperialEagle") // Only debug the wingman
                GD.Print($"Steer: F_Z={localForce.Z:F1} SteerZ={steeringVector.Z:F1} Angle={angleToForce:F2} Demand={turnDemand:F2} Damp={turnDamping:F2} MX={MovementX:F2}");
        }
        else if (HasArrived && Mathf.Abs(TargetOrientation) > 0.001f)
        {
            float orientationDiff = NormalizeAngle(TargetOrientation - rb.Rotation.Y);
             // Orientation Logic... reused roughly
             float turnDemand = orientationDiff * 2.0f;
             float turnDamping = rb.AngularVelocity.Y * AngularDamping;
             MovementX = Mathf.Clamp(turnDemand + turnDamping, -1f, 1f);
        }
        else
        {
             // Idle Damping - Fix Sign!
             // Pos Vel (Left) -> Need Pos MoveX (Right Torque) to Stop.
             MovementX = Mathf.Clamp((rb.AngularVelocity.Y * 5.0f), -1f, 1f);
        }
        
        // GD.Print($"AIPilot: MoveX={MovementX:F2} MoveY={MovementY:F2} Dist={dist:F1} V_Err={velocityError.Length():F1} F_Req={requiredForceVector.Length():F0} Arrived={HasArrived}");
    }

    // === COLLISION AVOIDANCE ===
    private const float AvoidanceRadius = 8.0f; // Start avoiding at this distance (reduced from 12)
    private const float DangerRadius = 2.0f; // Emergency avoidance distance (reduced from 5 to fix formation)
    private const float AvoidanceLookahead = 2.0f; // Seconds to predict ahead

    /// <summary>
    /// Check for nearby ships and calculate avoidance direction
    /// </summary>
    private bool CalculateCollisionAvoidance(
        RigidBody3D rb,
        Vector3 shipForward,
        out Vector3 avoidanceDir,
        out float urgency
    )
    {
        avoidanceDir = Vector3.Zero;
        urgency = 0;

        // Get all NPCs and player
        var tree = rb.GetTree();
        if (tree == null)
            return false;

        var npcs = tree.GetNodesInGroup("NPC");
        var players = tree.GetNodesInGroup("Player");


        Vector3 myPos = rb.GlobalPosition;
        Vector3 myVel = rb.LinearVelocity;

        Vector3 totalAvoidance = Vector3.Zero;
        float maxUrgency = 0;

        // Check against other NPCs
        foreach (var node in npcs)
        {
            if (node == rb || node == rb.GetParent())
                continue; // Skip self
            if (IgnoredColliders.Contains(node))
                continue; // Skip ignored


            if (node is RigidBody3D otherRb)
            {
                CheckShipCollision(myPos, myVel, otherRb, ref totalAvoidance, ref maxUrgency);
            }
        }

        // Check against player
        foreach (var node in players)
        {
            if (IgnoredColliders.Contains(node)) continue;

            if (node is RigidBody3D playerRb)
            {
                CheckShipCollision(myPos, myVel, playerRb, ref totalAvoidance, ref maxUrgency);
            }
        }



        if (totalAvoidance.LengthSquared() > 0.01f)
        {
            avoidanceDir = totalAvoidance.Normalized();
            urgency = maxUrgency;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Check collision with a single ship and accumulate avoidance
    /// </summary>
    private void CheckShipCollision(
        Vector3 myPos,
        Vector3 myVel,
        RigidBody3D other,
        ref Vector3 totalAvoidance,
        ref float maxUrgency
    )
    {
        Vector3 otherPos = other.GlobalPosition;
        Vector3 otherVel = other.LinearVelocity;

        Vector3 toOther = otherPos - myPos;
        toOther.Y = 0;
        float distance = toOther.Length();

        if (distance < 0.1f || distance > AvoidanceRadius * 2)
            return;

        // Predict future positions
        Vector3 relativeVel = myVel - otherVel;
        float closingSpeed = -relativeVel.Dot(toOther.Normalized());

        // Only worry if we're getting closer
        if (closingSpeed < 0.5f && distance > DangerRadius)
            return;

        // Time to closest approach
        float timeToClosest = closingSpeed > 0.1f ? distance / closingSpeed : AvoidanceLookahead;
        timeToClosest = Mathf.Clamp(timeToClosest, 0, AvoidanceLookahead);

        // Predicted positions
        Vector3 myFuturePos = myPos + myVel * timeToClosest;
        Vector3 otherFuturePos = otherPos + otherVel * timeToClosest;
        Vector3 futureToOther = otherFuturePos - myFuturePos;
        futureToOther.Y = 0;
        float futureDistance = futureToOther.Length();

        // Calculate urgency based on distance
        float currentUrgency = 0;
        if (distance < DangerRadius)
        {
            currentUrgency = 1.0f; // Maximum urgency
        }
        else if (distance < AvoidanceRadius)
        {
            currentUrgency = 1.0f - (distance - DangerRadius) / (AvoidanceRadius - DangerRadius);
        }
        else if (futureDistance < DangerRadius)
        {
            currentUrgency = 0.5f * (1.0f - futureDistance / DangerRadius);
        }

        if (currentUrgency > 0.05f)
        {
            // Avoidance direction: perpendicular to the direction to other ship
            // Choose the side that's more aligned with our current velocity
            Vector3 awayFromOther = -toOther.Normalized();
            Vector3 perpLeft = new Vector3(awayFromOther.Z, 0, -awayFromOther.X);
            Vector3 perpRight = new Vector3(-awayFromOther.Z, 0, awayFromOther.X);

            // Choose the perpendicular direction that's more ahead of us
            Vector3 forward = myVel.LengthSquared() > 0.1f ? myVel.Normalized() : Vector3.Forward;
            Vector3 bestPerp =
                perpLeft.Dot(forward) > perpRight.Dot(forward) ? perpLeft : perpRight;

            // Blend away + perpendicular for natural avoidance curves
            Vector3 avoidDir = (awayFromOther * 0.3f + bestPerp * 0.7f).Normalized();

            totalAvoidance += avoidDir * currentUrgency;
            maxUrgency = Mathf.Max(maxUrgency, currentUrgency);
        }
    }



    /// <summary>
    /// Set target to a position (orientation will be toward the position)
    /// </summary>
    public void SetTargetPosition(Vector3 position)
    {
        TargetPosition = position;
        TargetOrientation = 0; // Will be calculated on arrival
    }

    /// <summary>
    /// Set target with specific arrival orientation
    /// </summary>
    public void SetTargetPositionAndOrientation(Vector3 position, float orientationRadians)
    {
        TargetPosition = position;
        TargetOrientation = orientationRadians;
    }

    /// <summary>
    /// Set target to follow another entity
    /// </summary>
    public void SetTargetEntity(ITarget target, float followDistance = 10.0f)
    {
        if (target == null)
            return;

        Vector3 targetPos = target.Position;
        Vector3 targetVel = target.LinearVelocity;

        // Position behind the target
        if (targetVel.LengthSquared() > 0.1f)
        {
            TargetPosition = targetPos - targetVel.Normalized() * followDistance;
            TargetOrientation = Mathf.Atan2(targetVel.X, -targetVel.Z);
        }
        else
        {
            TargetPosition = targetPos;
        }

        DesiredSpeed = targetVel.Length();
    }

    /// <summary>
    /// Calculate attack position relative to a target
    /// </summary>
    public void SetAttackPosition(ITarget target, TacticalPosition tactic, float distance = 15.0f)
    {
        if (target == null)
            return;

        Vector3 targetPos = target.Position;
        RigidBody3D targetRb = target.GetRigidBody3D();
        float targetYaw = targetRb?.Rotation.Y ?? 0;

        Vector3 offset;
        float orientation;

        switch (tactic)
        {
            case TacticalPosition.FlankLeft:
                // Position to target's left, facing toward target
                offset = new Vector3(-distance, 0, 0);
                orientation = targetYaw + Mathf.Pi / 2; // Face right toward target
                break;
            case TacticalPosition.FlankRight:
                // Position to target's right, facing toward target
                offset = new Vector3(distance, 0, 0);
                orientation = targetYaw - Mathf.Pi / 2; // Face left toward target
                break;
            case TacticalPosition.Behind:
                // Position behind target
                offset = new Vector3(0, 0, distance);
                orientation = targetYaw; // Face same direction
                break;
            case TacticalPosition.Direct:
            default:
                // Head-on approach
                offset = new Vector3(0, 0, -distance);
                orientation = targetYaw + Mathf.Pi; // Face toward target
                break;
        }

        // Rotate offset by target's yaw
        float cos = Mathf.Cos(targetYaw);
        float sin = Mathf.Sin(targetYaw);
        Vector3 rotatedOffset = new Vector3(
            offset.X * cos - offset.Z * sin,
            0,
            offset.X * sin + offset.Z * cos
        );

        TargetPosition = targetPos + rotatedOffset;
        TargetOrientation = NormalizeAngle(orientation);
        DesiredSpeed = 0; // Will stop at attack position
    }

    /// <summary>
    /// Normalize angle to -PI to PI range
    /// </summary>
    private float NormalizeAngle(float angle)
    {
        while (angle > Mathf.Pi)
            angle -= Mathf.Pi * 2;
        while (angle < -Mathf.Pi)
            angle += Mathf.Pi * 2;
        return angle;
    }

    /// <summary>
    /// Get debug info string
    /// </summary>
    public string GetDebugInfo()
    {
        return $"MX:{MovementX:F2} MY:{MovementY:F2} Boost:{Boosting} "
            + $"Dist:{DebugDistanceToTarget:F1} Angle:{DebugAngleToTarget:F0}° "
            + $"Stop:{DebugStoppingDistance:F1} Arrived:{HasArrived} Orient:{OrientationMatched}";
    }
}

/// <summary>
/// Tactical positions for attack approaches
/// </summary>
public enum TacticalPosition
{
    Direct, // Head-on approach
    FlankLeft, // Attack from target's left
    FlankRight, // Attack from target's right
    Behind, // Attack from behind
    Evade, // Defensive orbit
}
