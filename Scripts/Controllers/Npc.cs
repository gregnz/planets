using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Godot.Collections;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Core;
using utils;

namespace Planetsgodot.Scripts.Controllers;

public partial class Npc : RigidBody3D, IDamageable, ITarget
{
    private CombatDirector.Squadron squadron;
    public CombatDirector.Squadron.Attitude Attitude =>
        squadron?.attitude ?? CombatDirector.Squadron.Attitude.None;
    private static System.Random rnd = new System.Random();
    private bool amIDead = false;
    private Skill skill;

    private SignalBus signalBus;

    internal GameController gameController;
    private ShipFactory.ShipType shipType;

    // AI stuff
    // private EnemyAI ai = new EnemyAI();
    // private CombatDirector.Order currentOrder;
    Vector3 targetPosition;

    // public GameObject target;

    public enum Skill
    {
        Rookie,
        Experienced,
        Ace,
        AcePlusPlus,
    }

    public ShipFactory.ShipSpec shipSpecification;
    private ShipController shipController = new ShipController();
    private FireSystem fireSystem;

    [System.Serializable]
    public struct Personality
    {
        public float Aggression; // 0-1
        public float Loyalty;    // 0-1
        public float SelfPreservation; // 0-1

        public static Personality Default => new Personality { Aggression = 0.5f, Loyalty = 0.9f, SelfPreservation = 0.5f };
        public static Personality Wingman => new Personality { Aggression = 0.7f, Loyalty = 1.0f, SelfPreservation = 0.3f };
    }

    public Personality MyPersonality = Personality.Default;
    public CombatDirector.SquadOrder CurrentSquadOrder;

    // public GameObject explosion;

    float yRot = 0;
    float zRot = 0;

    private float t = 0;

    // private ShieldModifier _shieldModifier;
    private LineRenderer targetLine;

    private AIController shipAi;
    private MeshInstance3D _debugTargetSphere;

    // GUI DEBUG PROPERTIES
    public string DebugThreatName { get; private set; } = "None";
    public float DebugMinTimeImpact { get; private set; } = 0f;
    private Shield shield;

    public override void _Ready()
    {
        // Add to NPC group for collision avoidance detection
        AddToGroup("NPC");

        skill = Skill.Rookie;
        shipSpecification = ShipFactory.GetPreset(ShipFactory.ShipType.Anaconda);
        fireSystem = new FireSystem(this);
        shipController.initialiseFromSpec(shipSpecification, this, fireSystem);

        new ShipBuilder().Build(shipSpecification, this);

        shield = new Shield();
        GetNode("Visual").AddChild(shield);
        GD.Print($"NPC Shield Created. Name: '{shield.Name}' Path: '{shield.GetPath()}'");
        shipController.SetShieldVisuals(shield); // Connect shield visuals to controller

        // _shieldModifier = GetComponentInChildren<ShieldModifier>();
        fireSystem.Initialise(shipSpecification);
        signalBus = GetNode<SignalBus>("/root/SignalBus");
        shipAi = GetNode<AIController>("AIController");
        shipAi._ship = shipController;
        
        // Re-apply pending order if one exists (was set before Ready)
        if (CurrentSquadOrder.Type != CombatDirector.OrderType.None)
        {
            ReceiveOrder(CurrentSquadOrder);
        }
        // Debug Sphere
        _debugTargetSphere = new MeshInstance3D();
        var sphereMesh = new SphereMesh();
        sphereMesh.Radius = 0.5f;
        sphereMesh.Height = 1.0f;
        var material = new StandardMaterial3D();
        material.AlbedoColor = new Color(1, 0, 0, 0.2f);
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        sphereMesh.Material = material;
        _debugTargetSphere.Mesh = sphereMesh;
        _debugTargetSphere.TopLevel = true; // Use global coords
        _debugTargetSphere.Visible = false;
        AddChild(_debugTargetSphere);

        // shipAi.TargetNode = (PlayerController)GetNode("/root/Root3D/Player"); // Removed auto-target
        
        // === DISABLE FRIENDLY COLLISIONS ===
        // Prevents spinning/jitter in formation
        var friendlies = GetTree().GetNodesInGroup("NPC");
        foreach (Node node in friendlies)
        {
            if (node is CollisionObject3D otherBody && node != this)
            {
                // AddCollisionExceptionWith(otherBody);
                // Also Make them ignore me (Dual Link) if needed, but they run this too
            }
        }

        // Ignore Player Collision too?
        var player = GetTree().GetFirstNodeInGroup("Player");
        if (player is CollisionObject3D playerBody)
        {
            // AddCollisionExceptionWith(playerBody); 
        }

        // === 3D TARGETING RETICULE ===
        // Remove old sprite if it exists (legacy support)
        var oldReticule = GetNodeOrNull<Sprite3D>("TargetedReticule");
        if (oldReticule != null) oldReticule.QueueFree();

        // Create new 3D Reticule
        var reticuleMesh = GetNodeOrNull<MeshInstance3D>("Reticule3D");
        if (reticuleMesh == null)
        {
            reticuleMesh = new MeshInstance3D();
            reticuleMesh.Name = "Reticule3D";
            AddChild(reticuleMesh);

            // Geometry: Torus
            var torus = new TorusMesh();
            torus.InnerRadius = 0.9f;
            torus.OuterRadius = 1.0f;
            torus.Rings = 64;
            torus.RingSegments = 4; // Flat ring look
            reticuleMesh.Mesh = torus;

            // Scale
            reticuleMesh.Scale = Vector3.One * 2f; // Large enough to surround ship
            reticuleMesh.Visible = false;

            // Shader Material
            var shaderMat = new ShaderMaterial();
            // Load shader from file
            var shader = GD.Load<Shader>("res://Shaders/reticule.gdshader");
            shaderMat.Shader = shader;
            reticuleMesh.MaterialOverride = shaderMat;
        }
    }
    


