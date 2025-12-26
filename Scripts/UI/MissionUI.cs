using System.Collections.Generic;
using Godot;
using Planetsgodot.Scripts.Missions;
using Planetsgodot.Scripts.Core;

public partial class MissionUI : Control
{
    private Label _commsLabel;
    private RichTextLabel _objectivesLabel;
    private SignalBus _signalBus;

    private Dictionary<string, string> _objectives = new();

    private Label _navMarker;
    private Label _coordsLabel;
    private Vector3? _currentNavTarget;

    public override void _Ready()
    {
        // Setup UI Elements (Creating programmatically for speed, or assume children exist)
        // We'll create a simple VBox layout
        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.TopLeft);
        vbox.Position = new Vector2(20, 20);
        AddChild(vbox);

        _objectivesLabel = new RichTextLabel();
        _objectivesLabel.FitContent = true;
        _objectivesLabel.CustomMinimumSize = new Vector2(300, 0);
        _objectivesLabel.Text = "[b]Objectives:[/b]";
        vbox.AddChild(_objectivesLabel);

        _commsLabel = new Label();
        _commsLabel.Text = "";
        _commsLabel.Modulate = new Color(0, 1, 0); // Green text

        // Position Comms at top center or similar
        _commsLabel.SetAnchorsPreset(LayoutPreset.CenterTop);
        // Manual pos for now since we are adding it to the root control
        _commsLabel.GlobalPosition = new Vector2(500, 50);
        AddChild(_commsLabel);

        // Nav Marker
        _navMarker = new Label();
        _navMarker.Text = "[ NAV ]";
        _navMarker.Modulate = new Color(1, 1, 0); // Yellow
        _navMarker.Visible = false;
        AddChild(_navMarker);

        _signalBus = GetNode<SignalBus>("/root/SignalBus");
        _signalBus.Connect(
            SignalBus.SignalName.MissionMessage,
            Callable.From<string, string>(OnMissionMessage)
        );
        _signalBus.Connect(
            SignalBus.SignalName.MissionObjectiveUpdated,
            Callable.From<string, string, int>(OnObjectiveUpdated)
        );
        _signalBus.Connect(
            "SetNavPoint",
            Callable.From<Vector3>(OnSetNavPoint)
        );
        _signalBus.Connect(
            "ClearNavPoint",
            Callable.From(OnClearNavPoint)
        );

        // Autopilot Label
        _autopilotLabel = new Label();
        _autopilotLabel.Text = "AUTOPILOT ENGAGED";
        _autopilotLabel.Modulate = new Color(1, 0, 0); // Red
        _autopilotLabel.SetAnchorsPreset(LayoutPreset.Center); // Center of screen
        // Adjust for center anchor behavior
        // _autopilotLabel.GrowHorizontal = Control.GrowDirection.Both; 
        // _autopilotLabel.GrowVertical = Control.GrowDirection.Both;
        _autopilotLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _autopilotLabel.GlobalPosition = new Vector2(GetViewportRect().Size.X / 2 - 100, GetViewportRect().Size.Y / 3);
        _autopilotLabel.Scale = new Vector2(2, 2); // Make it big
        _autopilotLabel.Visible = false;
        AddChild(_autopilotLabel);

        // Player Coordinates Label (bottom right)
        _coordsLabel = new Label();
        _coordsLabel.Text = "X: 0  Z: 0";
        _coordsLabel.Modulate = new Color(0.8f, 0.8f, 0.8f); // Light gray
        _coordsLabel.SetAnchorsPreset(LayoutPreset.BottomRight);
        _coordsLabel.GlobalPosition = new Vector2(GetViewportRect().Size.X - 150, GetViewportRect().Size.Y - 40);
        AddChild(_coordsLabel);

        // Heat Bar (bottom center)
        _heatBarBg = new ColorRect();
        _heatBarBg.Color = new Color(0.2f, 0.2f, 0.2f, 0.8f); // Dark gray background
        _heatBarBg.Size = new Vector2(200, 20);
        _heatBarBg.GlobalPosition = new Vector2(GetViewportRect().Size.X / 2 - 100, GetViewportRect().Size.Y - 50);
        AddChild(_heatBarBg);

        _heatBarFill = new ColorRect();
        _heatBarFill.Color = new Color(0, 1, 0); // Green when cool
        _heatBarFill.Size = new Vector2(0, 16); // Width set dynamically
        _heatBarFill.GlobalPosition = new Vector2(GetViewportRect().Size.X / 2 - 98, GetViewportRect().Size.Y - 48);
        AddChild(_heatBarFill);

