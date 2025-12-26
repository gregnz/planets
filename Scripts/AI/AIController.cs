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
    // private Queue<Vector3> _currentPath; // Legacy
    // private Vector3 _currentPathDest; // Legacy
    // private const float WaypointAcceptanceRadius = 40.0f; // Legacy

    // Trail Following ("Chase the Rabbit")
    private struct TrailPoint
    {
        public Vector3 Position;
        public double Timestamp;
    }

    private List<TrailPoint> _trailPoints = new List<TrailPoint>();
    private double _lastTrailTime = 0;
    private const double TrailRecordInterval = 0.25; // Create a point every 0.25s
    private const double TrailMaxAge = 10.0; // Keep points for 10 seconds
    private Vector3 _currentRabbitPos = Vector3.Zero; // The specific point we are chasing

    // Debug
    private MeshInstance3D _debugMeshInstance;
    private ImmediateMesh _debugImmediateMesh;
    private Material _debugMaterial;

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
        Warp, // High speed travel
    }

    public enum TacticalPhase
    {
        Approach,
        Attack,
        Disengage,
    }

    // === EXPORTED PROPERTIES ===
    [Export] public AIState CurrentState { get; set; } = AIState.Arrive;

    [ExportGroup("Tuning")] [Export] public float ArrivalRadius { get; set; } = 5.0f;

    [Export] public float SlowdownRadius { get; set; } = 25.0f;

    [Export] public float OrientationTolerance { get; set; } = 0.05f;


    [ExportGroup("Debug")] [Export] private bool _debugDraw = true;

    // === TARGET PROPERTIES ===
    public Node3D TargetNode { get; set; } = null;
    public Vector3 TargetPosition { get; set; } = Vector3.Zero;
    public float TargetOrientation { get; set; } = 0.0f;
    public TacticalPosition AttackTactic { get; set; } = TacticalPosition.Direct;
    public float AttackDistance { get; set; } = 15.0f;
    public Vector3 WarpTarget { get; set; }

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

    // Optimization: Throttled Steering
    private double _steeringTimer = 0;
    private const double SteeringInterval = 0.1; // 10Hz updates
    private Vector3 _cachedBestDirection = Vector3.Forward;


    private void SetupDebugDrawing()
    {
        _debugImmediateMesh = new ImmediateMesh();
        _debugMeshInstance = new MeshInstance3D
        {
            Mesh = _debugImmediateMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };

        // Ensure it's visible in game
        var material = new StandardMaterial3D();
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        material.VertexColorUseAsAlbedo = true;
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _debugMeshInstance.MaterialOverride = material;
        _debugMeshInstance.TopLevel = true; // Draw in Global Coordinates independent of ship rotation

        AddChild(_debugMeshInstance);
    }

    private bool _shouldLog = false;
    private int _logFrame = 0;

    public override void _Ready()
    {
        _pilot = new AIPilot
        {
            ArrivalRadius = ArrivalRadius,
            SlowdownRadius = SlowdownRadius,
        };

        _navSystem = new NavigationSystem(); // Initialize NavSystem

        Formation = new FormationManager();
        _brain = new AIDecisionEngine(this); // Initialize Brain

        SetupDebugDrawing();
        SetPhysicsProcess(true);

        // Randomize initial timer to stagger updates (Time Slicing)
        _steeringTimer = GD.RandRange(0.0f, (float)SteeringInterval);

        // Enable logging for Alpha 2
        // Check parent name because this node is likely named "AIController"
        if (GetParent().Name.ToString().Contains("Alpha 2"))
        {
            _shouldLog = true;
            GD.Print("AIController: Logging Enabled for Alpha 2");
        }

        GD.Print($"AIController Ready: State={CurrentState}");
    }

    public override void _Process(double delta)
    {
        // ... (Existing Process logic if any, currently AI logic is in PhysicsProcess)
        DrawDebugPath();
    }

    public override void _PhysicsProcess(double delta)
    {
        _steeringTimer -= delta; // Decrement throttling timer

        if (_shouldLog)
        {
            _logFrame++;
            if (_logFrame % 10 == 0) // Log 6 times a second
            {
                string pilotDebug = _pilot != null ? _pilot.GetDebugString() : "null";
                float dist = 0f;
                if (_ship?.Rb != null)
                {
                    dist = TargetPosition.DistanceTo(_ship.Rb.GlobalPosition);
                }

                string targetName = TargetNode != null ? TargetNode.Name.ToString() : "None";

                GD.Print(
                    $"[Alpha 2] State:{CurrentState} Dist:{dist:F1} Tgt:{targetName} {pilotDebug} Phase:{_tacticalPhase} Tactic:{AttackTactic}");
            }
        }

        if (_ship?.Rb == null || _ship.spec == null)
        {
            GD.PrintErr("AIController: Ship or spec is null!");
            return;
        }

        // Breadcrumbs


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
            return;
        }

        // Logic Moved to LowHealthConsideration
        // The Brain now handles switching to Retreat
        // We just ensure we don't accidentally switch back unless the Brain says so

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

        // COMBAT FLY (Orbit/Broadside) OVERRIDE
        if (CurrentState == AIState.CombatFly)
        {
            // For CombatFly (Turret/Capital), we want to maintain the Tactical Position (Orbit)
            // instead of closing in for a dogfight intercept.
            Vector3 dest = GetTacticalPoint(AttackTactic);
            TargetPosition = dest;

            // Optional: Periodically switch sides or orbit?
            // For now, holding the Flank/Tactical position is sufficient for broadsiding.
            // Boids separation (calculated above) is added effectively by the Pilot logic or separate pass?
            // In UpdateDogfightLogic, separation was added to TargetPosition in Attack phase.
            // Let's add it here too.
            if (IsInstanceValid(TargetNode))
            {
                // Add separation force to destination to avoid clumping
                TargetPosition += separation;
            }

            return;
        }

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

                TargetPosition =
                    predictedPos; // + separation is handled by generic Avoidance? No, by Boids logic usually.
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
                TargetPosition =
                    myPos + (_ship.Rb.GlobalTransform.Basis.Z * 100.0f) + separation; // Fly forward/tangent?
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

        float dist = 5.0f;
        return tPos + tBasis.Z * dist; // Front

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

        // Warp Logic
        _ship.InWarp = (CurrentState == AIState.Warp);

        if (CurrentState != AIState.Formation && _pilot.IgnoredColliders.Count > 0)
        {
            _pilot.IgnoredColliders.Clear();
        }

        switch (CurrentState)
        {
            case AIState.Warp:
                _pilot.TargetPosition = WarpTarget;
                _pilot.DesiredSpeed = ShipController.WarpSpeed;
                _pilot.Boosting = true;
                break;

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
            {
                // Arcade Dogfight Logic
                // We use the pilot to steer towards a calculated intercept point
                // But we handle the "Breakaway" logic here by changing the target

                // Use Context Steering to navigate to the attack position
                // This ensures obstacle avoidance and smooth flight (Slipstreaming)
                // Use Throttled Wrapper
                Vector3 bestDir = GetSteeringDirection(TargetPosition);

                // "Carrot on a stick" - Project target out to ensure full speed
                // But if we are very close to the strategic point, allow direct arrival
                float distToDest = (_ship.Rb.GlobalPosition - TargetPosition).Length();
                if (distToDest < 20.0f)
                {
                    _pilot.TargetPosition = TargetPosition;
                }
                else
                {
                    _pilot.TargetPosition = _ship.Rb.GlobalPosition + bestDir * 100.0f;
                }

                _pilot.DesiredSpeed = _ship.spec.maxSpeed; // Default to max speed

                _pilot.ArrivalRadius = 5.0f;
                _pilot.SlowdownRadius = 0.0f; // Disable slowdown for attack runs to prevent jerking

                // If we are getting close, maintain speed!
                if (distToDest < 30.0f)
                {
                    // Ensure we don't stop even if "arriving" at the tactical point
                    _pilot.DesiredSpeed = _ship.spec.maxSpeed;
                }

                // Ignore target collision during attack run
                if (TargetNode != null && !_pilot.IgnoredColliders.Contains(TargetNode))
                {
                    _pilot.IgnoredColliders.Add(TargetNode);
                }

                break;
            }

            case AIState.CombatFly:
                // Just fly towards the target vector (set by controller)
                _pilot.TargetPosition = TargetPosition;
                _pilot.DesiredSpeed = _ship.spec.maxSpeed;
                _pilot.SlowdownRadius = 0f;
                _pilot.ArrivalRadius = 5f;
                _pilot.ArrivalRadius = 5f;
                // Ignore target collision during combat fly
                if (TargetNode != null && !_pilot.IgnoredColliders.Contains(TargetNode))
                {
                    _pilot.IgnoredColliders.Add(TargetNode);
                }

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

                    // === NAVIGATION SYSTEM (CONTEXT STEERING) ===
                    // Use Throttled Wrapper
                    Vector3 bestDir = GetSteeringDirection(formationSlotPos);

                    // Set Target as "Carrot on a stick"
                    // If we are close to the target (< 20m), just fly directly to it to arrive logic works.
                    float distToTarget = _ship.Rb.GlobalPosition.DistanceTo(formationSlotPos);
                    // Always use Context Steering for the target direction
                    // But scale the offset by the actual distance so the Pilot calculates the correct arrival braking.
                    _pilot.TargetPosition = _ship.Rb.GlobalPosition + bestDir * distToTarget;


                    float leaderSpeed = leaderVel.Length();
                    // Set DesiredSpeed to MAX to allow catch-up. 
                    // The AIPilot "Time Based Arrival" will naturally regulate speed to match LeaderSpeed 
                    // when we are at the correct Prediction Distance (Speed = Dist / 1.0).
                    _pilot.DesiredSpeed = _ship.spec.maxSpeed;

                    // Match boost if falling behind
                    if (leaderSpeed > _ship.spec.maxSpeed + 5.0f ||
                        (_pilot.TargetPosition - _ship.Rb.GlobalPosition).Length() > 50.0f)
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

    private void DrawDebugPath()
    {
        // Debug Print to check visibility
        // if (_logFrame % 60 == 0) GD.Print($"AI {Name}: Trail Count={_trailPoints.Count} Rabbit={_currentRabbitPos}");

        _debugImmediateMesh.ClearSurfaces();

        // DRAW NAV SYSTEM RAYS (Context Map Visualization)
        if (_navSystem != null)
        {
            Vector3 shipPos = _ship.Rb.GlobalPosition;
            float[] danger = _navSystem.DangerMap;
            float[] interest = _navSystem.InterestMap;
            Vector3[] dirs = _navSystem.RayDirections;

            // First pass: Count vertices and check if we have anything to draw
            bool hasVertices = false;

            _debugImmediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

            for (int i = 0; i < dirs.Length; i++)
            {
                float d = danger[i];
                float val = interest[i];

                if (d > 0.1f)
                {
                    _debugImmediateMesh.SurfaceSetColor(Colors.Red);
                    Vector3 end = shipPos + dirs[i] * (d * 50.0f);
                    _debugImmediateMesh.SurfaceAddVertex(shipPos);
                    _debugImmediateMesh.SurfaceAddVertex(end);
                    hasVertices = true;
                }
                else if (val > 0.1f)
                {
                    _debugImmediateMesh.SurfaceSetColor(Colors.Green);
                    Vector3 end = shipPos + dirs[i] * (val * 20.0f);
                    _debugImmediateMesh.SurfaceAddVertex(shipPos);
                    _debugImmediateMesh.SurfaceAddVertex(end);
                    hasVertices = true;
                }
            }

            // Prevent crash: "No vertices were added"
            if (!hasVertices)
            {
                // Draw a tiny invisible line to satisfy Godot
                _debugImmediateMesh.SurfaceSetColor(new Color(0, 0, 0, 0));
                _debugImmediateMesh.SurfaceAddVertex(shipPos);
                _debugImmediateMesh.SurfaceAddVertex(shipPos + Vector3.Up * 0.01f);
            }

            _debugImmediateMesh.SurfaceEnd();
        }

        if (_navSystem != null)
        {
            var rays = _navSystem.RayDirections;
            var interest = _navSystem.InterestMap;
            var danger = _navSystem.DangerMap;
            Vector3 bestDir = _navSystem.BestDirection;

            if (rays == null) return;

            _debugImmediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            Vector3 startP = _ship.Rb.GlobalPosition;

            for (int i = 0; i < rays.Length; i++)
            {
                Vector3 rayDir = rays[i];

                // Draw base ray (Faint Grey)
                _debugImmediateMesh.SurfaceSetColor(new Color(0.5f, 0.5f, 0.5f, 0.1f));
                _debugImmediateMesh.SurfaceAddVertex(startP);
                _debugImmediateMesh.SurfaceAddVertex(startP + rayDir * 2.0f);

                // Draw Interest (Green)
                if (interest[i] > 0)
                {
                    _debugImmediateMesh.SurfaceSetColor(Colors.Green);
                    _debugImmediateMesh.SurfaceAddVertex(startP);
                    _debugImmediateMesh.SurfaceAddVertex(startP + rayDir * interest[i] * 5.0f);
                }

                // Draw Danger (Red)
                if (danger[i] > 0)
                {
                    _debugImmediateMesh.SurfaceSetColor(Colors.Red);
                    _debugImmediateMesh.SurfaceAddVertex(startP);
                    _debugImmediateMesh.SurfaceAddVertex(startP + rayDir * danger[i] * 10.0f);
                }
            }

            // Draw Best Dir (Blue)
            _debugImmediateMesh.SurfaceSetColor(Colors.Blue);
            _debugImmediateMesh.SurfaceAddVertex(startP);
            _debugImmediateMesh.SurfaceAddVertex(startP + bestDir * 15.0f);


            // Draw Trail (Cyan) and Rabbit (Magenta)
            if (_trailPoints != null)
            {
                foreach (var pt in _trailPoints)
                {
                    Vector3 crumb = pt.Position;
                    float size = 0.5f;
                    _debugImmediateMesh.SurfaceSetColor(Colors.Cyan);
                    _debugImmediateMesh.SurfaceAddVertex(crumb + Vector3.Up * size);
                    _debugImmediateMesh.SurfaceAddVertex(crumb - Vector3.Up * size);
                    _debugImmediateMesh.SurfaceAddVertex(crumb + Vector3.Right * size);
                    _debugImmediateMesh.SurfaceAddVertex(crumb - Vector3.Right * size);
                    _debugImmediateMesh.SurfaceAddVertex(crumb + Vector3.Forward * size);
                    _debugImmediateMesh.SurfaceAddVertex(crumb - Vector3.Forward * size);
                }
            }

            // Draw Rabbit
            if (_currentRabbitPos != Vector3.Zero)
            {
                Vector3 r = _currentRabbitPos;
                _debugImmediateMesh.SurfaceSetColor(Colors.Magenta);
                _debugImmediateMesh.SurfaceAddVertex(r + Vector3.Up * 2);
                _debugImmediateMesh.SurfaceAddVertex(r - Vector3.Up * 2);
                _debugImmediateMesh.SurfaceAddVertex(r + Vector3.Right * 2);
                _debugImmediateMesh.SurfaceAddVertex(r - Vector3.Right * 2);
                _debugImmediateMesh.SurfaceAddVertex(r + Vector3.Forward * 2);
                _debugImmediateMesh.SurfaceAddVertex(r - Vector3.Forward * 2);
            }

            _debugImmediateMesh.SurfaceEnd();
        }
    }

    private void UpdateTrail()
    {
        if (TargetNode != null)
        {
            double now = Time.GetTicksMsec() / 1000.0;
            if (now - _lastTrailTime > TrailRecordInterval)
            {
                Vector3 pos = TargetNode.GlobalPosition;

                // Add new point
                _trailPoints.Add(new TrailPoint { Position = pos, Timestamp = now });
                _lastTrailTime = now;

                // Prune old points
                while (_trailPoints.Count > 0 && (now - _trailPoints[0].Timestamp > TrailMaxAge))
                {
                    _trailPoints.RemoveAt(0);
                }
            }
        }
    }

    /// <summary>
    /// Finds the "Rabbit" - the furthest visible point on the trail to chase.
    /// </summary>
    private Vector3 GetPursuitTarget()
    {
        // Default: Target Position
        if (TargetNode == null) return TargetPosition;

        Vector3 targetPos = TargetNode.GlobalPosition;
        PhysicsDirectSpaceState3D spaceState = _ship.Rb.GetWorld3D().DirectSpaceState;

        // Check if we can see the target directly
        if (CanSeePoint(spaceState, targetPos))
        {
            _currentRabbitPos = targetPos;
            return targetPos;
        }

        // If not, find the freshest trail point we CAN see
        // Iterate backwards (Newest -> Oldest)
        for (int i = _trailPoints.Count - 1; i >= 0; i--)
        {
            if (CanSeePoint(spaceState, _trailPoints[i].Position))
            {
                _currentRabbitPos = _trailPoints[i].Position;
                return _currentRabbitPos;
            }
        }

        // Fallback: If we see nothing, just aim at the last known (Oldest).
        if (_trailPoints.Count > 0)
        {
            _currentRabbitPos = _trailPoints[0].Position;
            return _currentRabbitPos;
        }

        return targetPos;
    }

    private bool CanSeePoint(PhysicsDirectSpaceState3D spaceState, Vector3 point)
    {
        Vector3 from = _ship.Rb.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(from, point);

        // Exclude self
        Godot.Collections.Array<Godot.Rid> exclude = new Godot.Collections.Array<Godot.Rid>();
        exclude.Add(_ship.Rb.GetRid());

        // Exclude Target (we want to see THOUGH the target to the point, but target blocks ray?)
        if (TargetNode is CollisionObject3D targetCol) exclude.Add(targetCol.GetRid());

        // Mask 1 is default (Environment usually).
        // Let's assume Mask 1.

        query.Exclude = exclude;

        var result = spaceState.IntersectRay(query);
        // If no hit, we see it.
        return result.Count == 0;
    }

    private Vector3 GetSteeringDirection(Vector3 target)
    {
        if (_steeringTimer <= 0)
        {
            // Use Pursuit Logic if following a target
            Vector3 finalTarget = target;
            if (CurrentState == AIState.AttackRun || CurrentState == AIState.Seek)
            {
                UpdateTrail();
                finalTarget = GetPursuitTarget();
            }

            // Just pass the single target to NavigationSystem
            // We NO LONGER pass the breadcrumb list
            _cachedBestDirection = _navSystem.GetBestDirection(
                _ship.Rb,
                finalTarget,
                _pilot.IgnoredColliders,
                _ship.currentSpeed
            );
            _steeringTimer = SteeringInterval;
        }

        return _cachedBestDirection;
    }
}
