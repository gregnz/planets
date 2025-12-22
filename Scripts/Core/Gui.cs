using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Godot.Collections;
using Planetsgodot.Scripts.Core;

public partial class Gui : MarginContainer
{
    private Tween _tween;
    private Label _shipInfo;
    private Label _targetInfo;
    private Label _historyLabel;
    private Label _weaponInfo;
    private TextureProgressBar _bar;

    private int _animatedHealth = 0;
    private Dictionary _lastTargetStatus;
    private GameController _gameController;

    public override void _Ready()
    {
        _shipInfo = GetNode<Label>("%ShipInfoLabel");
        _targetInfo = GetNode<Label>("%TargetInfoLabel");
        _historyLabel = GetNode<Label>("%TargetHistoryLabel");
        _weaponInfo = GetNode<Label>("%WeaponInfoLabel");

        var signalBus = GetNode<SignalBus>("/root/SignalBus");
        signalBus.Connect("HealthChanged", new Callable(this, nameof(OnPlayerHealthChanged)));
        signalBus.Connect("StatusChanged", new Callable(this, nameof(OnPlayerStatusChanged)));
        signalBus.Connect("NpcTargetChanged", new Callable(this, nameof(OnNPCTargetChanged)));
        signalBus.Connect("WeaponStatusChanged", new Callable(this, nameof(OnPlayerWeaponChanged)));

        _gameController = GetNode<GameController>("/root/GameController");
    }

    public override void _Process(double delta)
    {
        var roundValue = Mathf.Round(_animatedHealth);
        // _numberLabel.Text = roundValue.ToString();
        // _bar.Value = roundValue;

        UpdateTargetDisplay();
    }

    public void OnNPCTargetChanged(Dictionary status)
    {
        _lastTargetStatus = status;
        UpdateTargetDisplay();
    }

    private void UpdateTargetDisplay()
    {
        if (_lastTargetStatus == null || _gameController == null || _gameController.player == null)
            return;

        var status = _lastTargetStatus;

        // Calculate Distance
        float dist = 0f;
        var target = _gameController.GetCurrentTarget();
        if (target != null)
        {
            dist = (_gameController.player.GlobalPosition - target.GlobalPosition).Length();
        }

        float[] shield = status.ContainsKey("shield")
            ? (float[])status["shield"]
            : new float[] { 0, 0, 0, 0 };
        float[] armor = status.ContainsKey("armor")
            ? (float[])status["armor"]
            : new float[] { 0, 0, 0, 0 };

        string name = status.ContainsKey("name") ? (string)status["name"] : "Unknown";
        string pos = status.ContainsKey("position") ? (string)status["position"] : "0 0";
        string state = status.ContainsKey("state") ? (string)status["state"] : "-";
        string tactics = status.ContainsKey("tactics") ? (string)status["tactics"] : "-";
        string threatName = status.ContainsKey("debug_threat_name")
            ? (string)status["debug_threat_name"]
            : "-";
        float threatTime = status.ContainsKey("debug_threat_time")
            ? (float)status["debug_threat_time"]
            : 0f;
        float accel = status.ContainsKey("acceleration") ? (float)status["acceleration"] : 0f;
        float linVel = status.ContainsKey("linear_velocity")
            ? (float)status["linear_velocity"]
            : 0f;
        float maxSpeed = status.ContainsKey("max_speed") ? (float)status["max_speed"] : 0f;

        _targetInfo.Text =
            $@"{name} [{dist:F0}m]
STATE: {state} ({tactics})
VEL: {linVel:F0}
SHD: {shield[0]:F0} {shield[1]:F0} {shield[2]:F0} {shield[3]:F0}
HULL: {armor[0]:F0} {armor[1]:F0} {armor[2]:F0} {armor[3]:F0}";

        // History
        if (status.ContainsKey("decision_history"))
        {
            try 
            {
                // In Godot 4, Variant can be converted to string array directly
                string[] history = status["decision_history"].AsStringArray();
                if (history != null && history.Length > 0)
                {
                    _historyLabel.Text = string.Join("\n", history);
                }
                else
                {
                    _historyLabel.Text = "";
                }
            }
            catch (Exception e)
            {
                 // Fallback if AsStringArray fails (though it shouldn't for string[])
                 GD.PrintErr($"Gui History Error: {e.Message}");
                 _historyLabel.Text = "Error";
            }
        }
    }

    public void OnPlayerWeaponChanged(Dictionary status)
    {
        _weaponInfo.Text =
            $@"{status["name"]}
Ammn: {status["ammo"]}";
    }

    public void OnPlayerStatusChanged(Dictionary status)
    {
        var shield = (float[])status["shield"];
        var armor = (float[])status["armor"];

        _shipInfo.Text =
            $@"{status["name"]}
VEL: {((float)status["linear_velocity"]):F0} / {status["max_speed"]}
SHD: {shield[0]:F0} {shield[1]:F0} {shield[2]:F0} {shield[3]:F0}
HULL: {armor[0]:F0} {armor[1]:F0} {armor[2]:F0} {armor[3]:F0}";
    }

    public void OnPlayerHealthChanged(int playerHealth)
    {
        UpdateHealth(playerHealth);
    }

    public void UpdateHealth(float health)
    {
        _animatedHealth = (int)health;
    }

    public void OnPlayerDied()
    {
        var startColor = new Color(1.0f, 1.0f, 1.0f);
        var endColor = new Color(1.0f, 1.0f, 1.0f, 0.0f);
    }
}