    public override void _Input(InputEvent inputEvent)
    {
        // Debug keys to test AI states
        Key[] npcStateKeys = { Key.Q, Key.W, Key.E, Key.R, Key.T };

        foreach (var k in npcStateKeys)
        {
            if (Input.IsKeyPressed(k))
            {
                Debug.Print($"NPC Debug Key pressed! {k}");
                if (k == Key.Q)
                {
                    // Seek - chase the player aggressively
                    var player = GetNode<Node3D>("/root/Root3D/Player");
                    shipAi.TargetNode = player;
                    shipAi.CurrentState = AIController.AIState.Seek;
                    Debug.Print("AI State: Seek (chase player)");
                }

                if (k == Key.W)
                {
                    // Idle - stop moving
                    shipAi.CurrentState = AIController.AIState.Idle;
                    Debug.Print("AI State: Idle (stop)");
                }

                if (k == Key.E)
                {
                    // Arrive and Orient - fly to a point and face the player
                    shipAi.FlyToAndFace(new Vector3(20, 0, 20), 0);
                    Debug.Print("AI State: ArriveOrient (go to 20,0,20 facing north)");
                }

                if (k == Key.R)
                {
                    // Attack from flank
                    var player = GetNode<PlayerController>("/root/Root3D/Player");
                    shipAi.AttackFrom(player, AI.TacticalPosition.FlankLeft, 15.0f);
                    Debug.Print("AI State: AttackRun (flank left)");
                }

                if (k == Key.T)
                {
                    // Follow player
                    var player = GetNode<Node3D>("/root/Root3D/Player");
                    shipAi.FollowTarget(player);
                    Debug.Print("AI State: Follow (follow player)");
                }
            }
        }
    }

    void FindObstacles(out List<Vector3> positions, out List<Vector3> velocities)
    {
        positions = new List<Vector3>();
        velocities = new List<Vector3>();

        List<ITarget> targets = gameController.possibleTargets;
        int i = 0;

        foreach (ITarget t in targets)
        {
            if (t.Equals(this))
                continue; // Skip "Me"
            positions.Add(t.Position);
            velocities.Add(t.LinearVelocity);
        }

        positions.Add(gameController.player.Position);
        velocities.Add(gameController.player.LinearVelocity);
    }

    private ITarget currentThreat = null;

