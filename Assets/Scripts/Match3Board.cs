using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class Match3Board : MonoBehaviour
{
    [Header("Board")]
    [SerializeField] private int width = 7;
    [SerializeField] private int height = 7;
    [SerializeField] private int tileTypeCount = 6;
    [SerializeField] private float tileSpacing = 1f;

    [Header("Animation")]
    [SerializeField] private float swapDuration = 0.14f;
    [SerializeField] private float fallDuration = 0.10f;

    private int[,] tileTypes;
    private TileView[,] tileViews;
    private Sprite tileSprite;
    private readonly List<Color> palette = new List<Color>
    {
        new Color(0.95f, 0.30f, 0.30f),
        new Color(0.25f, 0.60f, 0.95f),
        new Color(0.30f, 0.85f, 0.40f),
        new Color(0.96f, 0.82f, 0.20f),
        new Color(0.75f, 0.40f, 0.95f),
        new Color(0.95f, 0.55f, 0.20f),
    };

    private TileView selectedTile;
    private TileView pointerDownTile;
    private bool dragSwapTriggered;
    private bool isBusy;
    private int score;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<Match3Board>() != null)
        {
            return;
        }

        var root = new GameObject("Match3Board");
        root.AddComponent<Match3Board>();
    }

    private void Awake()
    {
        tileTypeCount = Mathf.Clamp(tileTypeCount, 3, palette.Count);
        tileTypes = new int[width, height];
        tileViews = new TileView[width, height];
        tileSprite = CreateTileSprite();
        PrepareCamera();
        GenerateBoardWithoutStartingMatches();
    }

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        GUI.Label(new Rect(14, 12, 400, 40), $"Score: {score}", style);
    }

    private void Update()
    {
        HandlePointerInput();
    }

    public void OnTileClicked(TileView clicked)
    {
        if (isBusy)
        {
            return;
        }

        if (selectedTile == null)
        {
            selectedTile = clicked;
            selectedTile.SetSelected(true);
            return;
        }

        if (selectedTile == clicked)
        {
            selectedTile.SetSelected(false);
            selectedTile = null;
            return;
        }

        bool adjacent = IsAdjacent(selectedTile, clicked);
        if (!adjacent)
        {
            selectedTile.SetSelected(false);
            selectedTile = clicked;
            selectedTile.SetSelected(true);
            return;
        }

        selectedTile.SetSelected(false);
        StartCoroutine(TrySwapAndResolve(selectedTile, clicked));
        selectedTile = null;
    }

    private void HandlePointerInput()
    {
        if (isBusy)
        {
            if (IsPointerReleasedThisFrame())
            {
                pointerDownTile = null;
                dragSwapTriggered = false;
            }
            return;
        }

        if (IsPointerPressedThisFrame())
        {
            pointerDownTile = TryGetTileUnderPointer(out TileView downTile) ? downTile : null;
            dragSwapTriggered = false;
        }

        if (IsPointerHeld() &&
            pointerDownTile != null &&
            !dragSwapTriggered &&
            TryGetTileUnderPointer(out TileView hoverTile) &&
            hoverTile != pointerDownTile &&
            IsAdjacent(pointerDownTile, hoverTile))
        {
            OnTileClicked(pointerDownTile);
            OnTileClicked(hoverTile);
            dragSwapTriggered = true;
        }

        if (IsPointerReleasedThisFrame())
        {
            if (pointerDownTile != null &&
                !dragSwapTriggered &&
                TryGetTileUnderPointer(out TileView upTile) &&
                upTile == pointerDownTile)
            {
                OnTileClicked(pointerDownTile);
            }

            pointerDownTile = null;
            dragSwapTriggered = false;
        }
    }

    private static bool IsAdjacent(TileView a, TileView b)
    {
        return Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Y - b.Y) == 1;
    }

    private bool TryGetTileUnderPointer(out TileView tile)
    {
        tile = null;
        if (!TryGetPointerScreenPosition(out Vector2 screenPos))
        {
            return false;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return false;
        }

        Vector3 world = cam.ScreenToWorldPoint(screenPos);
        Collider2D hit = Physics2D.OverlapPoint(new Vector2(world.x, world.y));
        if (hit == null)
        {
            return false;
        }

        tile = hit.GetComponent<TileView>();
        return tile != null;
    }

    private static bool TryGetPointerScreenPosition(out Vector2 screenPos)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            screenPos = Mouse.current.position.ReadValue();
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        screenPos = Input.mousePosition;
        return true;
