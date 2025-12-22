using System.Collections.Generic;
using Godot;

namespace utils
{
	
	
	static class Curves
	{
		static Node3D[] sphere;
		


		

		public static void DebugCurve(Vector3[] points, (Vector3, Vector3)[] cps)
		{
			Vector3 sp = points[0];
			for (int i = 1; i < points.Length; i++)
			{
				sp = points[i];
			}

			for (int i = 0; i < cps.Length; i++)
			{
			}
		}

		// public static Vector3[] CalcCurve(Vector3 P0, Vector3 P1, (Vector3 cp1, Vector3 cp2)[] cps, float u)
		// {
		//     q(t) = 0.5 *(  	(2 * P1) +
		//                     (-P0 + P2) * t +
		//                     (2*P0 - 5*P1 + 4*P2 - P3) * t2 +
		//                     (-P0 + 3*P1- 3*P2 + P3) * t3)
		// }

	}

	static class Util
	{
		public static string VP(Vector3 pos)
		{
			return $"({pos.X:f2},{pos.Y:f2},{pos.Z:f2})";
		}

		/*
		public static void AddOrUpdate(Dictionary<Good, List<float>> targetDictionary, Good key, float entry)
		{
			if (!targetDictionary.ContainsKey(key))
				targetDictionary.Add(key, new List<float>());

			targetDictionary[key].Add(entry);
		}
		*/
		
		public static void CalcDrag(Vector3 dragDirection, double crossSectionalArea, float vel, float dragCoefficient, out Vector3 drag)
		{
			float density = 15f; // https://en.wikipedia.org/wiki/Density Air = 1.2, water = 1000
			double d = 0.5f * density * vel * vel * dragCoefficient * crossSectionalArea;
			drag = dragDirection.Normalized() * (float)d;
		}

	}
}
