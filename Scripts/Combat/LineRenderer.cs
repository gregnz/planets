using Godot;

namespace utils;

public class LineRenderer
{
    public int positionCount;
    private Vector3[] positions = new Vector3[10];
    public bool enabled;
    public float widthMultiplier;

    public void SetPositions(Vector3[] points)
    {
        this.positions = points;
    }

    public void SetPosition(int p0, Vector3 position)
    {
        positions[p0] = position;
    }
}