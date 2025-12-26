using System;
using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

public partial class CampaignManager : Node
{
    public static CampaignManager Instance { get; private set; }

    public CampaignState State { get; private set; }

    private const string SAVE_FILE_PATH = "user://campaign_save.json";

    public override void _Ready()
    {
        if (Instance == null)
        {
            Instance = this;
            LoadGame();
        }
        else
        {
            QueueFree();
        }
    }

    public void NewGame(string pilotName)
    {
        State = new CampaignState
        {
            PilotName = pilotName,
            Rank = CampaignRank.Cadet,
            IsNavy = true,
            CurrentMissionId = "mission_01_patrol",
            Stats = new PlayerStats(),
            UnlockedShips = new List<string> { "SidewinderMkI" },
            UnlockedWeapons = new List<string> { "Weapon_Laser_SmallLaserER_1-Diverse_Optics" },
        };
        SaveGame();
    }

    public void SaveGame()
    {
        string json = JsonConvert.SerializeObject(State, Formatting.Indented);
        using var file = Godot.FileAccess.Open(SAVE_FILE_PATH, Godot.FileAccess.ModeFlags.Write);
        file.StoreString(json);
        GD.Print("Game Saved.");
    }

    public void LoadGame()
    {
        if (Godot.FileAccess.FileExists(SAVE_FILE_PATH))
        {
            using var file = Godot.FileAccess.Open(SAVE_FILE_PATH, Godot.FileAccess.ModeFlags.Read);
            string json = file.GetAsText();
            State = JsonConvert.DeserializeObject<CampaignState>(json);
            GD.Print("Game Loaded.");
        }
        else
        {
            NewGame("Maverick"); // Default
        }
    }

    public void AddKill(string shipType)
    {
        State.Stats.Kills++;
        State.Stats.Score += 100; // Placeholder score logic
        CheckPromotions();
        // SaveGame(); // Maybe don't save on every kill?
    }

    public void AddAssist()
    {
        State.Stats.Assists++;
        State.Stats.Score += 25;
    }

    public void CompleteMission(bool success)
    {
        // Logic to move to next mission comes from the Mission definition
        // For now, we'll let MissionController handle the specific next mission ID
        // and we just update stats here.
        SaveGame();
    }

    private void CheckPromotions()
    {
        // Simple example based on just kills
        if (State.Rank == CampaignRank.Cadet && State.Stats.Kills >= 5)
        {
            Promote(CampaignRank.Ensign);
        }
        else if (State.Rank == CampaignRank.Ensign && State.Stats.Kills >= 15)
        {
            Promote(CampaignRank.Lieutenant);
        }
    }

    private void Promote(CampaignRank newRank)
    {
        State.Rank = newRank;
        GD.Print($"PROMOTION! You are now a {newRank}");
        // Signal/UI notification here
    }

    public void UnlockShip(string shipId)
    {
        if (!State.UnlockedShips.Contains(shipId))
        {
            State.UnlockedShips.Add(shipId);
            GD.Print($"Unlocked Ship: {shipId}");
        }
    }
}

public class CampaignState
{
    public string PilotName { get; set; }
    public CampaignRank Rank { get; set; }
    public bool IsNavy { get; set; } // True = Navy, False = Civilian/Mercenary
    public string CurrentMissionId { get; set; }
    public PlayerStats Stats { get; set; }
    public List<string> UnlockedShips { get; set; } = new();
    public List<string> UnlockedWeapons { get; set; } = new();
}

public class PlayerStats
{
    public int Kills { get; set; }
    public int Assists { get; set; }
    public int Deaths { get; set; }
    public int Score { get; set; }
}

public enum CampaignRank
{
    Cadet,
    Ensign,
    Lieutenant,
    LieutenantCommander,
    Commander,
    Captain,
}