    private void AssessThreats(double delta)
    {
        // 0. Cleanup invalid threat
        if (currentThreat != null && !IsInstanceValid(currentThreat as Node))
        {
            currentThreat = null;
        }

        // 1. Scan for missiles targeting me
        List<ITarget> threats = new List<ITarget>();
        foreach (ITarget t in gameController.possibleTargets)
        {
            // Debug.Print($"NPC {Name}: Scanning {t} (Type: {t.GetType().Name})");

            if (!IsInstanceValid(t as Node))
                continue;


        }

        // Debug
        if (gameController.possibleTargets.Count > 0)
        {
            // Debug.Print(
            //     $"NPC {Name}: Scanned {gameController.possibleTargets.Count} targets. Found {threats.Count} threats."
            // );
        }

        // Debug
        // if (threats.Count > 0) Debug.Print($"NPC {Name}: Found {threats.Count} missile threats.");

        if (threats.Count == 0)
        {
            if (currentThreat != null)
            {
                // Threat cleared, revert to default target (Player)
                // In a full implementation, we'd query CombatDirector for the 'real' target.
                currentThreat = null;
                shipAi.TargetNode = (Node3D)gameController.player;
                shipAi.AttackTactic = AI.TacticalPosition.Direct; // Reset tactic override
                Debug.Print($"NPC {Name}: Threat cleared. Reverting to Player.");
            }
            return;
        }

        // 2. Evaluate closest threat
        ITarget closestThreat = null;
        float minTimeImpact = float.MaxValue;

        foreach (ITarget t in threats)
        {
            float dist = Position.DistanceTo(t.Position);
            float speed = (t.LinearVelocity - LinearVelocity).Length(); // Relative speed
            if (speed < 1.0f)
                speed = 1.0f;

            float timeToImpact = dist / speed;

            if (timeToImpact < minTimeImpact)
            {
                minTimeImpact = timeToImpact;
                closestThreat = t;
            }
        }

        // Debug Properties update
        DebugThreatName = closestThreat?.ToString() ?? "None";
        DebugMinTimeImpact = closestThreat != null ? minTimeImpact : 0f;

        if (closestThreat == null)
            return;
        currentThreat = closestThreat;

        // 3. Fight or Flight
        // Estimate Time To Kill (TTK)
        float myDps = 50.0f;
        float missileHealth = 0.01f;
        float timeToKill = missileHealth / myDps;
        timeToKill += 10.0f; // Reaction buffer check

        // Debug
        Debug.Print(
            $"NPC {Name}: Closest Missile {closestThreat}. Impact {minTimeImpact:F2}s vs Kill {timeToKill:F2}s"
        );

        if (timeToKill < minTimeImpact * 0.5f) // Safer margin (was implicit 1.0)
        {
            // FIGHT: Target missile
            if (shipAi.TargetNode != closestThreat.GetRigidBody3D())
            {
                shipAi.TargetNode = closestThreat.GetRigidBody3D();
                shipAi.CurrentState = AIController.AIState.CombatFly; // Use Boids-style approach to avoid ramming
                Debug.Print($"NPC {Name}: FIGHT MODE! Engaging Missile {closestThreat}");
            }
        }
        else
        {
            // FLIGHT: Evade
            // If we can't kill it, why wait until 4s? Evade sooner!
            // Increased threshold from 4.0f to 12.0f for earlier reaction against fast missiles
            // Also, if it's very close (< 2.0s), this is a PANIC evade (we might want to boost trigger here too)

            if (minTimeImpact < 12.0f)
            {
                if (closestThreat is Node3D threatNode)
                {
                    shipAi.EnterEvasion(threatNode);
                }
            }
            // Debug.Print($"NPC {Name}: FLIGHT MODE! Evading Missile (Impact {minTimeImpact:F2}s)");
        }
    }

