using System;
using System.Diagnostics;
using Godot;

namespace Planetsgodot.Scripts.curves;

public class BezierSpline
{
    public (Vector3 cp1, Vector3 cp2)[] cps;
    public Vector3[] points;
    private int index = 0;

    public void OnDrawGizmos()
    {
        DebugDraw3D.DrawPoints(points[0..index], .2f, Colors.Gold);
        DebugDraw3D.DrawPoints(points[index..], .2f, Colors.Gray);

        DebugDraw3D.DrawSphere(cps[0].Item1, .3f, Colors.Cyan);
        DebugDraw3D.DrawSphere(cps[0].Item2, .3f, Colors.Cyan);
    }

    public BezierSpline(int segmentCount, Vector3 pStart, Vector3 pEnd, (Vector3 cp1, Vector3 cp2)[] cps, Vector3 startFwd, float u)
    {
        this.cps = cps;
        int curveCount = cps.Length;
        var position1 = pStart;
        points = new Vector3[curveCount * segmentCount];

        Vector3 right = startFwd.Cross(Vector3.Up);
        // Debug.Print($"Forward: {startFwd} Up: {Vector3.Up} Cross: {right}");

        if (CalcBezier(segmentCount, pStart, pEnd, cps, u, curveCount, position1))
            /* MaxCurve exceeded, do a U-Turn to the opposite side.
             TODO: The other option is playing with the control points, ie, skewing them, but I think the U-turn is guaranteed to work.
             */
        {
            cps = new[]
            {
                (pStart - startFwd * 5, pStart + 5 * right - startFwd * 5)
            };
            // CalcBezier(segmentCount, pStart, pStart + 5 * right, cps, u, curveCount, position1);
        }
    }

    private bool CalcBezier(int segmentCount, Vector3 pStart, Vector3 pEnd, (Vector3 cp1, Vector3 cp2)[] cps, float u, int curveCount,
        Vector3 position1)
    {
        for (int j = 0; j < curveCount; j++)
        {
            var position = position1 + (pEnd - pStart) / curveCount * (j + 1);
            for (int i = 1; i <= segmentCount; i++)
            {
                float t = i / (float)segmentCount;
                Vector3 p = CalculateCubicBezierPoint(t, position1, cps[j].cp1, cps[j].cp2, position, u);
                Vector3 bdt = BdT(t, position1, cps[j].cp1, cps[j].cp2, position);
                Vector3 bddt = BddT(t, position1, cps[j].cp1, cps[j].cp2, position);
                float curvature = Curvature(bdt, bddt);

                if (curvature > 1.0f)
                {
                    return true;
                }

                points[(j * segmentCount) + i - 1] = p;
            }

            position1 = position;
        }

        return false;
    }


