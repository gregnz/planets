// AIController.cs - Refactored to use player-style controls via AIPilot

using System;
using System.Collections.Generic;
using Godot;
using Planetsgodot.Scripts;
using Planetsgodot.Scripts.AI;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Controllers;

/// <summary>
/// AI Controller that uses player-style controls (MovementX, MovementY, Boosting).
/// No "cheating" - ships fly exactly like the player controls them.
/// </summary>
public partial class AIController : Node
{
    private NavigationSystem _navSystem;
    public enum AIState
    {
        Idle, // Do nothing, coast to stop
        Seek, // Fly toward target at full speed
        Arrive, // Fly to target and stop
        ArriveOrient, // Fly to target, stop, and face direction
        Follow, // Follow another ship
        Formation, // Fly in formation with squad
        AttackRun, // Tactical attack approach
        CombatFly, // Boids/Arcade flight (Separation + Swooping)
        Evasion, // Jinking behavior
        Retreat, // Run away from target
    }

    public enum TacticalPhase
    {
        Approach,
        Attack,
        Disengage,
    }

    // === EXPORTED PROPERTIES ===
    [Export]
    public AIState CurrentState { get; set; } = AIState.Arrive;

    [ExportGroup("Tuning")]
    [Export]
    public float ArrivalRadius { get; set; } = 5.0f;

    [Export]
    public float SlowdownRadius { get; set; } = 25.0f;

    [Export]
    public float OrientationTolerance { get; set; } = 0.05f;

    private bool _enableCollisionAvoidance = true;

    [Export]
    public bool EnableCollisionAvoidance
    {
        get => _enableCollisionAvoidance;
        set
        {
            _enableCollisionAvoidance = value;
            if (_pilot != null)
                _pilot.CollisionAvoidanceEnabled = value;
        }
    }

    [ExportGroup("Debug")]
    [Export]
    private bool _debugDraw = true;

    // === TARGET PROPERTIES ===
    public Node3D TargetNode { get; set; } = null;
    public Vector3 TargetPosition { get; set; } = Vector3.Zero;
    public float TargetOrientation { get; set; } = 0.0f;
    public TacticalPosition AttackTactic { get; set; } = TacticalPosition.Direct;
    public float AttackDistance { get; set; } = 15.0f;

    // === FORMATION ===
    public FormationManager Formation { get; private set; }
    public int FormationIndex { get; set; } = 0;
    public AIController FormationLeader { get; set; } = null;
    public Node3D FormationLeaderBody { get; set; } = null;
    public CombatDirector.OrderType CurrentOrder { get; set; } = CombatDirector.OrderType.FreeFire;

    // === REFERENCES ===
    public ShipController _ship;

    // === INTERNAL ===
    private AIPilot _pilot;
    private AIDecisionEngine _brain;
    public List<DecisionLog> DecisionHistory => _brain?.History;

    // Internal state
    private TacticalPhase _tacticalPhase = TacticalPhase.Approach;
    private float _stateTimer = 0f;
    private bool _inBreakAway = false;
    private Vector3 _breakAwayDirection;

    private Vector3 _evasionDirection; // NEW: Persist the evasion vector

    public override void _Ready()
    {
        _pilot = new AIPilot
        {
            ArrivalRadius = ArrivalRadius,
            SlowdownRadius = SlowdownRadius,
            OrientationToleranceDeg = OrientationTolerance * Mathf.RadToDeg(1f), // Convert if needed
            CollisionAvoidanceEnabled = EnableCollisionAvoidance,
        };

        Formation = new FormationManager();
        _brain = new AIDecisionEngine(this); // Initialize Brain

        SetPhysicsProcess(true);
        GD.Print($"AIController Ready: State={CurrentState}");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_ship?.Rb == null || _ship.spec == null)
        {
            GD.PrintErr("AIController: Ship or spec is null!");
            return;
        }

        // Safety check: Am I in the tree?
        if (!IsInsideTree())
            return;

