using System;
using System.Collections.Generic;
using Godot;

namespace Planetsgodot.Scripts.AI;

/// <summary>
/// Manages ship formations and calculates positions for formation members.
/// </summary>
public class FormationManager
{
    /// <summary>
    /// Formation types
    /// </summary>
    public enum FormationType
    {
        Line,       // Ships in a line behind leader
        Wedge,      // V-formation (attack)
        Diamond,    // Diamond (defensive)
        Echelon,    // Angled line (flanking)
        Spread      // Horizontal line (patrol)
    }

    // Formation parameters
    public float Spacing { get; set; } = 3.0f;      // Distance between ships (reduced from 8)
    public float Depth { get; set; } = 4.0f;        // Forward/back offset per rank (reduced from 6)
    public FormationType CurrentFormation { get; set; } = FormationType.Wedge;

    /// <summary>
    /// Get the local offset position for a ship in formation (relative to leader).
    /// Index 0 is the leader (always at origin).
    /// </summary>
    public Vector3 GetFormationOffset(int shipIndex)
    {
        if (shipIndex == 0) return Vector3.Zero; // Leader is at center

        return CurrentFormation switch
        {
            FormationType.Line => GetLineOffset(shipIndex),
            FormationType.Wedge => GetWedgeOffset(shipIndex),
            FormationType.Diamond => GetDiamondOffset(shipIndex),
            FormationType.Echelon => GetEchelonOffset(shipIndex),
            FormationType.Spread => GetSpreadOffset(shipIndex),
            _ => GetWedgeOffset(shipIndex)
        };
    }

    /// <summary>
    /// Get world position for a ship in formation given leader's position and rotation.
    /// </summary>
    public Vector3 GetFormationPosition(int shipIndex, Vector3 leaderPosition, float leaderRotationY)
    {
        Vector3 localOffset = GetFormationOffset(shipIndex);
        
        // Rotate offset by leader's yaw
        float cos = Mathf.Cos(leaderRotationY);
        float sin = Mathf.Sin(leaderRotationY);
        
        Vector3 worldOffset = new Vector3(
            localOffset.X * cos - localOffset.Z * sin,
            0,
            localOffset.X * sin + localOffset.Z * cos
        );
        
        return leaderPosition + worldOffset;
    }

    /// <summary>
    /// Get orientation for formation member (usually matches leader).
    /// </summary>
    public float GetFormationOrientation(int shipIndex, float leaderRotationY)
    {
        // Most formations face the same direction as leader
        // Could add offset for echelon or other formations
        return leaderRotationY;
    }

    // === FORMATION PATTERNS ===

    /// <summary>
    /// Line formation: Ships in a column behind leader
    /// Pattern: 0
    ///          1
    ///          2
    ///          3
    /// </summary>
    private Vector3 GetLineOffset(int index)
    {
        // Each ship directly behind the previous
        return new Vector3(0, 0, Depth * index);
    }

    /// <summary>
    /// Wedge/V formation: Classic attack formation
    /// Pattern:     0
    ///           1     2
    ///        3     4     5
    /// </summary>
    private Vector3 GetWedgeOffset(int index)
    {
        // Calculate row (1, 2, 3, ...) and position within row
        int row = (int)Math.Floor((-1 + Math.Sqrt(1 + 8 * index)) / 2.0);
        int rowStart = row * (row - 1) / 2 + 1;
        int posInRow = index - rowStart;
        
        // Row 1 has 2 ships, Row 2 has 3 ships, etc.
        int rowSize = row + 1;
        
        // Center the row
        float xOffset = (posInRow - (rowSize - 1) / 2.0f) * Spacing;
        float zOffset = row * Depth;
        
        return new Vector3(xOffset, 0, zOffset);
    }

    /// <summary>
    /// Diamond formation: Defensive formation
    /// Pattern:     0
    ///           1     2
    ///              3
    /// </summary>
    private Vector3 GetDiamondOffset(int index)
    {
        return index switch
        {
            1 => new Vector3(-Spacing, 0, Depth),
            2 => new Vector3(Spacing, 0, Depth),
            3 => new Vector3(0, 0, Depth * 2),
            4 => new Vector3(-Spacing * 2, 0, Depth * 2),
            5 => new Vector3(Spacing * 2, 0, Depth * 2),
            _ => new Vector3(0, 0, Depth * (index / 2 + 1))
        };
    }

    /// <summary>
    /// Echelon formation: Angled line for flanking
    /// Pattern: 0
    ///            1
    ///               2
    ///                  3
    /// </summary>
    private Vector3 GetEchelonOffset(int index)
    {
        // Angled back and to the right
        return new Vector3(Spacing * index * 0.7f, 0, Depth * index);
    }

    /// <summary>
    /// Spread formation: Horizontal line for patrol/search
    /// Pattern: 3  1  0  2  4
    /// </summary>
    private Vector3 GetSpreadOffset(int index)
    {
        // Alternating left and right
        int side = index % 2 == 1 ? -1 : 1;
        int distance = (index + 1) / 2;
        
        return new Vector3(Spacing * distance * side, 0, 0);
    }

    /// <summary>
    /// Get all formation positions for a squad
    /// </summary>
    public List<Vector3> GetAllFormationPositions(int shipCount, Vector3 leaderPosition, float leaderRotationY)
    {
        var positions = new List<Vector3>(shipCount);
        for (int i = 0; i < shipCount; i++)
        {
            positions.Add(GetFormationPosition(i, leaderPosition, leaderRotationY));
        }
        return positions;
    }
}
