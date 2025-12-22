using System.Collections.Generic;
using Godot;
using Planetsgodot.Scripts.Missions;

public partial class MissionUI : Control
{
    private Label _commsLabel;
    private RichTextLabel _objectivesLabel;
    private SignalBus _signalBus;

    private Dictionary<string, string> _objectives = new();

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

        _signalBus = GetNode<SignalBus>("/root/SignalBus");
        _signalBus.Connect(
            SignalBus.SignalName.MissionMessage,
            Callable.From<string, string>(OnMissionMessage)
        );
        _signalBus.Connect(
            SignalBus.SignalName.MissionObjectiveUpdated,
            Callable.From<string, string, int>(OnObjectiveUpdated)
        );
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