        // Update target position if tracking a node
        // Must check IsInstanceValid and IsInsideTree to avoid "NativeCalls" errors during scene changes
        if (TargetNode != null && IsInstanceValid(TargetNode) && TargetNode.IsInsideTree())
        {
            TargetPosition = TargetNode.GlobalPosition;
        }
        else if (TargetNode != null)
        {
            // Target became invalid or detached.
            TargetNode = null;
        }

        // Run Brain (Decision Making)
        _brain.Update(delta);

        // Run arcade logic overrides
        UpdateDogfightLogic(delta);

        // Run weapon logic
        UpdateWeaponsLogic();

        // Calculate pilot targets based on current state
        UpdatePilotTargets();

        // Run the pilot logic to get control outputs
        _pilot.UpdateControls(_ship, _ship.Rb, (float)delta);

        // Apply pilot outputs to ship (same as player does)
        // DEBUG: Force stationary for debugging
        _ship.MovementX = _pilot.MovementX;
        _ship.MovementY = _pilot.MovementY;
        // _ship.MovementZ = _pilot.MovementZ; // Not used/implemented yet
        _ship.boosting = _pilot.Boosting;

        // Debug output (commented to reduce console spam)
        // GD.Print($"AIController [{CurrentState}]: Target={TargetPosition} " +
        //  $"MX={_ship.MovementX:F2} MY={_ship.MovementY:F2}");

