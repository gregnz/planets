using System;
using Godot;
using Planetsgodot.Scripts;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Environment;
using Planetsgodot.Scripts.Combat;

/// <summary>
/// Optimized asteroid field using MultiMeshInstance3D for GPU instanced rendering.
/// Supports thousands of asteroids with minimal draw calls.
/// </summary>
public partial class OptimizedAsteroidField : Node3D
{
    [Export] public int AsteroidCount = 1000;

    [Export] public float FieldRadius = 100f;

    [Export] public float OrbitSpeed = 0.5f;

    [Export] public float Gravitation = 0.45f;

    [Export] public Vector3 FieldCenter = Vector3.Zero;

    private AsteroidData[] _asteroids;
    private MultiMeshInstance3D _multiMeshInstance;
    private MultiMesh _multiMesh;
    private Mesh _asteroidMesh;

    // Physics pool for nearby asteroids
    private const int PHYSICS_POOL_SIZE = 2000;
    private const float PHYSICS_ACTIVATION_RANGE = 300f;
    private Asteroid[] _physicsPool;
    private int[] _poolToDataMapping; // Maps pool index to data index

    private GameController _gc;
    private Random _rng = new();

    public override void _Ready()
    {
        _gc = GetNodeOrNull<GameController>("/root/GameController");

        InitializeMultiMesh();
        InitializeAsteroidData();
        InitializePhysicsPool();

        GD.Print($"OptimizedAsteroidField: Initialized {AsteroidCount} asteroids with 1 draw call");
        GD.Print($"OptimizedAsteroidField Center: {FieldCenter}");
    }

    private void InitializeMultiMesh()
    {
        // Create the asteroid mesh (simple sphere)
        var sphereMesh = new SphereMesh();
        sphereMesh.Radius = 1.0f;
        sphereMesh.Height = 2.0f;
        sphereMesh.RadialSegments = 8; // Low poly for performance
        sphereMesh.Rings = 4;
        _asteroidMesh = sphereMesh;

        // Create MultiMesh
        _multiMesh = new MultiMesh();
        _multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        _multiMesh.Mesh = _asteroidMesh;
        // Reserve extra instance slots for fragments (buffer)
        _multiMesh.InstanceCount = AsteroidCount + PHYSICS_POOL_SIZE;

        // Create MultiMeshInstance3D
        _multiMeshInstance = new MultiMeshInstance3D();
        _multiMeshInstance.Multimesh = _multiMesh;

        // Apply a basic material
        var material = new StandardMaterial3D();
        material.AlbedoColor = new Color(0.5f, 0.4f, 0.3f); // Brownish asteroid color
        material.Roughness = 0.9f;
        _multiMeshInstance.MaterialOverride = material;

        AddChild(_multiMeshInstance);
    }

    private void InitializeAsteroidData()
    {
        // Allocate buffer: Initial Count + Space for Fragments
        _asteroids = new AsteroidData[AsteroidCount + PHYSICS_POOL_SIZE];

        for (int i = 0; i < AsteroidCount; i++)
        {
            // Distribute asteroids in a disk pattern (Uniform density)
            float angle = (float)_rng.NextDouble() * Mathf.Pi * 2f;
            float distPercent = Mathf.Sqrt((float)_rng.NextDouble()); // Uniform point in circle
            float orbitRadius = FieldRadius * distPercent;

            // Initial position (Y=0 for top-down game)
            Vector3 pos =
                FieldCenter
                + new Vector3(
                    Mathf.Cos(angle) * orbitRadius,
                    0, // Keep on same plane as ships/planets
                    Mathf.Sin(angle) * orbitRadius
                );

            float scale = 0.5f + (float)_rng.NextDouble() * 2f; // 0.5 to 2.5 scale

            _asteroids[i] = AsteroidData.Create(pos, orbitRadius, angle, scale);

            // Set initial transform in MultiMesh
            UpdateMultiMeshTransform(i);
        }

        // Initialize Buffer Slots as Destroyed
        for (int i = AsteroidCount; i < _asteroids.Length; i++)
        {
            ref var a = ref _asteroids[i];
            a.IsDestroyed = true;
            a.ActiveBodyIndex = -1;
            // Hide explicitely
            _multiMesh.SetInstanceTransform(i, Transform3D.Identity.Scaled(Vector3.Zero));
        }
    }

