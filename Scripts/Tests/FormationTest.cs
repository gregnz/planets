using System;
using Godot;
using Planetsgodot.Scripts.AI;

namespace Planetsgodot.Scripts.Tests
{
    public class FormationTest
    {
        public static void Main()
        {
            FormationManager fm = new FormationManager();
            fm.Spacing = 10f;
            fm.Depth = 10f;
            fm.CurrentFormation = FormationManager.FormationType.Wedge;

            Console.WriteLine("Testing Wedge Formation Offsets:");
            for (int i = 0; i < 10; i++)
            {
                Vector3 offset = fm.GetFormationOffset(i);
                Console.WriteLine($"Index {i}: {offset}");
            }
        }
    }
}
