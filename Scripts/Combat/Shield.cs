using System;
using Godot;

public partial class Shield : Node3D
{
    private MeshInstance3D _meshInstance;
    private ShaderMaterial _material;
    private float _currentImpactStrength = 0f;
    private float _decayRate = 5.0f; // Fast decay for impact flash

    // Per-quadrant vaporization
    // 0=Front, 1=Back, 2=Left, 3=Right
    private float[] _vaporizeTimers = new float[] { 0f, 0f, 0f, 0f };
    private bool[] _isVaporising = new bool[] { false, false, false, false };
    private const float VAPORIZE_DURATION = 1.0f;
    
    // Quadrant strength tracking (0-100 percentage)
    private float[] _quadrantStrengths = new float[] { 100f, 100f, 100f, 100f };

    private Color _baseColor = new Color(0.0f, 0.4f, 1.0f, 0.05f);
    
    /// <summary>
    /// Check if the shield quadrant at a given global hit position is active (has health).
    /// Used by weapons to determine if a shield hit should be processed.
    /// </summary>
    public bool IsQuadrantActive(Vector3 globalHitPoint)
    {
        Vector3 localHit = ToLocal(globalHitPoint);
        int quadrant = GetQuadrantFromLocalPos(localHit);
        float strength = _quadrantStrengths[quadrant];
        
        // Debug logging to diagnose why depleted shields might still be hit
        GD.Print($"Shield Hit Check: Global={globalHitPoint} Local={localHit} Quadrant={quadrant} Strength={strength}");
        // Temporary debug
        if (strength <= 0)
        {
             GD.Print($"Shield Hit Check: QUADRANT DEPLETED but checked? Global={globalHitPoint} Local={localHit} Quadrant={quadrant} Strength={strength}");
        }
        
        return strength > 0;
    }
    
    /// <summary>
    /// Determines which quadrant (0=Front, 1=Back, 2=Left, 3=Right) a local position belongs to.
    /// Uses same logic as shader for consistency.
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

    public override void _Ready()
    {
        // Create Mesh
        _meshInstance = new MeshInstance3D();
        var mesh = new SphereMesh();
        mesh.Radius = 1.0f;
        mesh.Height = 2.0f;
        _meshInstance.Mesh = mesh;
        AddChild(_meshInstance);

        // Load Shader
        var shader = GD.Load<Shader>("res://shield.gdshader");
        if (shader == null)
        {
            GD.PrintErr("Shield shader not found at res://shield.gdshader");
            return;
        }

        _material = new ShaderMaterial();
        _material.Shader = shader;

        // Defaults
        // Faint blue, low opacity
        _material.SetShaderParameter("shield_color", _baseColor);
        _material.SetShaderParameter("rim_power", 3.0f);
        _material.SetShaderParameter("rim_intensity", 2.0f);
        _material.SetShaderParameter("quadrant_intensities", new Color(1, 1, 1, 1));

        // Impact defaults
        _material.SetShaderParameter("impact_strength", 0.0f);
        _material.SetShaderParameter("impact_radius", 0.3f); // Reduced from 2.0 to be relative to unit sphere

        _meshInstance.MaterialOverride = _material;

        // Default scale
        // Adjusted based on feedback (Ship is approx 2 units long)
        Scale = new Vector3(1.5f, 1.5f, 1.5f);

        SetupPhysics();
    }

    private void SetupPhysics()
    {
        var rb = GetParentRigidBody();
        if (rb != null)
        {
            _area = new Area3D();
            _area.Name = "ShieldArea";
            // Set collision layer to 2 (Matches Ships) so weapons detecting ships (Layer 2) detect shields too.
            _area.CollisionLayer = 2;
            // Shields don't need to scan interactions, so mask 0?
            // Or maybe they need to detect things? PlayerController CollisionMask is 60.
            // We just want to BE detected.

            rb.AddChild(_area);

            _sphereShape = new SphereShape3D();
            _choiceShape = new CollisionShape3D();
            _choiceShape.Shape = _sphereShape;
            _choiceShape.Name = "ShieldCollider";
            _area.AddChild(_choiceShape);

            // Initial sizing - Radius of 1 scaled by 2.5 = 2.5
            _sphereShape.Radius = Scale.X;
            _collisionShape = _choiceShape;
        }
    }

    public override void _ExitTree()
    {
        if (_area != null && GodotObject.IsInstanceValid(_area))
        {
            _area.QueueFree();
        }
    }