    private void InitializePhysicsPool()
    {
        _physicsPool = new Asteroid[PHYSICS_POOL_SIZE];
        _poolToDataMapping = new int[PHYSICS_POOL_SIZE];

        var asteroidScene = GD.Load<PackedScene>("res://asteroid.tscn");
        if (asteroidScene == null)
        {
            GD.PrintErr("Failed to load asteroid.tscn for physics pool");
            return;
        }

        for (int i = 0; i < PHYSICS_POOL_SIZE; i++)
        {
            var asteroid = asteroidScene.Instantiate<Asteroid>();
            asteroid.Name = $"PooledAsteroid_{i}";
            asteroid.Visible = false;
            asteroid.ProcessMode = ProcessModeEnum.Disabled;
            asteroid.SetPhysicsProcess(false);

            // Don't add to SectorRoot - keep in this node
            AddChild(asteroid);

            _physicsPool[i] = asteroid;
            _poolToDataMapping[i] = -1; // Not mapped
        }

        GD.Print($"Physics pool initialized with {PHYSICS_POOL_SIZE} bodies");
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateOrbits((float)delta);
        UpdateVisuals();

        // Enforce 2D Planar Physics
        // (Handled by AxisLocks on individual bodies now)

        if (_gc?.player != null)
        {
            UpdatePhysicsPool(_gc.player.GlobalPosition);
        }
    }

    private void UpdateOrbits(float delta)
    {
        for (int i = 0; i < _asteroids.Length; i++)
        {
            ref var a = ref _asteroids[i];
            if (a.IsDestroyed)
                continue;

            // Skip if physics body is active (it handles its own position)
            if (a.ActiveBodyIndex >= 0)
                continue;

            // Update orbital angle
            a.OrbitAngle += OrbitSpeed * delta / a.OrbitRadius; // Outer = slower

            // Update position
            // Update position
            float newX = FieldCenter.X + Mathf.Cos(a.OrbitAngle) * a.OrbitRadius;
            float newZ = FieldCenter.Z + Mathf.Sin(a.OrbitAngle) * a.OrbitRadius;

            a.Position = new Vector3(newX, a.Position.Y, newZ);
        }
    }

    private void UpdateVisuals()
    {
        for (int i = 0; i < _asteroids.Length; i++)
        {
            UpdateMultiMeshTransform(i);
        }
    }

