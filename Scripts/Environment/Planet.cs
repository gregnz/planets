using Godot;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Controllers;

namespace Planetsgodot.Scripts.Environment;

public partial class Planet : StaticBody3D, ITarget
{
    [Export] public float Radius = 50.0f;
    [Export] public string PlanetName = "Unknown Planet";

    // === PROCEDURAL PLANET SETTINGS ===
    [ExportGroup("Planet Generation")]
    [Export] public int Seed { get; set; } = 0;
    [Export] public Color PlanetColor { get; set; } = new Color(0.2f, 0.6f, 0.1f); // Land
    [Export] public Color OceanColor { get; set; } = new Color(0.0f, 0.1f, 0.3f);
    [Export] public Color AtmosphereColor { get; set; } = new Color(0.4f, 0.6f, 1.0f);
    [Export(PropertyHint.Range, "0,1")] public float CloudDensity { get; set; } = 0.4f;
    [Export(PropertyHint.Range, "0,1")] public float SeaLevel { get; set; } = 0.5f;

    [ExportGroup("Rings")]
    [Export] public bool HasRings { get; set; } = false;
    [Export] public Color RingColor { get; set; } = new Color(0.7f, 0.6f, 0.5f, 0.8f);
    [Export] public float RingRadius { get; set; } = 70.0f; // Outer radius roughly
    [Export] public float RingWidth { get; set; } = 15.0f;
    [Export] public float RingInclination { get; set; } = 25.0f; // Degrees

    // Selection visual
    private MeshInstance3D _selectionRing;
    private MeshInstance3D _planetMesh;
    private MeshInstance3D _ringMesh;
    
    public CombatDirector.Squadron.Attitude Attitude => CombatDirector.Squadron.Attitude.Neutral;
    
    public override void _Ready()
    {
        AddToGroup("Planet");
        
        if (Radius <= 0) Radius = 5.0f; // Default safety

        // Setup Procedural Shader
        _planetMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (_planetMesh != null)
        {
            // Create a new unique material so planets don't share the same instance
            var shader = GD.Load<Shader>("res://Shaders/planet.gdshader");
            var mat = new ShaderMaterial();
            mat.Shader = shader;
            
            // Apply Uniforms
            if (Seed == 0) Seed = (int)GD.Randi() % 1000;
            mat.SetShaderParameter("seed", (float)Seed);
            
            mat.SetShaderParameter("color_land", PlanetColor);
            mat.SetShaderParameter("color_deep_ocean", OceanColor);
            mat.SetShaderParameter("color_shallow_water", OceanColor.Lerp(Colors.White, 0.2f));
            mat.SetShaderParameter("atmosphere_color", AtmosphereColor);
            
            mat.SetShaderParameter("sea_level", SeaLevel);
            mat.SetShaderParameter("cloud_cover", CloudDensity);

            _planetMesh.MaterialOverride = mat;
            
            // Ensure high quality mesh if it's the default sphere
            if (_planetMesh.Mesh is SphereMesh sphere)
            {
                sphere.Radius = Radius;
                sphere.Height = Radius * 2.0f;
                sphere.Rings = 64; // Higher resolution for vertex shader if needed, or better lighting
                sphere.RadialSegments = 64;
            }
        }

        // === Generate Rings ===
        if (HasRings)
        {
            _ringMesh = new MeshInstance3D();
            _ringMesh.Name = "Rings";
            
            // TorusMesh Logic:
            // OuterRadius and InnerRadius in Godot TorusMesh are weird. 
            // It uses Inner Radius (tube thickness) and Outer Radius (ring size)
            // Wait, Godot 4 TorusMesh:
            // InnerRadius: The radius of the tube itself? No, Godot docs say:
            // inner_radius: "The inner radius of the torus."
            // outer_radius: "The outer radius of the torus."
            // Let's assume standard behavior.
            
            var ringTorus = new TorusMesh();
            
            // We want a flat washer shape. 
            // We can achieve this by making a TorusMesh and scaling it FLAT in Y.
            // Center Radius = RingRadius. Tube Radius = Width/2.
            
            float centerRadius = RingRadius;
            float tubeRadius = RingWidth / 2.0f;
            
            ringTorus.InnerRadius = tubeRadius; // This is the thickness of the ring band (radius of the tube section)
            ringTorus.OuterRadius = centerRadius; // This is the distance from center to center of tube
            
            ringTorus.Rings = 64;
            ringTorus.RingSegments = 4; // Low poly vertical, we will flatten it anyway

            _ringMesh.Mesh = ringTorus;
            
            // Flatten it to look like a disc
            _ringMesh.Scale = new Vector3(1.0f, 0.05f, 1.0f); 
            
            // Rotate Inclination
            _ringMesh.RotationDegrees = new Vector3(RingInclination, 0, 0);

            // Shader
            var ringShader = GD.Load<Shader>("res://Shaders/rings.gdshader");
            var ringMat = new ShaderMaterial();
            ringMat.Shader = ringShader;
            ringMat.SetShaderParameter("ring_color", RingColor);
            ringMat.SetShaderParameter("seed", (float)Seed);
            
            _ringMesh.MaterialOverride = ringMat;
            
            AddChild(_ringMesh);
        }
        
        // Create selection ring (lazy helper)
        if (_selectionRing == null)
        {
            _selectionRing = new MeshInstance3D();
            var torus = new TorusMesh();
            torus.InnerRadius = Radius * 1.2f;
            torus.OuterRadius = Radius * 1.25f;
            _selectionRing.Mesh = torus;
            
            var mat = new StandardMaterial3D();
            mat.AlbedoColor = Colors.Green;
            mat.EmissionEnabled = true;
            mat.Emission = Colors.Green;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            _selectionRing.MaterialOverride = mat;
            
            AddChild(_selectionRing);
            _selectionRing.Visible = false;
        }
    }

    public void SetTargeted(bool targeted)
    {
        if (_selectionRing != null) _selectionRing.Visible = targeted;
    }

    public void Destroy() { }
    public void Damage(float damage, Vector3 direction) { }
    public void Damage(HardpointSpec spec, Vector3 hit, double time) { }
    public void Destroy(System.Collections.Generic.List<Node> nodes) { }
    public bool IsDead() => false;
    public void _OnBodyEntered(Node body) { }
    // public Vector3 Position { get => GlobalPosition; set => GlobalPosition = value; } // Using inherited Position
    
    public Vector3 LinearVelocity { get => Vector3.Zero; set { } } // Static body, no velocity
    
    public RigidBody3D GetRigidBody3D() => null; // Not a rigidbody

    public Godot.Collections.Dictionary GetStatus()
    {
         var dict = new Godot.Collections.Dictionary();
         dict["name"] = PlanetName;
         return dict;
    }
    
    public ShipController MyShip() => null;
}