    private CollisionShape3D _choiceShape;
    private Area3D _area;
    private CollisionShape3D _collisionShape; // Kept as a class member for SetActive
    private SphereShape3D _sphereShape;

    public void SetSize(float radius)
    {
        Scale = new Vector3(radius, radius, radius);
        if (_sphereShape != null)
            _sphereShape.Radius = radius;
    }

    public override void _Process(double delta)
    {
        bool anyVaporizing = false;
        Color factors = new Color(0, 0, 0, 0);

        // Update vaporization timers
        for (int i = 0; i < 4; i++)
        {
            if (_isVaporising[i])
            {
                _vaporizeTimers[i] += (float)delta;

                // 0.0 to 1.0 progress
                float progress = Mathf.Clamp(_vaporizeTimers[i] / VAPORIZE_DURATION, 0f, 1.0f);

                // Map to vec4 component (RGBA = xyzw)
                if (i == 0)
                    factors.R = progress;
                if (i == 1)
                    factors.G = progress;
                if (i == 2)
                    factors.B = progress;
                if (i == 3)
                    factors.A = progress;

                anyVaporizing = true;
            }
            else
            {
                _vaporizeTimers[i] = 0f;
            }
        }

        // Always update if any are vaporizing to ensure shader state persists
        if (anyVaporizing && _material != null)
        {
            _material.SetShaderParameter("vaporize_factors", factors);
        }

        if (_currentImpactStrength > 0)
        {
            _currentImpactStrength -= _decayRate * (float)delta;
            if (_currentImpactStrength < 0)
                _currentImpactStrength = 0;
            _material?.SetShaderParameter("impact_strength", _currentImpactStrength);
        }
    }

    // Deprecated global SetActive
    public void SetActive(bool active)
    {
        if (active)
        {
            if (!Visible)
                Visible = true;
            if (_collisionShape != null)
                _collisionShape.Disabled = false;
        }
        else
        {
            // Only hide immediately if NOT vaporizing.
            // If vaporizing, the Process loop will hide us when done (or alpha goes to 0).
            bool anyVap = false;
            foreach (bool v in _isVaporising)
                if (v)
                    anyVap = true;

            if (!anyVap)
            {
                Visible = false;
            }

            // Collision ALWAYS OFF immediately if inactive
            if (_collisionShape != null)
                _collisionShape.Disabled = true;
        }
    }

    private RigidBody3D GetParentRigidBody()
    {
        Node n = GetParent();
        while (n != null)
        {
            if (n is RigidBody3D rb)
                return rb;
            n = n.GetParent();
        }
        return null;
    }

    public void OnHit(Vector3 globalHitPos)
    {
        if (_material == null)
            return;

        // Convert global hit position to local space
        Vector3 localPos = ToLocal(globalHitPos);

        // Update Shader
        _material.SetShaderParameter("impact_pos", localPos);

        // Bright flash
        _currentImpactStrength = 5.0f;
        _material.SetShaderParameter("impact_strength", _currentImpactStrength);
    }

    public void UpdateShieldStrengths(float front, float back, float left, float right)
    {
        // Store for IsQuadrantActive checks
        _quadrantStrengths[0] = front;
        _quadrantStrengths[1] = back;
        _quadrantStrengths[2] = left;
        _quadrantStrengths[3] = right;
        
        if (_material != null)
        {
            // Normalize healths to 1.0 (assuming input is percentage 0-100)
            var quadrants = new Color(front / 100f, back / 100f, left / 100f, right / 100f);
            _material.SetShaderParameter("quadrant_intensities", quadrants);

            // Check for vaporization triggers
            float[] newStrengths = { front, back, left, right };
            for (int i = 0; i < 4; i++)
            {
                // Trigger if low health (<= 1%)
                if (newStrengths[i] <= 1.0f)
                {
                    // If we are already vaporizing, keep going.
                    // If not, start it.
                    if (!_isVaporising[i])
                    {
                        _isVaporising[i] = true;
                        _vaporizeTimers[i] = 0f;
                    }
                }
                // Stop ONLY if significantly restored (> 10%)
                else if (newStrengths[i] > 10.0f)
                {
                    if (_isVaporising[i])
                    {
                        _isVaporising[i] = false;
                        _vaporizeTimers[i] = 0f;
                    }
                }
                // If between 1% and 10%, keep current state (Hysteresis)
            }
        }
    }
}