    public Vector3 CalculateQuadraticBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        // B(t) = (1-t)²P0 + 2(1-t)tP1 + t²P2 , 0 < t < 1
        Vector3 p = (Mathf.Pow((1 - t), 2) * p0) + (2 * (1 - t) * t * p1) + (Mathf.Pow(t, 2) * p2);
        return p;
    }

    public Vector3 CalculateCubicBezierPoint(float t, Vector3 A, Vector3 B, Vector3 C, Vector3 D, float u)
    {
        // B(t) = (1-t)3P0 + 3(1-t)2tP1 + 3(1-t)t2P2 + t3P3 , 0 < t < 1
        // https://math.stackexchange.com/questions/1021381/how-can-i-limit-the-amount-of-curvature-of-a-bezier-curve
        // 𝐵𝑢=𝐴+(𝐵−𝐴)𝑢
        // 𝐶𝑢=𝐷+(𝐶−𝐷)𝑢

        Vector3 Bu = A + (B - A) * u;
        Vector3 Cu = C; //D + (C - D) * u;
        float t_ = 1 - t;
        float tt = t * t;
        float tt_ = t_ * t_;
        float ttt_ = tt_ * t_;
        float ttt = tt * t;

        Vector3 p = ttt_ * A;
        p += 3 * tt_ * t * Bu;
        p += 3 * t_ * tt * Cu;
        p += ttt * D;

        return p;
    }

    public Vector3 BdT(float t, Vector3 A, Vector3 B, Vector3 C, Vector3 D)

    {
        // 𝑟⃗′(𝑡)=3(1−𝑡)2(𝐵−𝐴)+6(1−𝑡)𝑡(𝐶−𝐵)+3𝑡2(𝐷−𝐶)
        Vector3 bdt = 3 * (1 - t) * (1 - t) * (B - A) + 6 * (1 - t) * t * (C - B) + 3 * t * t * (D - C);
        return bdt;
    }

    public Vector3 BddT(float t, Vector3 A, Vector3 B, Vector3 C, Vector3 D)
    {
        // 𝑟⃗″(𝑡)=6(1−𝑡)(𝐶−2𝐵+𝐴)+6𝑡(𝐷−2𝐶+𝐵)   
        Vector3 bddt = 6 * (1 - t) * (C - 2 * B + A) + 6 * t * (D - 2 * C + B);
        return bddt;
    }

    public float Curvature(Vector3 bdt, Vector3 bddt)
    {
        // 𝜅=|𝑟⃗′×𝑟⃗″|/|𝑟⃗′|^3
        var bdtLength = bdt.Length();
        return (bdt * bddt).Length() / (bdtLength * bdtLength * bdtLength);
    }

    public static Vector3 CalculateLinearBezierPoint(float t, Vector3 pStart, Vector3 pEnd)
    {
        return ((1 - t) * pStart) + (t * pEnd);
    }

    public Vector3 CurrentPoint()
    {
        return points[index];
    }

    public void Next()
    {
        index++;
        if (index >= points.Length) index = points.Length - 1;
    }

    public Vector3 CurrentAveragePoint()
    {
        Vector3 sum = Vector3.Zero;
        int endIndex = index + 1;
        if (endIndex > points.Length - 1) endIndex = points.Length - 1;
        if (endIndex == index) return Vector3.Zero;

        foreach (Vector3 p in points[index..endIndex])
        {
            sum += p;
        }

        sum /= (endIndex - index);
        return sum;
    }

    // Hermite splines
    // 

    /*
     * Arr is the points. Num is the smoothing.
     *
     * function chaikin(arr, num) {
       if (num === 0) return arr;
       const l = arr.length;
       const smooth = arr.map((c,i) => {
       return [
       [0.75*c[0] + 0.25*arr[(i + 1)%l][0],0.75*c[1] + 0.25*arr[(i + 1)%l][1]],
       [0.25*c[0] + 0.75*arr[(i + 1)%l][0],0.25*c[1] + 0.75*arr[(i + 1)%l][1]]
       ];
       }).flat();
       return num === 1 ? smooth : chaikin(smooth, num - 1);
       }


       public class ChaikinCurve {

       public static List<Vector3> SmoothPath(List<Vector3> path)
       {
       var output = new List<Vector3>();

       if (path.Count > 0)
       {
       output.Add(path[0]);
       }

       for (var i = 0; i < path.Count – 1; i++)
       {
       var p0 = path[i];
       var p1 = path[i + 1];
       var p0x = p0.x;
       var p0y = p0.z;
       var p1x = p1.x;
       var p1y = p1.z;

       var qx = 0.75f * p0x + 0.25f * p1x;
       var qy = 0.75f * p0y + 0.25f * p1y;
       var Q = new Vector3(qx, 0, qy);

       var rx = 0.25f * p0x + 0.75f * p1x;
       var ry = 0.25f * p0y + 0.75f * p1y;
       var R = new Vector3(rx, 0, ry);

       output.Add(Q);
       output.Add(R);
       }

       if (path.Count > 1)
       {
       output.Add(path[path.Count – 1]);
       }

       return output;
       }
       }
     */
}