        _heatLabel = new Label();
        _heatLabel.Text = "HEAT";
        _heatLabel.Modulate = new Color(0.7f, 0.7f, 0.7f);
        _heatLabel.GlobalPosition = new Vector2(GetViewportRect().Size.X / 2 - 100, GetViewportRect().Size.Y - 70);
        AddChild(_heatLabel);
    }

    private Label _autopilotLabel;
    private ColorRect _heatBarBg;
    private ColorRect _heatBarFill;
    private Label _heatLabel;

    private void OnSetNavPoint(Vector3 pos)
    {
        GD.Print($"[MissionUI] SetNavPoint received: {pos}");
        _currentNavTarget = pos;
        _navMarker.Visible = true;
    }

    private void OnClearNavPoint()
    {
        GD.Print("[MissionUI] ClearNavPoint received");
        _currentNavTarget = null;
        _navMarker.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_currentNavTarget.HasValue && _navMarker.Visible)
        {
            var cam = GetViewport().GetCamera3D();
            if (cam == null)
            {
                if (Engine.GetFramesDrawn() % 60 == 0) GD.Print("[MissionUI] No active camera found!");
                return;
            }

            Vector3 targetPos = _currentNavTarget.Value;
            float dist = cam.GlobalPosition.DistanceTo(targetPos);
            _navMarker.Text = $"[ NAV ]\n{dist:F0}m";

            // Check if point is behind camera
            if (cam.IsPositionBehind(targetPos))
            {
                // If behind, handle gracefully? 
                // For a simple approach, we can project it to the edge opposite to where it is?
                // Or just hide it as currently done.
                // However, often "Behind" means "Behind the camera plane", so we might want to flip it.
                // But let's start with clamping the "Front" ones which are the confusing ones (e.g. -900 Y).
                _navMarker.Modulate = new Color(1, 1, 0, 0.2f);
            }
            else
            {
                Vector2 screenPos = cam.UnprojectPosition(targetPos);

                // Clamp to screen
                Rect2 viewport = GetViewportRect();
                float margin = 30f; // px

                Vector2 clampedPos = screenPos;
                clampedPos.X = Mathf.Clamp(screenPos.X, margin, viewport.Size.X - margin);
                clampedPos.Y = Mathf.Clamp(screenPos.Y, margin, viewport.Size.Y - margin);

                _navMarker.GlobalPosition = clampedPos - (_navMarker.Size / 2);
                _navMarker.Modulate = new Color(1, 1, 0, 1.0f);
            }
        }

        // Check Autopilot Status
        // Need access to Player via singleton or path
        var gc = GetNodeOrNull<GameController>("/root/GameController");
        if (gc != null && gc.player != null)
        {
            if (gc.player.ship.InWarp)
            {
                _autopilotLabel.Visible = true;
                // Maybe blink?
                if (Engine.GetFramesDrawn() % 30 < 15) _autopilotLabel.Modulate = new Color(1, 0, 0, 1);
                else _autopilotLabel.Modulate = new Color(1, 0, 0, 0.5f);
            }
            else
            {
                _autopilotLabel.Visible = false;
            }

            // Update player coordinates
            _coordsLabel.Text = $"X: {gc.player.Position.X:F0}  Z: {gc.player.Position.Z:F0}";

            // Update heat bar
            var fireSystem = gc.player.GetFireSystem();
            if (fireSystem != null)
            {
                float heatPercent = fireSystem.HeatPercent;
                float barWidth = Mathf.Clamp(heatPercent * 196f, 0, 196); // Max 196px (with 2px margin)
                _heatBarFill.Size = new Vector2(barWidth, 16);

                // Color based on heat level: green -> yellow -> red
                if (heatPercent < 0.5f)
                    _heatBarFill.Color = new Color(0, 1, 0); // Green
                else if (heatPercent < 1.0f)
                    _heatBarFill.Color = new Color(1, 1, 0); // Yellow
                else if (heatPercent < 1.2f)
                    _heatBarFill.Color = new Color(1, 0.5f, 0); // Orange (overheated)
                else
                    _heatBarFill.Color = new Color(1, 0, 0); // Red (critical)

                // Update label with percentage
                _heatLabel.Text = $"HEAT {heatPercent * 100:F0}%";
            }
        }
    }

    private void OnMissionMessage(string sender, string message)
    {
        _commsLabel.Text = $"{sender}: {message}";

        // Clear message after 5 seconds
        GetTree().CreateTimer(5.0f).Connect("timeout", Callable.From(() => _commsLabel.Text = ""));
    }

    private void OnObjectiveUpdated(string id, string desc, int status)
    {
        string statusStr = ((ObjectiveStatus)status).ToString();
        string color = "white";
        if (status == (int)ObjectiveStatus.Complete)
            color = "green";
        if (status == (int)ObjectiveStatus.Failed)
            color = "red";

        _objectives[id] = $"[color={color}]- {desc} ({statusStr})[/color]";
        UpdateObjectivesDisplay();
    }

    private void UpdateObjectivesDisplay()
    {
        string text = "[b]Objectives:[/b]\n";
        foreach (var obj in _objectives.Values)
        {
            text += obj + "\n";
        }

        _objectivesLabel.Text = text;
    }
}