    private void UpdateMultiMeshTransform(int index)
    {
        ref var a = ref _asteroids[index];

        if (a.IsDestroyed)
        {
            // Hide by scaling to zero
            _multiMesh.SetInstanceTransform(index, Transform3D.Identity.Scaled(Vector3.Zero));
            return;
        }

        // If physics body is active, sync position FROM the body
        if (a.ActiveBodyIndex >= 0 && a.ActiveBodyIndex < PHYSICS_POOL_SIZE)
        {
            var body = _physicsPool[a.ActiveBodyIndex];
            if (body != null && GodotObject.IsInstanceValid(body))
            {
                a.Position = body.GlobalPosition;
            }
        }

        var transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * a.Scale), a.Position);
        _multiMesh.SetInstanceTransform(index, transform);
    }

    private void UpdatePhysicsPool(Vector3 playerPos)
    {
        // Deactivate far asteroids
        for (int poolIdx = 0; poolIdx < PHYSICS_POOL_SIZE; poolIdx++)
        {
            int dataIdx = _poolToDataMapping[poolIdx];
            if (dataIdx < 0)
                continue;

            float dist = playerPos.DistanceTo(_asteroids[dataIdx].Position);
            if (dist > PHYSICS_ACTIVATION_RANGE * 1.5f) // Hysteresis
            {
                DeactivatePhysicsBody(poolIdx);
            }
        }

        // Activate nearby asteroids
        for (int i = 0; i < _asteroids.Length; i++)
        {
            ref var a = ref _asteroids[i];
            if (a.IsDestroyed || a.ActiveBodyIndex >= 0)
                continue;

            float dist = playerPos.DistanceTo(a.Position);
            if (dist < PHYSICS_ACTIVATION_RANGE)
            {
                ActivatePhysicsBody(i);
            }
        }
    }

    private int FindFreePoolSlot()
    {
        for (int i = 0; i < PHYSICS_POOL_SIZE; i++)
        {
            if (_poolToDataMapping[i] < 0)
                return i;
        }

        return -1;
    }

    private void ActivatePhysicsBody(int dataIndex)
    {
        int poolIdx = FindFreePoolSlot();
        if (poolIdx < 0)
            return; // Pool exhausted

        ref var a = ref _asteroids[dataIndex];
        var body = _physicsPool[poolIdx];

        // Configure body
        body.GlobalPosition = a.Position;
        body.Scale = Vector3.One; // Reset body scale to 1
        body.Mass = 1000f * a.Scale;
        body.Visible = true; // Debug: Show physics body

        // Hide original mesh if present
        var originalMesh = body.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (originalMesh != null)
            originalMesh.Visible = false;

        // Scale Collision Shape
        var collisionShape = body.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (collisionShape != null)
        {
            collisionShape.Disabled = false;
            collisionShape.Scale = Vector3.One * a.Scale * 2.0f;
        }

        // Add/Update Debug Sphere
        var debugSphere = body.GetNodeOrNull<MeshInstance3D>("DebugSphere");
        if (debugSphere == null)
        {
            debugSphere = new MeshInstance3D();
            debugSphere.Name = "DebugSphere";

            var sphereMesh = new SphereMesh();
            sphereMesh.Radius = 0.5f; // Base radius 0.5 matches CollisionShape base size
            sphereMesh.Height = 1.0f;
            debugSphere.Mesh = sphereMesh;

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(1, 0, 0, 0.3f); // Red transparent
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            debugSphere.MaterialOverride = mat;

            body.AddChild(debugSphere);
        }

        debugSphere.Visible = true;
        debugSphere.Scale = Vector3.One * a.Scale * 2.0f;

        body.ProcessMode = ProcessModeEnum.Inherit;
        body.SetPhysicsProcess(true);

        // Enable collision - layer 32 (asteroid), mask 63 (all weapons/ships)
        body.CollisionLayer = 32; // Bit 6 - Asteroids
        body.CollisionMask = 63; // Bits 1-6 - Collide with everything

        // Link
        a.ActiveBodyIndex = poolIdx;
        _poolToDataMapping[poolIdx] = dataIndex;


        // Set orbital velocity
        Vector3 radialDir = (a.Position - FieldCenter).Normalized();
        Vector3 tangentDir = new Vector3(-radialDir.Z, 0, radialDir.X);
        body.LinearVelocity = tangentDir * OrbitSpeed * 10f;

        // Enforce 2D Plane via Physics Engine
        body.AxisLockLinearY = true;
        body.AxisLockAngularX = true;
        body.AxisLockAngularZ = true;

        // Assign Reference to Asteroid Script
        if (body is Asteroid astScript)
        {
            astScript.OptimizedField = this;
            astScript.OptimizedPoolIndex = poolIdx;
            astScript.Mass = 1000f * a.Scale;
            astScript.radius = 40.0f * a.Scale;
        }
    }

    public void SpawnFragment(Vector3 position, float scale, Vector3 velocity)
    {
        // Find a free slot in the data array (reusing destroyed asteroids)
        int slotIndex = -1;
        for (int i = 0; i < _asteroids.Length; i++)
        {
            if (_asteroids[i].IsDestroyed)
            {
                slotIndex = i;
                break;
            }
        }

        if (slotIndex == -1)
        {
            // Pool is full, cannot spawn fragment
            return;
        }

        // Calculate Orbit Parameters for the new position
        Vector3 relPos = position - FieldCenter;
        float orbitRadius = relPos.Length();
        float orbitAngle = Mathf.Atan2(relPos.Z, relPos.X);

        // Update Data
        _asteroids[slotIndex] = AsteroidData.Create(position, orbitRadius, orbitAngle, scale);

        // Activate Physics immediately
        ActivatePhysicsBody(slotIndex);

        // Apply specific velocity (override orbital velocity set by Activate)
        int bodyIdx = _asteroids[slotIndex].ActiveBodyIndex;
        if (bodyIdx >= 0)
        {
            var body = _physicsPool[bodyIdx];
            body.LinearVelocity = velocity;
        }
    }

    public void ReportAsteroidDestruction(Asteroid asteroidScript)
    {
        int poolIdx = asteroidScript.OptimizedPoolIndex;
        if (poolIdx < 0 || poolIdx >= PHYSICS_POOL_SIZE) return;

        int dataIdx = _poolToDataMapping[poolIdx];
        if (dataIdx >= 0 && dataIdx < _asteroids.Length)
        {
            DestroyAsteroid(dataIdx);
            // DestroyAsteroid calls DeactivatePhysicsBody, which resets the pool item.
        }
    }

    private void DeactivatePhysicsBody(int poolIndex)
    {
        int dataIdx = _poolToDataMapping[poolIndex];
        if (dataIdx < 0)
            return;

        var body = _physicsPool[poolIndex];

        // Sync final position back to data
        _asteroids[dataIdx].Position = body.GlobalPosition;
        _asteroids[dataIdx].OrbitAngle = Mathf.Atan2(
            body.GlobalPosition.Z - FieldCenter.Z,
            body.GlobalPosition.X - FieldCenter.X
        );
        _asteroids[dataIdx].ActiveBodyIndex = -1;

        if (body is ITarget target)
        {
            _gc?.DeregisterTarget(target);
        }

        // Disable body and collision
        body.ProcessMode = ProcessModeEnum.Disabled;
        body.Visible = false; // Hide entire node
        body.SetPhysicsProcess(false);
        body.LinearVelocity = Vector3.Zero;
        body.CollisionLayer = 0;
        body.CollisionMask = 0;

        var collisionShape = body.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (collisionShape != null)
        {
            collisionShape.Disabled = true;
            collisionShape.Scale = Vector3.One; // Reset scale
        }

        var debugSphere = body.GetNodeOrNull<Node3D>("DebugSphere");
        if (debugSphere != null)
        {
            debugSphere.Visible = false;
        }

        _poolToDataMapping[poolIndex] = -1;
    }

    /// <summary>
    /// Damage an asteroid at the given data index.
    /// </summary>
    /// <summary>
    /// Damage an asteroid given its physics pool index.
    /// </summary>
    public void DamageAsteroid(int poolIndex, float damage, Vector3 hitDirection)
    {
        if (poolIndex < 0 || poolIndex >= PHYSICS_POOL_SIZE)
            return;

        int dataIndex = _poolToDataMapping[poolIndex];
        if (dataIndex < 0 || dataIndex >= _asteroids.Length)
            return;

        ref var a = ref _asteroids[dataIndex];
        a.Integrity -= damage;

        if (a.Integrity <= 0)
        {
            SplitAsteroid(dataIndex, hitDirection);
        }
    }

    private void SplitAsteroid(int parentIndex, Vector3 hitDirection)
    {
        ref var parent = ref _asteroids[parentIndex];

        // Mass check (50.0f min mass approx corresponds to Scale 0.05 given Mass=1000*Scale)
        if (parent.Scale < 0.05f)
        {
            DestroyAsteroid(parentIndex);
            return;
        }

        // Parent Velocity
        Vector3 parentVelocity = Vector3.Zero;
        if (parent.ActiveBodyIndex >= 0)
        {
            parentVelocity = _physicsPool[parent.ActiveBodyIndex].LinearVelocity;
        }
        else
        {
            // Approximate if not physics active
            Vector3 radialDir = (parent.Position - FieldCenter).Normalized();
            Vector3 tangentDir = new Vector3(-radialDir.Z, 0, radialDir.X);
            parentVelocity = tangentDir * OrbitSpeed * 10f;
        }

        // Capture data before destroying
        Vector3 pPos = parent.Position;
        float pScale = parent.Scale;

        // Destroy parent immediately to recycle its slot
        DestroyAsteroid(parentIndex);

        int fragments = _rng.Next(2, 5); // 2 to 4
        float newScale = pScale * 0.6f;

        for (int i = 0; i < fragments; i++)
        {
            // Offset calculation (approx radius 40 * Scale units)
            float radiusApprox = 40.0f * pScale;
            Vector3 offset =
                new Vector3((float)_rng.NextDouble() - 0.5f, 0, (float)_rng.NextDouble() - 0.5f).Normalized() *
                radiusApprox * 0.005f;
            Vector3 spawnPos = pPos + offset;
            spawnPos.Y = 0;

            Vector3 explosionDir = (offset.Normalized() + hitDirection * 0.5f).Normalized();
            float deltaV = 5.0f + (float)_rng.NextDouble() * 10.0f; // 5-15

            SpawnFragment(spawnPos, newScale, parentVelocity + explosionDir * deltaV);
            GD.Print($"Fragment {i} spawned at {spawnPos} Asteroid: {pPos}");
        }
    }

    /// <summary>
    /// Destroy an asteroid, removing it from rendering and physics.
    /// </summary>
    public void DestroyAsteroid(int dataIndex)
    {
        ref var a = ref _asteroids[dataIndex];
        a.IsDestroyed = true;

        // Deactivate physics if active
        if (a.ActiveBodyIndex >= 0)
        {
            DeactivatePhysicsBody(a.ActiveBodyIndex);
        }

        // MultiMesh transform will be set to zero scale on next update
        GD.Print($"Asteroid {dataIndex} destroyed");
    }
}