    public override void _Process(double delta)
    {
        AssessThreats(delta);

        // Update debug sphere
        if (_debugTargetSphere != null)
        {
            if (shipAi != null && shipAi.TargetNode != null && IsInstanceValid(shipAi.TargetNode))
            {
                _debugTargetSphere.Visible = true;
                _debugTargetSphere.GlobalPosition = shipAi.TargetNode.GlobalPosition;
            }
            else
            {
                _debugTargetSphere.Visible = false;
            }
        }

        t += (float)delta;
        if (t > 1)
        {
            signalBus.EmitSignal("NpcTargetChanged", GetStatus());
            // Debug.Print("NPC Signal emitted");
            t = 0;
        }

        fireSystem.Update(delta);

        // Visual Update for Reticule (New Logic)
        // Visual Update for Reticule (New Logic)
        var reticule = GetNodeOrNull<MeshInstance3D>("Reticule3D");
        if (reticule != null && reticule.Visible)
        {
            // Rotate 90 degrees per second
            reticule.RotateY((float)delta * 1.5f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        FollowOrder(delta);

        // Keep ship in top-down plane - prevent tipping from collisions
        ConstrainRotation();
    }

    /// <summary>
    /// Constrain rotation to Y-axis only (yaw) - prevents ships from tipping over
    /// </summary>
    private void ConstrainRotation()
    {
        // Reset X and Z rotation, keep only Y (yaw)
        Vector3 rot = Rotation;
        if (Mathf.Abs(rot.X) > 0.01f || Mathf.Abs(rot.Z) > 0.01f)
        {
            Rotation = new Vector3(0, rot.Y, 0);
        }

        // Also constrain angular velocity to Y axis only
        Vector3 angVel = AngularVelocity;
        if (Mathf.Abs(angVel.X) > 0.01f || Mathf.Abs(angVel.Z) > 0.01f)
        {
            AngularVelocity = new Vector3(0, angVel.Y, 0);
        }

        // Keep Y position at 0 (on the play plane)
        if (Mathf.Abs(Position.Y) > 0.1f)
        {
            Vector3 pos = Position;
            pos.Y = 0;
            Position = pos;

            Vector3 vel = LinearVelocity;
            vel.Y = 0;
            LinearVelocity = vel;
        }
    }

    public void FollowOrder(double delta)
    {
        if (amIDead)
            return;

        // AI has updated MovementX, MovementY, boosting in _PhysicsProcess
        // Now apply those controls through the ship controller (same as player)
        shipController.HandleMovement(shipSpecification, this, delta, false, true);
    }

    void FireDecision(Vector3 tp)
    {
        // Choose active hardpoint
        //      - Ammo?
        //      - Range
        //      - Probability of success
        //      - Damage, target position (for defensive measures)
        // https://www.gdcvault.com/play/1024679/Math-for-Game-Programmers-Predictable

        List<ShipFactory.ShipSpec.Hardpoint> hps = fireSystem.GetHardpointsInRange(tp, true);
        Debug.Print(@$"Fire AI: In range:  {hps}");

        foreach (ShipFactory.ShipSpec.Hardpoint h in hps)
        {
            // Debug.Print(@$"Fire AI: Turret: {h.HardpointSpec.isTurret} Missile: {h.HardpointSpec is HardpointSpec.Missile}");
            // if (
            //     ai.IsFacingObject(transform.forward, transform.position,
            //         currentOrder.GetCurrentAction().Target.transform.position) ||
            //     h.HardpointSpec.isTurret || h.HardpointSpec is HardpointSpec.Missile
            // )
            // {
            //     fireSystem.activeHardpoint = h;
            //     fireSystem.currentTarget = currentOrder.GetCurrentAction().Target;
            //     fireSystem.Fire();
            //     break;
            // }
            // else fireSystem.StopFiring();
        }
    }

    float m_Angle;
    public CombatDirector.Squadron.Attitude attitude;
    public string name;

    void OnGUI()
    {
        //Output the angle found above
    }

    private bool IsTargetInRange(Vector3 position1, Vector3 position2, float magnitudeCheck)
    {
        return (position1 - position2).LengthSquared() < magnitudeCheck;
    }

    public ShipController MyShip()
    {
        return shipController;
    }

    public async void Destroy()
    {
        if (amIDead)
            return;
        amIDead = true;

        // Campaign Tracking
        if (CampaignManager.Instance != null && shipSpecification != null)
        {
            CampaignManager.Instance.AddKill(shipSpecification.name);
        }

        PackedScene explosion = GD.Load<PackedScene>("res://explosion_big_1.tscn");

        Node3D e = explosion.Instantiate() as Node3D;
        e.Position = Position;
        Array<Node> children = e.GetChildren();
        foreach (var child in children)
        {
            ((GpuParticles3D)child).Emitting = true;
        }

        GetTree().Root.AddChild(e);

        await ToSignal(GetTree().CreateTimer(5.0f), SceneTreeTimer.SignalName.Timeout);

        GpuParticles3D eng1 = GetNodeOrNull<GpuParticles3D>("Engine_1/GPUParticles3D");
        GpuParticles3D eng2 = GetNodeOrNull<GpuParticles3D>("Engine_2/GPUParticles3D");
        if (eng1 != null)
            eng1.Emitting = false;
        if (eng2 != null)
            eng2.Emitting = false;
        e.QueueFree();
    }

    public void Destroy(List<Node> createdNodes) { }

    public void Damage(HardpointSpec currentHardpointSpec, Vector3 hit, double deltaTime)
    {
        // shield?.OnHit(hit); // Handled by ShipController
        shipController.Damage(currentHardpointSpec, hit, deltaTime);
        ProgressBar shieldBar =
            GetNode("SubViewport/Node2D/VBoxContainer/ShieldBar") as ProgressBar;
        ProgressBar armourBar =
            GetNode("SubViewport/Node2D/VBoxContainer/ArmourBar") as ProgressBar;

        float[] strengthPercents = shipController.Shield.GetStrengthPercents();
        shieldBar.Value = strengthPercents.Min();
        strengthPercents = shipController.Armor.GetStrengthPercents();
        armourBar.Value = strengthPercents.Min();
        signalBus.EmitSignal("NpcTargetChanged", GetStatus());
        UpdateShieldState();
    }

    public void Damage(float damage, Vector3 transformForward, Vector3 hitPosition = default)
    {
        // Shield hit visual handled by ShipController if quadrant active
        shipController.Damage(damage, transformForward, hitPosition);

        ProgressBar shieldBar =
            GetNode("SubViewport/Node2D/VBoxContainer/ShieldBar") as ProgressBar;
        ProgressBar armourBar =
            GetNode("SubViewport/Node2D/VBoxContainer/ArmourBar") as ProgressBar;

        float[] strengthPercents = shipController.Shield.GetStrengthPercents();
        shieldBar.Value = strengthPercents.Min();
        strengthPercents = shipController.Armor.GetStrengthPercents();
        armourBar.Value = strengthPercents.Min();

        signalBus.EmitSignal("NpcTargetChanged", GetStatus());
        UpdateShieldState();
    }

    private void UpdateShieldState()
    {
        if (shield == null || shipController.Shield == null)
            return;

        bool shieldUp = false;
        foreach (float s in shipController.Shield.strength)
        {
            if (s > 0)
            {
                shieldUp = true;
                break;
            }
        }
        shield.SetActive(shieldUp);
    }

    public bool IsDead()
    {
        return amIDead;
    }

    public void _OnBodyEntered(Node body)
    {
        GD.Print("NPC Collided with: ", body.Name);
    }



    public void SetAttitude(CombatDirector.Squadron.Attitude attitude)
    {
        this.attitude = attitude;
    }

    public void SetSquadron(CombatDirector.Squadron s)
    {
        squadron = s;
    }

    public RigidBody3D GetRigidBody3D()
    {
        return this;
    }

    public Dictionary GetStatus()
    {
        Dictionary status = shipController.ToUIDict();
        status["linear_velocity"] = LinearVelocity.Length();
        status["state"] = $"{shipAi.CurrentState}";
        status["tactics"] = $"{shipAi.AttackTactic} - {shipAi.CurrentTacticalPhase}";
        status["heat"] = fireSystem.heatTotal;
        status["position"] = Position.X + " " + Position.Z;
        status["debug_threat_name"] = DebugThreatName;
        status["debug_threat_time"] = DebugMinTimeImpact;
        
        // AI History
        if (shipAi.DecisionHistory != null && shipAi.DecisionHistory.Count > 0)
        {
            // Take last 5
            var history = shipAi.DecisionHistory.Skip(Math.Max(0, shipAi.DecisionHistory.Count - 5)).Reverse().Select(x => x.ToString()).ToArray();
            status["decision_history"] = history;
        }
        else
        {
            status["decision_history"] = new string[] { };
        }
        
        return status;
    }

    public void SetTargeted(bool b)
    {
        var reticule = GetNodeOrNull<MeshInstance3D>("Reticule3D");
        if (reticule != null)
        {
            reticule.Visible = b;
            
            if (b)
            {
                Color targetColor = new Color(0, 1, 0, 1); // Default Green
                if (Attitude == CombatDirector.Squadron.Attitude.Enemy)
                {
                    targetColor = new Color(1, 0, 0, 1); // Red
                }
                
                if (reticule.MaterialOverride is ShaderMaterial mat)
                {
                    mat.SetShaderParameter("albedo", targetColor);
                }
            }
        }
    }

    public void SetFormationIndex(int index)
    {
        if (shipAi != null)
        {
            shipAi.FormationIndex = index;
        }
    }

    public void ReceiveOrder(CombatDirector.SquadOrder order)
    {
        // 1. Personality Check (Loyalty)
        // Simple check: if Loyalty is very low, they might ignore "Dangerous" orders
        // For now, Wingmen obey most things.
        
        float roll = (float)rnd.NextDouble();
        if (roll > MyPersonality.Loyalty && order.Type != CombatDirector.OrderType.Evasion) // Execution check, except evasion
        {
             // Ignore/Complain
             GD.Print($"NPC {Name}: Ignored Order {order.Type} (Loyalty Check Failed)");
             return;
        }

        CurrentSquadOrder = order;
        GD.Print($"NPC {Name}: Executing Order {order.Type}");

        if (shipAi != null)
        {
            shipAi.CurrentOrder = order.Type;
        }
        else
        {
            // AI not ready yet, order stored in CurrentSquadOrder and will be applied in _Ready
            return;
        }

        // Pass to AI based on Order
        switch (order.Type)
        {
            case CombatDirector.OrderType.None:
                break; 
            case CombatDirector.OrderType.FormUp:
                // Find Leader (Player for now, or Squadron Leader)
                // If I'm in Player Squadron:
                if (squadron != null && squadron.playerSquadron)
                {
                    // Assuming Player is available via GameController
                    var player = gameController.player;
                    if (player != null)
                    {
                         // Join Formation on Player
                         // Special case: Player Leader.
                         // We need to tell AIController to form on Player RigidBody
                         shipAi.FormationLeaderBody = player; 
                         shipAi.ForceState(AIController.AIState.Formation);
                    }
                }
                break;
                
            case CombatDirector.OrderType.AttackTarget:
                if (order.Target != null)
                {
                    shipAi.TargetNode = order.Target.GetRigidBody3D(); // Cast safely
                    shipAi.CurrentState = AIController.AIState.AttackRun;
                }
                break;
                
            case CombatDirector.OrderType.FreeFire:
                shipAi.CurrentState = AIController.AIState.CombatFly; // Default wandering/combat
                break;
                
            case CombatDirector.OrderType.Evasion:
                shipAi.EnterEvasion(null); // Evade nothing specific, just break
                break;

             case CombatDirector.OrderType.HoldFire:
                shipAi.CurrentState = AIController.AIState.Idle; // Or follow but don't shoot
                break;
        }
    }
}

public partial class Ship : Node2D
{
    // Position and movement properties
    public Vector2 Velocity = new Vector2(0, 0);
    public new float Rotation;
    public float TargetRotation;
    public string ShipType = "generic";
    public string Id;

    // Ship status
    public int Health = 100;
    public bool IsAttacking = false;
    public Ship TargetShip = null;
    public string SquadRole = null; // 'leader', 'flanker', 'support'

    // Adjustable parameters
    public float MaxSpeed = 3.0f;
    public float Acceleration = 0.05f;
    public float RotationSpeed = 0.05f;
    public float Drag = 0.98f;

    // Ship dimensions for collision detection
    public float Radius = 15f; // collision radius

    // Tactical parameters
    public float PreferredDistance = 150f; // combat distance preference
    public float FlankingAngle = Mathf.Pi / 2; // 90 degrees for flanking
    public string CombatState = "idle"; // idle, approach, attack, retreat, flank
    public int StateTimer = 0;

    // Collision avoidance parameters
    public float AvoidanceRadius = 50f; // distance at which to start avoiding other ships
    public float AvoidanceWeight = 1.5f; // how strongly to avoid other ships

    // Vision range
    public float VisionRange = 400f;

    // Called when the node enters the scene tree for the first time
    public override void _Ready()
    {
        Id = GenerateRandomId(9);
        Rotation = TargetRotation;
    }

    // Constructor equivalent for Godot
    public void Initialize(float x, float y, float initialRotation = 0, string shipType = "generic")
    {
        Position = new Vector2(x, y);
        Rotation = initialRotation;
        TargetRotation = initialRotation;
        ShipType = shipType;
        Id = GenerateRandomId(9);
    }

    private string GenerateRandomId(int length)
    {
        var random = new Random();
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return new string(
            Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray()
        );
    }

    // Basic methods
    public void SetTarget(float targetX, float targetY)
    {
        float dx = targetX - Position.X;
        float dy = targetY - Position.Y;
        TargetRotation = Mathf.Atan2(dy, dx);
    }

    public override void _Process(double delta)
    {
        // This would be called instead of update in the game loop
    }

    public void Update(List<Ship> allShips)
    {
        // Apply collision avoidance before regular movement updates
        if (allShips != null && ShipType != "player")
        {
            AvoidCollisions(allShips);
        }

        // Update state timer
        if (StateTimer > 0)
        {
            StateTimer--;
        }

        // Smoothly rotate toward target rotation
        float rotationDiff = TargetRotation - Rotation;

        // Normalize the rotation difference
        while (rotationDiff > Mathf.Pi)
            rotationDiff -= Mathf.Pi * 2;
        while (rotationDiff < -Mathf.Pi)
            rotationDiff += Mathf.Pi * 2;

        // Apply rotation with smooth turning
        if (Mathf.Abs(rotationDiff) > 0.01f)
        {
            Rotation +=
                Mathf.Sign(rotationDiff) * Mathf.Min(Mathf.Abs(rotationDiff), RotationSpeed);
        }

        // Apply thrust
        float thrustX = Mathf.Cos(Rotation) * Acceleration;
        float thrustY = Mathf.Sin(Rotation) * Acceleration;

        Velocity = new Vector2(Velocity.X + thrustX, Velocity.Y + thrustY);

        // Apply drag
        Velocity = new Vector2(Velocity.X * Drag, Velocity.Y * Drag);

        // Limit maximum speed
        float speed = Velocity.Length();
        if (speed > MaxSpeed)
        {
            Velocity = Velocity.Normalized() * MaxSpeed;
        }

        // Update position
        Position = new Vector2(Position.X + Velocity.X, Position.Y + Velocity.Y);

        // Update the node's rotation to match our internal rotation
        RotationDegrees = Mathf.RadToDeg(Rotation);
    }

    // COLLISION AVOIDANCE METHODS

    // Calculate distance between this ship and another
    public float DistanceTo(Ship otherShip)
    {
        float dx = otherShip.Position.X - Position.X;
        float dy = otherShip.Position.Y - Position.Y;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // Detect and avoid collisions with other ships
    public void AvoidCollisions(List<Ship> allShips)
    {
        Vector2 avoidanceForce = new Vector2(0, 0);
        bool needsAvoidance = false;

        // Check against all other ships
        foreach (Ship otherShip in allShips)
        {
            // Skip self and destroyed ships
            if (otherShip == this || otherShip.Health <= 0)
            {
                continue;
            }

            float distance = DistanceTo(otherShip);

            // If within avoidance radius, calculate avoidance force
            if (distance < AvoidanceRadius)
            {
                needsAvoidance = true;

                // Direction away from other ship
                float dx = Position.X - otherShip.Position.X;
                float dy = Position.Y - otherShip.Position.Y;

                // Normalize and scale by inverse of distance (closer = stronger avoidance)
                float length = Mathf.Max(0.01f, Mathf.Sqrt(dx * dx + dy * dy));
                float avoidanceStrength =
                    AvoidanceWeight * (AvoidanceRadius - distance) / AvoidanceRadius;

                avoidanceForce += new Vector2(
                    (dx / length) * avoidanceStrength,
                    (dy / length) * avoidanceStrength
                );
            }
        }

        // Apply avoidance force if needed
        if (needsAvoidance)
        {
            // Get desired avoidance heading
            float avoidanceAngle = Mathf.Atan2(avoidanceForce.Y, avoidanceForce.X);

            // Blend current target rotation with avoidance angle
            // Higher weight to avoidance for closer ships
            float avoidanceInfluence = Mathf.Min(
                1.0f,
                Mathf.Sqrt(
                    avoidanceForce.X * avoidanceForce.X + avoidanceForce.Y * avoidanceForce.Y
                )
            );

            // Calculate weighted average of target rotation and avoidance angle
            float blendedAngle = BlendAngles(TargetRotation, avoidanceAngle, avoidanceInfluence);

            // Set new target rotation
            TargetRotation = blendedAngle;

            // Add immediate velocity component away from obstacles for faster response
            Velocity += avoidanceForce * 0.1f;
        }
    }

    // Helper to blend between two angles with weighting
    public float BlendAngles(float angle1, float angle2, float weight)
    {
        // Ensure angles are in similar range to avoid problems when blending near -PI/PI boundary
        float sin1 = Mathf.Sin(angle1);
        float cos1 = Mathf.Cos(angle1);
        float sin2 = Mathf.Sin(angle2);
        float cos2 = Mathf.Cos(angle2);

        // Blend the sine and cosine components
        float blendedSin = sin1 * (1 - weight) + sin2 * weight;
        float blendedCos = cos1 * (1 - weight) + cos2 * weight;

        // Convert back to angle
        return Mathf.Atan2(blendedSin, blendedCos);
    }

    // Enhanced seek with collision avoidance
    public bool Seek(float targetX, float targetY, float arrivalRadius = 50f)
    {
        float dx = targetX - Position.X;
        float dy = targetY - Position.Y;
        float distanceToTarget = Mathf.Sqrt(dx * dx + dy * dy);

        if (distanceToTarget > arrivalRadius)
        {
            SetTarget(targetX, targetY);
        }
        else
        {
            // Start slowing down when near target
            Velocity *= 0.95f;
        }

        return distanceToTarget <= arrivalRadius;
    }

    // Prediction-based collision avoidance
    public bool PredictCollisions(List<Ship> allShips, float timeHorizon = 2.0f)
    {
        // For more advanced collision avoidance, predict future positions
        Vector2 futurePosition = new Vector2(
            Position.X + Velocity.X * timeHorizon,
            Position.Y + Velocity.Y * timeHorizon
        );

        // Check for potential future collisions
        bool collisionDetected = false;
        Vector2 avoidanceVector = new Vector2(0, 0);

        foreach (Ship otherShip in allShips)
        {
            if (otherShip == this || otherShip.Health <= 0)
                continue;

            // Predict other ship's future position
            Vector2 otherFuturePosition = new Vector2(
                otherShip.Position.X + otherShip.Velocity.X * timeHorizon,
                otherShip.Position.Y + otherShip.Velocity.Y * timeHorizon
            );

            // Calculate future distance
            float futureDx = futurePosition.X - otherFuturePosition.X;
            float futureDy = futurePosition.Y - otherFuturePosition.Y;
            float futureDistance = Mathf.Sqrt(futureDx * futureDx + futureDy * futureDy);

            // Check if future positions will be too close
            if (futureDistance < this.Radius + otherShip.Radius + 10)
            {
                collisionDetected = true;

                // Calculate avoidance vector (perpendicular to relative velocity)
                Vector2 relVelocity = new Vector2(
                    this.Velocity.X - otherShip.Velocity.X,
                    this.Velocity.Y - otherShip.Velocity.Y
                );

                // Create perpendicular vector (for sidestepping)
                Vector2 perpendicular = new Vector2(-relVelocity.Y, relVelocity.X);

                // Normalize perpendicular vector
                float perpLength = perpendicular.Length();
                if (perpLength > 0.01f)
                {
                    avoidanceVector += perpendicular / perpLength;
                }
            }
        }

        // Apply predictive avoidance if needed
        if (collisionDetected)
        {
            // Normalize avoidance vector
            float avoidLength = avoidanceVector.Length();
            if (avoidLength > 0.01f)
            {
                float avoidanceAngle = Mathf.Atan2(
                    avoidanceVector.Y / avoidLength,
                    avoidanceVector.X / avoidLength
                );

                // Blend with current target direction (80% avoidance, 20% original direction)
                TargetRotation = BlendAngles(TargetRotation, avoidanceAngle, 0.8f);

                // Apply strong sideways thrust to avoid imminent collision
                Velocity += avoidanceVector.Normalized() * 0.2f;
            }

            return true;
        }

        return false;
    }

    // Method stub for finding targets
    public (Ship target, float distance) FindTarget(List<Ship> ships, string targetType)
    {
        Ship closestTarget = null;
        float closestDistance = float.MaxValue;

        foreach (Ship ship in ships)
        {
            if (ship.ShipType == targetType && ship.Health > 0)
            {
                float distance = DistanceTo(ship);
                if (distance < closestDistance)
                {
                    closestTarget = ship;
                    closestDistance = distance;
                }
            }
        }

        return (closestTarget, closestDistance);
    }

    // Method stub for squad tactics
    public void UpdateSquadTactics(GameState gameState)
    {
        // Find a target if we don't have one or our target is destroyed
        if (TargetShip == null || TargetShip.Health <= 0)
        {
            var targetInfo = FindTarget(gameState.Ships, "player");
            TargetShip = targetInfo.target;

            // No valid targets found
            if (TargetShip == null)
            {
                CombatState = "idle";
                return;
            }
        }

        // Get all allied ships in our squad
        var mySquad = gameState
            .Ships.Where(ship =>
                ship.ShipType == this.ShipType && ship.Health > 0 && ship.Id != this.Id
            )
            .ToList();

        // First check for imminent collisions (high priority)
        bool needsEmergencyAvoidance = PredictCollisions(gameState.Ships);

        // If emergency avoidance is active, temporarily suspend other behaviors
        if (needsEmergencyAvoidance)
        {
            CombatState = "avoiding_collision";
            return;
        }

        // Resume normal tactics if no collision danger
        // Determine my index in the squad for formation positioning
        int myIndex = mySquad.FindIndex(ship => ship.Id == this.Id);

        // Choose tactics based on squad size and situation
        if (mySquad.Count >= 3)
        {
            // With 3+ ships, perform pincer attacks
            if (CombatState != "pincer" || StateTimer <= 0)
            {
                CombatState = "pincer";
                StateTimer = 120; // Stay in this state for 120 frames
            }

            PerformPincerAttack(TargetShip, mySquad, myIndex);
        }
        else if (mySquad.Count >= 1)
        {
            // With 2 ships, perform flanking
            if (CombatState != "flank" || StateTimer <= 0)
            {
                CombatState = "flank";
                StateTimer = 90; // Stay in this state for 90 frames

                // Assign roles - one ship flanks left, one flanks right
                if (myIndex == 0)
                {
                    SquadRole = "leftFlanker";
                }
                else
                {
                    SquadRole = "rightFlanker";
                }
            }

            PerformFlankingAttack(TargetShip, mySquad);
        }
        else
        {
            // Solo ship - use basic approach and maintain distance
            string situation = MaintainCombatDistance(TargetShip);
            CombatState = situation;
        }
    }

    // Method stubs for tactical behaviors
    public void PerformPincerAttack(Ship target, List<Ship> mySquad, int myIndex)
    {
        // Implementation would go here
    }

    public void PerformFlankingAttack(Ship target, List<Ship> mySquad)
    {
        // Implementation would go here
    }

    public string MaintainCombatDistance(Ship target)
    {
        // Implementation would go here
        return "approach";
    }
}

// Game state class
public class GameState
{
    public List<Ship> Ships { get; set; } = new List<Ship>();
    public SquadManager Squads { get; set; }
}

// Squad manager class stub
public class SquadManager
{
    public void UpdateSquads(GameState gameState)
    {
        // Implementation would go here
    }
}