        // Debug visualization
        if (_debugDraw)
        {
            DrawDebug();
        }
    }

    // === DOGFIGHT STATE ===

    private void UpdateDogfightLogic(double delta)
    {
        if (
            CurrentState != AIState.AttackRun
            && CurrentState != AIState.CombatFly
            && CurrentState != AIState.Retreat
        )
        {
            // Re-enable standard avoidance when not in dogfight
            // Only if globally enabled (Missiles disable this!)
            if (_pilot != null && EnableCollisionAvoidance)
                _pilot.CollisionAvoidanceEnabled = true;
            return;
        }

        // Logic Moved to LowHealthConsideration
        // The Brain now handles switching to Retreat
        // We just ensure we don't accidentally switch back unless the Brain says so

        if (_pilot != null)
            _pilot.CollisionAvoidanceEnabled = false;

        if (TargetNode == null)
            return;

        _stateTimer -= (float)delta;

        Vector3 myPos = _ship.Rb.GlobalPosition;
        Vector3 targetPos = TargetNode.GlobalPosition;
        Vector3 toTarget = targetPos - myPos;
        float dist = toTarget.Length();

        // --- SEPARATION FORCE ---
        // Simple repulsion from other NPCs to avoid clumping
        Vector3 separation = Vector3.Zero;
        var neighbors = GetTree().GetNodesInGroup("NPC");
        foreach (Node node in neighbors)
        {
            if (node == this || node == _ship.Rb)
                continue; // Skip self
            if (node is Node3D neighbor && neighbor.IsInsideTree())
            {
                Vector3 toNeighbor = myPos - neighbor.GlobalPosition;
                float neighborDistSq = toNeighbor.LengthSquared();
                if (neighborDistSq < 49.0f && neighborDistSq > 0.1f)
                {
                    separation += toNeighbor.Normalized() / Mathf.Sqrt(neighborDistSq) * 100.0f;
                }
            }
        }

        // DEBUG: Separation
        if (_debugDraw && separation.Length() > 0.1f)
        {
            DebugDraw3D.DrawLine(myPos, myPos + separation.Normalized() * 10f, Colors.Magenta);
        }

        // --- STATE SWITCHING ---
        // If too close, FORCE break away
        if (dist < 5.0f && !_inBreakAway)
        {
            _inBreakAway = true;
            _stateTimer = 2.0f;

            // ...
            Vector3 forward = -_ship.Rb.GlobalTransform.Basis.Z;
            Vector3 toTargetDir = toTarget.Normalized();
            Vector3 right = forward.Cross(Vector3.Up);
            float side = right.Dot(toTargetDir);
            _breakAwayDirection = (side > 0) ? -right : right;
            _breakAwayDirection = (_breakAwayDirection + forward * 0.5f).Normalized();

            GD.Print($"AI {Name}: Break Away! Dist={dist:F1}");
        }

        // If timer expires OR we are far enough away, switch back to attack
        if (_stateTimer <= 0f || (_inBreakAway && dist > 10.0f))
        {
            if (_inBreakAway && dist > 10.0f)
                GD.Print($"AI {Name}: Break Away Complete (Distance).");
            _inBreakAway = false;
        }

        // === TACTICAL STATE MACHINE ===

        // === TACTICAL STATE MACHINE ===

        // Logic Moved to EvasionConsideration
        // The Brain now handles switching to Evade Tactic based on hit time

        // 1. PHASE LOGIC
        switch (_tacticalPhase)
        {
            case TacticalPhase.Approach:
                // Goal: Reach advantage point
                Vector3 dest = GetTacticalPoint(AttackTactic);
                TargetPosition = dest;

                // If close enough, switch to ATTACK
                if ((dest - myPos).Length() < 15.0f)
                {
                    _tacticalPhase = TacticalPhase.Attack;
                    _stateTimer = 5.0f; // Attack for 5 seconds max
                }

                // B. Timeout (Don't orbit forever)
                _stateTimer -= (float)delta;
                if (_stateTimer <= 0)
                {
                    _tacticalPhase = TacticalPhase.Attack;
                    _stateTimer = 5.0f;
                }

                // C. Opportunity Fire (If we happen to align while flanking)
                Vector3 aimDir = (targetPos - myPos).Normalized();
                if ((-_ship.Rb.GlobalTransform.Basis.Z).Dot(aimDir) > 0.95f && dist < 250.0f)
                {
                    _tacticalPhase = TacticalPhase.Attack;
                    _stateTimer = 5.0f;
                }
                break;

            case TacticalPhase.Attack:
                // Goal: Eliminate target (Standard Dogfight)
                Vector3 targetVel = Vector3.Zero;
                if (TargetNode is RigidBody3D targetRb)
                    targetVel = targetRb.LinearVelocity;

                float timeToTarget = Mathf.Min(dist / 1.0f, 1.5f);
                Vector3 predictedPos = targetPos + (targetVel * timeToTarget);

                // Add wobble if Evading (Defensive attacking)
                if (AttackTactic == TacticalPosition.Evade)
                {
                    // Sine wave logic reused here for evade-attack
                    Vector3 dirToTarget = (predictedPos - myPos).Normalized();
                    Vector3 right = dirToTarget.Cross(Vector3.Up).Normalized();
                    float time = Time.GetTicksMsec() / 1000.0f;
                    float wave = Mathf.Sin(time * 1.0f + (_ship.Rb.GetInstanceId() % 100));
                    float distToT = (predictedPos - myPos).Length();
                    float amp = Mathf.Clamp(distToT * 0.25f, 15.0f, 80.0f);
                    predictedPos += right * wave * amp;
                }

                TargetPosition = predictedPos; // + separation is handled by generic Avoidance? No, by Boids logic usually.
                // We add separation manually? Previous code had '+ separation'.
                // If we are in AttackRun, we collide? No, we have separation force in Boids?
                // The previous code explicitly added 'separation' vector. I should calculate it if I replaced the block.
                // Assuming 'separation' var exists in scope (it does at line 180ish).
                TargetPosition += separation;

                // Triggers to Disengage
                _stateTimer -= (float)delta;
                if (dist < 10.0f || _stateTimer <= 0)
                {
                    _tacticalPhase = TacticalPhase.Disengage;
                }
                break;

            case TacticalPhase.Disengage:
                // Goal: Get out
                TargetPosition = myPos + (_ship.Rb.GlobalTransform.Basis.Z * 100.0f) + separation; // Fly forward/tangent?
                // Better: Fly away from enemy
                Vector3 away = (myPos - targetPos).Normalized();
                TargetPosition = myPos + away * 100.0f + separation;

                if (dist > 25.0f)
                {
                    PickNewTactic();
                    _tacticalPhase = TacticalPhase.Approach;
                    _stateTimer = 8.0f; // Reset timer for new approach
                }
                break;
        }
    }

    // Tactical state
    // _tacticalPhase defined at top of class
    public TacticalPhase CurrentTacticalPhase => _tacticalPhase;

    private void PickNewTactic()
    {
        // Randomly pick HeadOn, Flank, Rear
        var values = Enum.GetValues(typeof(TacticalPosition));
        // Exclude Evade (index?)
        int random = GD.RandRange(0, 3); // 0=Direct, 1=Left, 2=Right, 3=Behind?
        AttackTactic = (TacticalPosition)random;
        // Debug.Print($"AI New Tactic: {AttackTactic}");
    }

    private Vector3 GetTacticalPoint(TacticalPosition tactic)
    {
        if (TargetNode == null)
            return Vector3.Zero;
        Vector3 tPos = TargetNode.GlobalPosition;
        Basis tBasis = TargetNode.GlobalTransform.Basis;
        // If Target is not Nod3D, use identity? TargetNode is Node3D.

        float dist = 20.0f;

        switch (tactic)
        {
            case TacticalPosition.FlankLeft:
                return tPos - tBasis.X * dist; // Left
            case TacticalPosition.FlankRight:
                return tPos + tBasis.X * dist; // Right
            case TacticalPosition.Behind:
                return tPos + tBasis.Z * dist; // Behind (Assuming +Z is back)
            case TacticalPosition.Evade:
                // Orbit / Tangent
                Vector3 toMe = (_ship.Rb.GlobalPosition - tPos).Normalized();
                Vector3 tangent = toMe.Cross(Vector3.Up);
                return tPos + (toMe + tangent).Normalized() * dist;
            case TacticalPosition.Direct:
            default:
                return tPos + tBasis.Z * dist; // Front
        }
    }

    private void UpdateWeaponsLogic()
    {
        if (_ship?.FireSys == null || TargetNode == null)
        {
            if (_ship?.FireSys != null)
                _ship.FireSys.StopAllFiring();
            return;
        }

        // 1. Assign Target
        if (TargetNode is ITarget targetInterface)
        {
            _ship.FireSys.currentTarget = targetInterface;
        }

        // 2. Check Alignment
        Vector3 forward = -_ship.Rb.GlobalTransform.Basis.Z;
        Vector3 toTarget = (TargetPosition - _ship.Rb.GlobalPosition).Normalized();

        float dot = forward.Dot(toTarget);
        bool isAligned = dot > 0.97f; // Approx 14 degrees cone

        // 3. Get Valid Hardpoints (Checks Range + Ammo, stops invalid ones)
        // Note: GetHardpointsInRange calls StopFiring internally for out-of-range/ammo weapons
        var validHardpoints = _ship.FireSys.GetHardpointsInRange(TargetPosition, true);

        // 4. Fire Decision per Hardpoint
        foreach (var hp in validHardpoints)
        {
            bool canFire = false;

            // Turrets and Missiles can fire if in range (range checked by GetHardpointsInRange)
            // We assume Turrets handle their own aiming/rotation logic internally or in FireSystem
            if (hp.HardpointSpec.isTurret || hp.HardpointSpec is HardpointSpec.Missile)
            {
                canFire = true;
            }
            else
            {
                // Fixed weapons require ship alignment and not being in a breakaway maneuver
                if (isAligned && !_inBreakAway)
                {
                    canFire = true;
                }
            }

            if (canFire)
            {
                _ship.FireSys.activeHardpoint = hp; // Context for Fire()
                _ship.FireSys.Fire();
            }
            else
            {
                _ship.FireSys.StopFiring(hp);
            }
        }
    }

    /// <summary>
    /// Update pilot target position and orientation based on current AI state
    /// </summary>
    private void UpdatePilotTargets()
    {
        // Reset pilot defaults each frame
        _pilot.ArrivalRadius = ArrivalRadius;
        _pilot.SlowdownRadius = SlowdownRadius;

        if (CurrentState != AIState.Formation && _pilot.IgnoredColliders.Count > 0)
        {
            _pilot.IgnoredColliders.Clear();
        }

        switch (CurrentState)
        {
            case AIState.Idle:
                // Stop and stay still at current position
                _pilot.TargetPosition = _ship.Rb.GlobalPosition;
                _pilot.TargetOrientation = _ship.Rb.Rotation.Y;
                _pilot.DesiredSpeed = 0;
                _pilot.ArrivalRadius = 1.0f; // Small radius since we're already here
                break;

            case AIState.Seek:
                // Fly toward target at max speed
                _pilot.TargetPosition = TargetPosition;
                _pilot.DesiredSpeed = _ship.spec.maxSpeed;

                // MISSILE CORNERING: Handle by AIPilot now (Turn then Burn)
                _pilot.ArrivalRadius = 0.1f;
                _pilot.SlowdownRadius = 0.2f;
                break;

            case AIState.Arrive:
                // Fly to position and stop
                _pilot.TargetPosition = TargetPosition;
                _pilot.TargetOrientation = CalculateOrientationToward(TargetPosition);
                _pilot.DesiredSpeed = _ship.spec.maxSpeed;
                break;

            case AIState.ArriveOrient:
                // Fly to position, stop, and face specific direction
                _pilot.TargetPosition = TargetPosition;
                _pilot.TargetOrientation = TargetOrientation;
                _pilot.DesiredSpeed = _ship.spec.maxSpeed;
                break;

            case AIState.AttackRun:
                // Arcade Dogfight Logic
                // We use the pilot to steer towards a calculated intercept point
                // But we handle the "Breakaway" logic here by changing the target

                _pilot.TargetPosition = TargetPosition;
                _pilot.DesiredSpeed = _ship.spec.maxSpeed; // Default to max speed

                // CORNERING LOGIC: Slow down if we need to turn to face target
                // This prevents "orbiting" where we fly too fast to turn tight enough
                float angleToTarget = Mathf.Abs(_pilot.DebugAngleToTarget);
                if (angleToTarget > 45.0f)
                {
                    _pilot.DesiredSpeed *= 0.1f; // Slam brakes to turn
                }
                else if (angleToTarget > 20.0f)
                {
                    _pilot.DesiredSpeed *= 0.5f; // Slow down to corner
                }

                _pilot.ArrivalRadius = 2.0f;
                _pilot.SlowdownRadius = 10.0f;

                // If we are getting close, maintain speed!
                if ((TargetPosition - _ship.Rb.GlobalPosition).Length() < 15.0f)
                {
                    _pilot.DesiredSpeed = _ship.spec.maxSpeed;
                    _pilot.SlowdownRadius = 0.0f; // Don't slow down
                }
                break;

            case AIState.CombatFly:
                // Just fly towards the target vector (set by controller)
                _pilot.TargetPosition = TargetPosition;
                _pilot.DesiredSpeed = _ship.spec.maxSpeed;
                _pilot.SlowdownRadius = 0f;
                _pilot.ArrivalRadius = 5f;
                _pilot.ArrivalRadius = 5f;
                break;

            case AIState.Evasion:
                // PROJECT TARGET FAR AHEAD (Directional Evasion)
                // This prevents "Orbiting" or vacuum-cleaner behavior around a fixed point
                TargetPosition = _ship.Rb.GlobalPosition + _evasionDirection * 1000.0f;

                _pilot.TargetPosition = TargetPosition;
                _pilot.DesiredSpeed = _ship.spec.maxSpeed;
                _pilot.SlowdownRadius = 0f;
                _pilot.ArrivalRadius = 100.0f; // Ignored effectively

                // Count down evasion timer
                _stateTimer -= 0.016f;
                if (_stateTimer <= 0)
                {
                    PickNewTactic();
                    _tacticalPhase = TacticalPhase.Approach;
                    CurrentState = AIState.AttackRun;
                }
                break;

            case AIState.Retreat:
                _pilot.TargetPosition = TargetPosition;
                _pilot.DesiredSpeed = _ship.spec.maxSpeed;
                _pilot.SlowdownRadius = 0f;
                _pilot.ArrivalRadius = 5f;
                break;

            case AIState.Follow:
                // Follow the target node - stay behind them based on facing or velocity
                if (TargetNode != null && TargetNode is RigidBody3D targetRb)
                {
                    float followDist = 15.0f;

                    // Always calculate position based on target's facing direction
                    // This ensures the follow position updates even when target only rotates
                    Vector3 targetForward = -targetRb.GlobalTransform.Basis.Z; // -Z is forward in Godot
                    targetForward.Y = 0;
                    targetForward = targetForward.Normalized();

                    // Position behind target (opposite of their forward)
                    Vector3 followOffset = -targetForward * followDist;

                    _pilot.TargetPosition = TargetPosition + followOffset;
                    _pilot.TargetOrientation = targetRb.Rotation.Y;

                    // Match target's speed, or cruise slowly if target is stationary
                    float targetSpeed = targetRb.LinearVelocity.Length();
                    _pilot.DesiredSpeed =
                        targetSpeed > 1.0f ? targetSpeed : _ship.spec.maxSpeed * 0.3f;

                    _pilot.ArrivalRadius = 3.0f;
                    _pilot.SlowdownRadius = 15.0f;
                }
                break;

            case AIState.Formation:
                // Fly in formation relative to leader
                Node3D leaderNode = FormationLeaderBody;
                if (leaderNode == null && FormationLeader != null)
                    leaderNode = FormationLeader._ship?.Rb;

                if (leaderNode != null)
                {
                    // === IGNORE LEADER COLLISION ===
                    if (!_pilot.IgnoredColliders.Contains(leaderNode))
                    {
                        _pilot.IgnoredColliders.Add(leaderNode);
                    }
                    // Also ignore other wingmen if possible? 
                    // For now, just leader is the main issue.

                    Vector3 leaderPos = leaderNode.GlobalPosition;
                    float leaderRotY = leaderNode.Rotation.Y;
                    Vector3 leaderVel = Vector3.Zero;
                    if (leaderNode is RigidBody3D lrb) leaderVel = lrb.LinearVelocity;

                    // FEED-FORWARD VELOCITY MATCHING (New 3-Layer Feature)
                    // Pass leader's velocity directly to Pilot. This is the "God Mode" formation tool.
                    _pilot.MatchVelocity = leaderVel;

                    // Feed-Forward Prediction: Target where the leader WILL be
                    // This reduces "lag" in turns and speed changes
                    float lookaheadTime = 1.0f; 
                    Vector3 predictedLeaderPos = leaderPos + (leaderVel * lookaheadTime);

                    Vector3 formationSlotPos = Formation.GetFormationPosition(
                        FormationIndex, 
                        predictedLeaderPos, 
                        leaderRotY
                    );
                    _pilot.TargetOrientation = Formation.GetFormationOrientation(
                        FormationIndex, 
                        leaderRotY
                    );
                    
                    // === NAVIGATION SYSTEM INTEGRATION ===
                    if (_navSystem == null) _navSystem = new NavigationSystem();
                    
                    // Generate Path to the SLOT, not the leader
                    Queue<Vector3> path = _navSystem.GetPath(_ship.Rb, formationSlotPos, _pilot.IgnoredColliders);
                    
                    if (path.Count > 0)
                    {
                        Vector3 nextWaypoint = path.Peek();
                        // Fly to the waypoint. 
                        // Note: If we are close to it, we should probably dequeue it, 
                        // but NavSystem regenerates every frame currently so it will automatically 
                        // return the next point once we pass the detour.
                         _pilot.TargetPosition = nextWaypoint;
                    }
                    else
                    {
                         // Should not happen as GetPath always returns at least end point
                         _pilot.TargetPosition = formationSlotPos;
                    }
                    
                    float leaderSpeed = leaderVel.Length();
                    // Set DesiredSpeed to MAX to allow catch-up. 
                    // The AIPilot "Time Based Arrival" will naturally regulate speed to match LeaderSpeed 
                    // when we are at the correct Prediction Distance (Speed = Dist / 1.0).
                    _pilot.DesiredSpeed = _ship.spec.maxSpeed;
                    
                    // Match boost if falling behind
                    if (leaderSpeed > _ship.spec.maxSpeed + 5.0f || (_pilot.TargetPosition - _ship.Rb.GlobalPosition).Length() > 50.0f) 
                    {
                        _pilot.ForceBoost = true;
                    }
                    else
                    {
                        _pilot.ForceBoost = false;
                    }

                    _pilot.ArrivalRadius = 2.0f;
                    _pilot.SlowdownRadius = 25.0f; // Reduced from 50 to 25 to fix "crawling" approach
                }
                break;
        }
    }

    /// <summary>
    /// Calculate orientation to face toward a position
    /// </summary>
    private float CalculateOrientationToward(Vector3 targetPos)
    {
        Vector3 toTarget = targetPos - _ship.Rb.GlobalPosition;
        toTarget.Y = 0;
        if (toTarget.LengthSquared() < 0.1f)
            return _ship.Rb.Rotation.Y;
        return Mathf.Atan2(toTarget.X, -toTarget.Z);
    }

    /// <summary>
    /// Force a state change from external controller
    /// </summary>
    public void ForceState(AIState newState)
    {
        CurrentState = newState;
        // Reset timers or other transitional logic here if needed
        _stateTimer = 0;
        _inBreakAway = false;
    }

    /// <summary>
    /// Debug drawing for AI state visualization
    /// </summary>
    private void DrawDebug()
    {
        Vector3 shipPos = _ship.Rb.GlobalPosition;

        // Draw line to target (Green if Evading, Yellow otherwise)
        if (CurrentState == AIState.Evasion)
        {
            // LARGE OBVIOUS GREEN LINE AND SPHERE
            DebugDraw3D.DrawLine(shipPos, _pilot.TargetPosition, Colors.Red);
            // Draw a second offset line to simulate thickness/emphasis
            DebugDraw3D.DrawLine(
                shipPos + Vector3.Up,
                _pilot.TargetPosition + Vector3.Up,
                Colors.Green
            );

            DebugDraw3D.DrawSphere(_pilot.TargetPosition, 0.5f, Colors.Red); // Large sphere

            // Draw a giant arrow-head ish indicator
            Vector3 dir = (_pilot.TargetPosition - shipPos).Normalized();
            DebugDraw3D.DrawLine(shipPos, shipPos + dir * 100.0f, Colors.Red); // Short bright directional pointer
        }
        else
        {
            DebugDraw3D.DrawLine(shipPos, _pilot.TargetPosition, Colors.Yellow);
            DebugDraw3D.DrawSphere(_pilot.TargetPosition, 0.5f, Colors.Green);
        }

        // Draw target orientation arrow
        Vector3 orientDir = new Vector3(
            Mathf.Sin(_pilot.TargetOrientation),
            0,
            -Mathf.Cos(_pilot.TargetOrientation)
        );
        DebugDraw3D.DrawLine(
            _pilot.TargetPosition,
            _pilot.TargetPosition + orientDir * 3f,
            Colors.Cyan
        );

        // Draw current facing direction
        Vector3 forward = -_ship.Rb.Basis.Z * 2f;
        DebugDraw3D.DrawLine(shipPos, shipPos + forward, Colors.Red);
    }

    // === CONVENIENCE METHODS ===

    /// <summary>
    /// Set target to fly to a position and stop
    /// </summary>
    public void FlyTo(Vector3 position)
    {
        TargetNode = null; // Clear node so position isn't overwritten
        TargetPosition = position;
        CurrentState = AIState.Arrive;
    }

    /// <summary>
    /// Set target to fly to a position with specific facing direction
    /// </summary>
    public void FlyToAndFace(Vector3 position, float orientationRadians)
    {
        TargetNode = null; // Clear node so position isn't overwritten
        TargetPosition = position;
        TargetOrientation = orientationRadians;
        CurrentState = AIState.ArriveOrient;
    }

    /// <summary>
    /// Follow another entity
    /// </summary>
    public void FollowTarget(Node3D target)
    {
        TargetNode = target;
        CurrentState = AIState.Follow;
    }

    /// <summary>
    /// Set up formation flying
    /// </summary>
    public void JoinFormation(
        AIController leader,
        int index,
        FormationManager.FormationType formationType
    )
    {
        FormationLeader = leader;
        FormationIndex = index;
        Formation.CurrentFormation = formationType;
        CurrentState = AIState.Formation;
    }

    /// <summary>
    /// Attack target from specified angle
    /// </summary>
    public void AttackFrom(Node3D target, TacticalPosition tactic, float distance = 15.0f)
    {
        TargetNode = target;
        AttackTactic = tactic;
        AttackDistance = distance;
        CurrentState = AIState.AttackRun;
    }

    /// <summary>
    /// Check if AI has reached its destination
    /// </summary>
    public bool HasArrived => _pilot?.HasArrived ?? false;

    /// <summary>
    /// Check if AI is facing the correct direction
    /// </summary>
    public bool OrientationMatched => _pilot?.OrientationMatched ?? false;

    /// <summary>
    /// Get debug info string
    /// </summary>
    public string GetDebugInfo() =>
        (_pilot?.GetDebugInfo() ?? "No pilot") + "\n" + (_brain?.CurrentThought ?? "No Brain");

    /// <summary>
    /// Trigger an evasion maneuver against a specific threat
    /// </summary>
    public void EnterEvasion(Node3D threat)
    {
        if (CurrentState == AIState.Evasion && _stateTimer > 0)
            return; // Already evading

        CurrentState = AIState.Evasion;
        _stateTimer = (float)GD.RandRange(3.0, 6.0); // Longer commit time (3-6s) for "Human" feel

        Vector3 myPos = _ship.Rb.GlobalPosition;
        Vector3 threatDir;

        if (threat != null)
        {
            Vector3 threatPos = threat.GlobalPosition;
            // 1. Detect Threat Vector (Threat -> Me)
            threatDir = (myPos - threatPos).Normalized();
        }
        else
        {
            // No specific threat? Just panic in a random direction (mostly forwardish but erratic)
            // Or just a random vector on the plane
            threatDir = new Vector3((float)GD.RandRange(-1.0, 1.0), 0, (float)GD.RandRange(-1.0, 1.0)).Normalized();
        }
        Vector3 myVel = _ship.Rb.LinearVelocity;

        // 2. Decide Evasive Vector
        // Standard: Fly roughly away from threat (threatDir)
        // But also: Preserve momentum to keep it "Graceful"

        Vector3 desiredDir = threatDir;

        // If we have speed, bias away from direct U-turn
        if (myVel.Length() > 5.0f)
        {
            Vector3 currentDir = myVel.Normalized();

            // If threat is somewhat behind us (Dot > -0.5), just keep flying forward/away
            // If threat is directly in front (Dot < -0.8), we MUST turn (Beam reach?)

            // Bias: Mix "Away from Threat" with "Current Forward"
            // Start with perpendicular to threat (the classic 'Beam Reach' evasion)
            Vector3 right = threatDir.Cross(Vector3.Up).Normalized();

            // Which side is closer to our current velocity?
            if (right.Dot(currentDir) < 0)
                right = -right;

            // Blend: 40% Away, 60% Flank
            desiredDir = (threatDir * 0.4f + right * 0.6f).Normalized();
        }
        else
        {
            // Stationary? Just fly away perpendicular (random side)
            Vector3 right = threatDir.Cross(Vector3.Up).Normalized();
            if (GD.Randf() > 0.5f)
                right = -right;
            desiredDir = (threatDir * 0.3f + right * 0.7f).Normalized();
        }

        _evasionDirection = desiredDir;

        // Initial target set
        TargetPosition = myPos + _evasionDirection * 1000.0f;

        GD.Print(
            $"AI {Name}: Entering Evasion! Duration={_stateTimer:F1}s Dir={_evasionDirection}"
        );
    }
}
