using Godot;
using System;

public partial class GaussVisualBullet : Node3D
{
    // Tuning
    private Color _coreColor = new Color(0.2f, 0.8f, 1.0f); // Cyan/Electric Blue
    private float _coreSize = 0.08f; // Thicker slug
    private float _length = 6.0f; // Very long slug/rail
    private float _spiralRadius = 0.3f;
    private float _spiralSpeed = 20.0f; // Rotations per second

    private MeshInstance3D _coreMesh;
    private Node3D _spiralRoot;
    private Planetsgodot.Scripts.Combat.Bullet _parentBullet;

    public override void _Ready()
    {
        _parentBullet = GetParent() as Planetsgodot.Scripts.Combat.Bullet;

        // 1. Glowing Core (The physical slug/rail trail)
        CreateCore();

        // 2. Spiral Trails
        CreateSpirals();

        // 3. Light (Illumination)
        CreateLight();
    }

    public override void _Process(double delta)
    {
        if (_spiralRoot != null)
        {
            _spiralRoot.RotateZ((float)delta * _spiralSpeed);
        }

        UpdateCoreTrail();
    }

    private void UpdateCoreTrail()
    {
        if (_coreMesh == null || _parentBullet == null) return;

        Vector3 start = _parentBullet.startPos;
        Vector3 end = GlobalPosition;
        Vector3 diff = end - start;
        float dist = diff.Length();

        if (dist < 0.1f) return;

        // Position at midpoint
        _coreMesh.GlobalPosition = (start + end) / 2.0f;

        // Align with path
        // LookAt aligns -Z to target. We want the Y-axis cylinder to align with the path.
        // We look at 'end' from 'midpoint'.
        _coreMesh.LookAt(end, Vector3.Up);
        // Rotate 90 degrees on X to align Y-axis mesh with Z-axis look vector
        _coreMesh.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(90));

        // Stretch
        bool updated = false;
        if (_coreMesh.Mesh is CylinderMesh cylinder)
        {
            cylinder.Height = dist;
            updated = true;
        }
        else if (_coreMesh.Mesh is CapsuleMesh capsule)
        {
            capsule.Height = dist;
            updated = true;
        }
    }

    private void CreateCore()
    {
        _coreMesh = new MeshInstance3D();
        var mesh = new CylinderMesh();
        mesh.TopRadius = _coreSize;
        mesh.BottomRadius = _coreSize;
        mesh.Height = 1.0f; // Dynamic
        mesh.RadialSegments = 16;
        mesh.Rings = 1;
        _coreMesh.Mesh = mesh;

        // Glowing Material with Gradient
        var mat = new StandardMaterial3D();
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.BlendMode = BaseMaterial3D.BlendModeEnum.Add; // Additive blending for glow
        mat.AlbedoColor = _coreColor;

        // Create Gradient Texture for fading (Bottom/Start = Transparent, Top/End = Semi-Transparent)
        var grad = new Gradient();
        grad.SetColor(0, new Color(_coreColor.R, _coreColor.G, _coreColor.B, 0.0f)); // Start (Transparent)
        grad.SetColor(1, new Color(_coreColor.R, _coreColor.G, _coreColor.B, 0.4f)); // End (Semi-Transparent)

        var gradTex = new GradientTexture2D();
        gradTex.Gradient = grad;
        gradTex.Fill = GradientTexture2D.FillEnum.Linear;
        gradTex.FillFrom = new Vector2(0, 0); // Top (Gun end) -> Transparent
        gradTex.FillTo = new Vector2(0, 1); // Bottom (Bullet end) -> Opaque

        mat.AlbedoTexture = gradTex;

        mat.EmissionEnabled = true;
        mat.Emission = _coreColor * 4.0f;
        mat.EmissionEnergyMultiplier = 4.0f;
        // Use the same gradient for emission so the tail doesn't glow while transparent
        mat.EmissionTexture = gradTex;

        _coreMesh.MaterialOverride = mat;
        // Don't inherit transform (we set Global manually)
        _coreMesh.TopLevel = true;

        AddChild(_coreMesh);

        // Trail particles (spiral) handle the head effect
    }

    private void CreateSpirals()
    {
        _spiralRoot = new Node3D();
        AddChild(_spiralRoot);

        // Create two opposite spinners
        CreateSpiralPart(new Vector3(_spiralRadius, 0, 0));
        CreateSpiralPart(new Vector3(-_spiralRadius, 0, 0));
    }

    private void CreateSpiralPart(Vector3 offset)
    {
        var node = new Node3D();
        node.Position = offset;
        _spiralRoot.AddChild(node);

        var trail = CreateTrailNode();
        trail.Amount = 50;
        trail.Lifetime = 0.5f;
        trail.ScaleAmountMin = 0.05f;
        trail.ScaleAmountMax = 0.01f;

        node.AddChild(trail);
    }

    private CpuParticles3D CreateTrailNode()
    {
        var particles = new CpuParticles3D();
        particles.Amount = 20;
        particles.Lifetime = 0.5f;
        particles.Explosiveness = 0.0f;
        particles.FixedFps = 60;

        particles.Direction = Vector3.Back;
        particles.Spread = 0;
        particles.Gravity = Vector3.Zero;
        particles.InitialVelocityMin = 0;
        particles.InitialVelocityMax = 0;

        var color = _coreColor;
        color.A = 0.5f;
        particles.Color = color;

        var drawPass = new QuadMesh();
        drawPass.Size = new Vector2(0.1f, 0.1f);
        var drawMat = new StandardMaterial3D();
        drawMat.VertexColorUseAsAlbedo = true;
        drawMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        drawMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        drawMat.EmissionEnabled = true;
        drawMat.Emission = _coreColor;
        drawMat.EmissionEnergyMultiplier = 2.0f;
        drawPass.Material = drawMat;

        particles.Mesh = drawPass;
        particles.LocalCoords = false; // World space trails

        return particles;
    }

    private void CreateLight()
    {
        var light = new OmniLight3D();
        light.LightColor = _coreColor;
        light.LightEnergy = 2.0f;
        light.OmniRange = 15.0f;
        AddChild(light);
    }
}
