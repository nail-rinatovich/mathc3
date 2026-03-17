using UnityEngine;

public class TileView : MonoBehaviour
{
    public int X { get; private set; }
    public int Y { get; private set; }

    private SpriteRenderer spriteRenderer;
    private Color baseColor;

    public void Setup(int x, int y, Color color)
    {
        X = x;
        Y = y;
        baseColor = color;
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.color = color;
    }

    public void SetGridPosition(int x, int y)
    {
        X = x;
        Y = y;
        gameObject.name = $"Tile_{x}_{y}";
    }

    public void SetSelected(bool selected)
    {
        transform.localScale = selected ? Vector3.one * 1.12f : Vector3.one;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = selected ? Color.white : baseColor;
        }
    }
}