#else
        screenPos = default;
        return false;
#endif
    }

    private static bool IsPointerPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }

    private static bool IsPointerHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(0);
#else
        return false;
#endif
    }

    private static bool IsPointerReleasedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonUp(0);
#else
        return false;
#endif
    }

    private IEnumerator TrySwapAndResolve(TileView a, TileView b)
    {
        isBusy = true;

        yield return StartCoroutine(AnimateSwap(a, b, swapDuration));
        SwapData(a, b);

        bool[,] matched = FindMatches();
        if (!HasAnyMatch(matched))
        {
            yield return StartCoroutine(AnimateSwap(a, b, swapDuration));
            SwapData(a, b);
            isBusy = false;
            yield break;
        }

        yield return StartCoroutine(ResolveBoard());
        isBusy = false;
    }

    private IEnumerator ResolveBoard()
    {
        while (true)
        {
            bool[,] matched = FindMatches();
            if (!HasAnyMatch(matched))
            {
                yield break;
            }

            int cleared = ClearMatches(matched);
            score += cleared * 10;

            yield return StartCoroutine(CollapseColumns());
            yield return StartCoroutine(RefillBoard());
        }
    }

    private void GenerateBoardWithoutStartingMatches()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int type = Random.Range(0, tileTypeCount);

                // Prevent initial horizontal/vertical 3-in-a-row on generation.
                while ((x >= 2 && tileTypes[x - 1, y] == type && tileTypes[x - 2, y] == type) ||
                       (y >= 2 && tileTypes[x, y - 1] == type && tileTypes[x, y - 2] == type))
                {
                    type = Random.Range(0, tileTypeCount);
                }

                CreateTile(x, y, type);
            }
        }
    }

    private void CreateTile(int x, int y, int type)
    {
        tileTypes[x, y] = type;

        GameObject go = new GameObject($"Tile_{x}_{y}");
        go.transform.SetParent(transform);
        go.transform.position = GridToWorld(x, y);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = tileSprite;
        sr.color = palette[type];
        sr.sortingOrder = 10;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.95f, 0.95f);

        TileView tv = go.AddComponent<TileView>();
        tv.Setup(x, y, palette[type]);

        tileViews[x, y] = tv;
    }

    private IEnumerator AnimateSwap(TileView a, TileView b, float duration)
    {
        Vector3 aStart = a.transform.position;
        Vector3 bStart = b.transform.position;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            a.transform.position = Vector3.Lerp(aStart, bStart, k);
            b.transform.position = Vector3.Lerp(bStart, aStart, k);
            yield return null;
        }

        a.transform.position = bStart;
        b.transform.position = aStart;
    }

    private void SwapData(TileView a, TileView b)
    {
        int ax = a.X;
        int ay = a.Y;
        int bx = b.X;
        int by = b.Y;

        (tileTypes[ax, ay], tileTypes[bx, by]) = (tileTypes[bx, by], tileTypes[ax, ay]);
        (tileViews[ax, ay], tileViews[bx, by]) = (tileViews[bx, by], tileViews[ax, ay]);

        a.SetGridPosition(bx, by);
        b.SetGridPosition(ax, ay);
    }

    private bool[,] FindMatches()
    {
        bool[,] matched = new bool[width, height];

        // Horizontal matches.
        for (int y = 0; y < height; y++)
        {
            int runStart = 0;
            while (runStart < width)
            {
                int runType = tileTypes[runStart, y];
                if (runType < 0)
                {
                    runStart++;
                    continue;
                }

                int runEnd = runStart + 1;
                while (runEnd < width && tileTypes[runEnd, y] == runType)
                {
                    runEnd++;
                }

                if (runEnd - runStart >= 3)
                {
                    for (int x = runStart; x < runEnd; x++)
                    {
                        matched[x, y] = true;
                    }
                }

                runStart = runEnd;
            }
        }

        // Vertical matches.
        for (int x = 0; x < width; x++)
        {
            int runStart = 0;
            while (runStart < height)
            {
                int runType = tileTypes[x, runStart];
                if (runType < 0)
                {
                    runStart++;
                    continue;
                }

                int runEnd = runStart + 1;
                while (runEnd < height && tileTypes[x, runEnd] == runType)
                {
                    runEnd++;
                }

                if (runEnd - runStart >= 3)
                {
                    for (int y = runStart; y < runEnd; y++)
                    {
                        matched[x, y] = true;
                    }
                }

                runStart = runEnd;
            }
        }

        return matched;
    }

    private bool HasAnyMatch(bool[,] matched)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (matched[x, y])
                {
                    return true;
                }
            }
        }
        return false;
    }

    private int ClearMatches(bool[,] matched)
    {
        int cleared = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!matched[x, y])
                {
                    continue;
                }

                cleared++;
                tileTypes[x, y] = -1;
                Destroy(tileViews[x, y].gameObject);
                tileViews[x, y] = null;
            }
        }

        return cleared;
    }

    private IEnumerator CollapseColumns()
    {
        for (int x = 0; x < width; x++)
        {
            int writeY = 0;

            for (int y = 0; y < height; y++)
            {
                if (tileTypes[x, y] < 0)
                {
                    continue;
                }

                if (writeY != y)
                {
                    tileTypes[x, writeY] = tileTypes[x, y];
                    tileTypes[x, y] = -1;

                    tileViews[x, writeY] = tileViews[x, y];
                    tileViews[x, y] = null;

                    TileView moved = tileViews[x, writeY];
                    moved.SetGridPosition(x, writeY);
                }

                writeY++;
            }
        }

        yield return StartCoroutine(AnimateBoardToGrid(fallDuration));
    }

    private IEnumerator RefillBoard()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (tileTypes[x, y] >= 0)
                {
                    continue;
                }

                int type = Random.Range(0, tileTypeCount);
                tileTypes[x, y] = type;

                GameObject go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(transform);
                go.transform.position = GridToWorld(x, height + 1);

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tileSprite;
                sr.color = palette[type];
                sr.sortingOrder = 10;

                BoxCollider2D col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(0.95f, 0.95f);

                TileView tv = go.AddComponent<TileView>();
                tv.Setup(x, y, palette[type]);
                tileViews[x, y] = tv;
            }
        }

        yield return StartCoroutine(AnimateBoardToGrid(fallDuration));
    }

    private IEnumerator AnimateBoardToGrid(float duration)
    {
        List<TileView> allTiles = new List<TileView>();
        List<Vector3> starts = new List<Vector3>();
        List<Vector3> targets = new List<Vector3>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                TileView tv = tileViews[x, y];
                if (tv == null)
                {
                    continue;
                }

                allTiles.Add(tv);
                starts.Add(tv.transform.position);
                targets.Add(GridToWorld(x, y));
            }
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);

            for (int i = 0; i < allTiles.Count; i++)
            {
                allTiles[i].transform.position = Vector3.Lerp(starts[i], targets[i], k);
            }

            yield return null;
        }

        for (int i = 0; i < allTiles.Count; i++)
        {
            allTiles[i].transform.position = targets[i];
        }
    }

    private Vector3 GridToWorld(int x, int y)
    {
        float xOffset = (width - 1) * tileSpacing * 0.5f;
        float yOffset = (height - 1) * tileSpacing * 0.5f;
        return new Vector3(x * tileSpacing - xOffset, y * tileSpacing - yOffset, 0f);
    }

    private Sprite CreateTileSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
    }

    private void PrepareCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        cam.orthographic = true;
        cam.orthographicSize = 5.3f;
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.15f);
        cam.transform.position = new Vector3(0f, 0f, -10f);
    }
}
