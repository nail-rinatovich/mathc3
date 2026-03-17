using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class Match3Board : MonoBehaviour
{
    //Создаем переменные
    [Header("Board")] // Заголовок в инспекторе Unity
    [SerializeField] private int width = 7; // Ширина игрового поля (количество клеток по горизонтали)
    [SerializeField] private int height = 7; // Высота игрового поля (количество клеток по вертикали)
    [SerializeField] private int tileTypeCount = 6; // Количество различных типов плиток
    [SerializeField] private float tileSpacing = 1f; // Расстояние между плитками

    [Header("Animation")] // Заголовок для анимаций в инспекторе
    [SerializeField] private float swapDuration = 0.14f; // Длительность анимации обмена плиток
    [SerializeField] private float fallDuration = 0.10f; // Длительность анимации падения плиток

    private int[,] tileTypes; // Двумерный массив для хранения типов плиток на каждой позиции
    private TileView[,] tileViews; // Двумерный массив для хранения ссылок на визуальные компоненты плиток
    private Sprite tileSprite; // Спрайт для отображения плиток (будет создан программно)
    
    //создаем цвета
    private readonly List<Color> palette = new List<Color> // Палитра цветов для разных типов плиток
    {
        new Color(0.95f, 0.30f, 0.30f), // Красный
        new Color(0.25f, 0.60f, 0.95f), // Синий
        new Color(0.30f, 0.85f, 0.40f), // Зеленый
        new Color(0.96f, 0.82f, 0.20f), // Желтый
        new Color(0.75f, 0.40f, 0.95f), // Фиолетовый
        new Color(0.95f, 0.55f, 0.20f), // Оранжевый
    };

    private TileView selectedTile; // Текущая выбранная плитка
    private TileView pointerDownTile; // Плитка, на которую нажали (для drag-and-drop)
    private bool dragSwapTriggered; // Флаг, был ли уже вызван обмен при перетаскивании
    private bool isBusy; // Флаг, выполняется ли сейчас анимация/обработка (блокирует новые действия)
    private int score; // Текущий счет игрока

    // Метод автоматического создания объекта игры, если его нет на сцене
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        // Проверяем, существует ли уже объект Match3Board на сцене
        if (FindAnyObjectByType<Match3Board>() != null)
        {
            return; // Если существует, ничего не делаем
        }

        // Создаем новый корневой объект с компонентом Match3Board
        var root = new GameObject("Match3Board");
        root.AddComponent<Match3Board>();
    }

    private void Awake()
    {
        // Ограничиваем количество типов плиток доступной палитрой (минимум 3)
        tileTypeCount = Mathf.Clamp(tileTypeCount, 3, palette.Count);
        tileTypes = new int[width, height]; // Инициализируем массив типов
        tileViews = new TileView[width, height]; // Инициализируем массив представлений
        tileSprite = CreateTileSprite(); // Создаем спрайт для плиток
        PrepareCamera(); // Настраиваем камеру
        GenerateBoardWithoutStartingMatches(); // Генерируем поле без совпадений в начале
    }

    // Отображение счета на экране (GUI)
    private void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28, // Размер шрифта
            fontStyle = FontStyle.Bold, // Жирный стиль
            normal = { textColor = Color.white } // Белый цвет текста
        };

        // Создаем метку с текстом счета в верхнем левом углу
        GUI.Label(new Rect(14, 12, 400, 40), $"Score: {score}", style);
    }

    private void Update()
    {
        HandlePointerInput(); // Обрабатываем ввод с мыши/касания каждый кадр
    }

    // Обработка клика по плитке (вызывается из TileView)
    public void OnTileClicked(TileView clicked)
    {
        if (isBusy) // Если выполняется анимация - игнорируем клик
        {
            return;
        }

        if (selectedTile == null) // Если нет выбранной плитки
        {
            selectedTile = clicked; // Запоминаем выбранную плитку
            selectedTile.SetSelected(true); // Визуально выделяем её
            return;
        }

        if (selectedTile == clicked) // Если кликнули по той же плитке
        {
            selectedTile.SetSelected(false); // Снимаем выделение
            selectedTile = null; // Сбрасываем выбранную плитку
            return;
        }

        bool adjacent = IsAdjacent(selectedTile, clicked); // Проверяем, соседние ли плитки
        if (!adjacent) // Если не соседние
        {
            selectedTile.SetSelected(false); // Снимаем выделение со старой
            selectedTile = clicked; // Выбираем новую плитку
            selectedTile.SetSelected(true); // Выделяем её
            return;
        }

        // Если плитки соседние - пытаемся их поменять
        selectedTile.SetSelected(false); // Снимаем выделение
        StartCoroutine(TrySwapAndResolve(selectedTile, clicked)); // Запускаем корутину обмена и обработки
        selectedTile = null; // Сбрасываем выбранную плитку
    }

    // Обработка ввода с мыши/касания для drag-and-drop
    private void HandlePointerInput()
    {
        if (isBusy) // Если выполняется анимация
        {
            if (IsPointerReleasedThisFrame()) // Если кнопка отпущена в этом кадре
            {
                pointerDownTile = null; // Сбрасываем плитку под курсором
                dragSwapTriggered = false; // Сбрасываем флаг перетаскивания
            }
            return; // Выходим, так как заняты
        }

        if (IsPointerPressedThisFrame()) // Если кнопка нажата в этом кадре
        {
            // Запоминаем плитку под курсором, если есть
            pointerDownTile = TryGetTileUnderPointer(out TileView downTile) ? downTile : null;
            dragSwapTriggered = false; // Сбрасываем флаг перетаскивания
        }

        if (IsPointerHeld() && // Если кнопка удерживается
            pointerDownTile != null && // И есть плитка, на которую нажали
            !dragSwapTriggered && // И обмен еще не был вызван
            TryGetTileUnderPointer(out TileView hoverTile) && // И есть плитка под курсором
            hoverTile != pointerDownTile && // И это другая плитка
            IsAdjacent(pointerDownTile, hoverTile)) // И они соседние
        {
            // Вызываем обмен плиток (через логику кликов)
            OnTileClicked(pointerDownTile);
            OnTileClicked(hoverTile);
            dragSwapTriggered = true; // Помечаем, что обмен вызван
        }

        if (IsPointerReleasedThisFrame()) // Если кнопка отпущена в этом кадре
        {
            if (pointerDownTile != null && // Если была плитка, на которую нажали
                !dragSwapTriggered && // И обмен не был вызван
                TryGetTileUnderPointer(out TileView upTile) && // И есть плитка под курсором
                upTile == pointerDownTile) // И это та же плитка (простой клик, не drag)
            {
                OnTileClicked(pointerDownTile); // Обрабатываем клик
            }

            pointerDownTile = null; // Сбрасываем плитку под курсором
            dragSwapTriggered = false; // Сбрасываем флаг перетаскивания
        }
    }

    // Проверка, являются ли две плитки соседними (по горизонтали или вертикали)
    private static bool IsAdjacent(TileView a, TileView b)
    {
        return Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Y - b.Y) == 1; // Сумма разностей координат = 1
    }

    // Попытка получить плитку под курсором мыши/касания
    private bool TryGetTileUnderPointer(out TileView tile)
    {
        tile = null;
        if (!TryGetPointerScreenPosition(out Vector2 screenPos)) // Получаем позицию курсора на экране
        {
            return false;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return false;
        }

        Vector3 world = cam.ScreenToWorldPoint(screenPos); // Преобразуем экранные координаты в мировые
        Collider2D hit = Physics2D.OverlapPoint(new Vector2(world.x, world.y)); // Проверяем коллайдер в этой точке
        if (hit == null)
        {
            return false;
        }

        tile = hit.GetComponent<TileView>(); // Пытаемся получить компонент TileView
        return tile != null;
    }

    // Получение экранной позиции указателя (работает с новым и старым вводом Unity)
    private static bool TryGetPointerScreenPosition(out Vector2 screenPos)
    {
#if ENABLE_INPUT_SYSTEM // Если используется новая система ввода
        if (Mouse.current != null)
        {
            screenPos = Mouse.current.position.ReadValue();
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER // Если используется старая система ввода
        screenPos = Input.mousePosition;
        return true;
#else
        screenPos = default;
        return false;
#endif
    }

    // Проверка, было ли нажатие в этом кадре
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

    // Проверка, удерживается ли кнопка
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

    // Проверка, было ли отпускание в этом кадре
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

    // Корутина попытки обмена и разрешения совпадений
    private IEnumerator TrySwapAndResolve(TileView a, TileView b)
    {
        isBusy = true; // Блокируем новые действия

        yield return StartCoroutine(AnimateSwap(a, b, swapDuration)); // Анимируем обмен
        SwapData(a, b); // Меняем данные в массивах

        bool[,] matched = FindMatches(); // Ищем совпадения
        if (!HasAnyMatch(matched)) // Если совпадений нет
        {
            // Отменяем обмен
            yield return StartCoroutine(AnimateSwap(a, b, swapDuration));
            SwapData(a, b);
            isBusy = false; // Разблокируем
            yield break;
        }

        // Если совпадения есть - разрешаем их
        yield return StartCoroutine(ResolveBoard());
        isBusy = false; // Разблокируем
    }

    // Корутина разрешения всех совпадений на поле (цикл пока есть совпадения)
    private IEnumerator ResolveBoard()
    {
        while (true)
        {
            bool[,] matched = FindMatches(); // Ищем совпадения
            if (!HasAnyMatch(matched)) // Если совпадений нет - выходим
            {
                yield break;
            }

            int cleared = ClearMatches(matched); // Удаляем совпавшие плитки
            score += cleared * 10; // Начисляем очки

            yield return StartCoroutine(CollapseColumns()); // Осаждаем плитки вниз
            yield return StartCoroutine(RefillBoard()); // Заполняем пустые места новыми плитками
        }
    }

    // Генерация начального поля без совпадений
    private void GenerateBoardWithoutStartingMatches()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int type = Random.Range(0, tileTypeCount); // Случайный тип

                // Предотвращаем начальные совпадения по горизонтали и вертикали (3 в ряд)
                while ((x >= 2 && tileTypes[x - 1, y] == type && tileTypes[x - 2, y] == type) ||
                       (y >= 2 && tileTypes[x, y - 1] == type && tileTypes[x, y - 2] == type))
                {
                    type = Random.Range(0, tileTypeCount); // Генерируем новый тип, пока не подойдет
                }

                CreateTile(x, y, type); // Создаем плитку
            }
        }
    }

    // Создание отдельной плитки
    private void CreateTile(int x, int y, int type)
    {
        tileTypes[x, y] = type; // Запоминаем тип

        GameObject go = new GameObject($"Tile_{x}_{y}"); // Создаем игровой объект
        go.transform.SetParent(transform); // Делаем дочерним объектом Match3Board
        go.transform.position = GridToWorld(x, y); // Устанавливаем позицию

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>(); // Добавляем рендерер
        sr.sprite = tileSprite; // Устанавливаем спрайт
        sr.color = palette[type]; // Устанавливаем цвет из палитры
        sr.sortingOrder = 10; // Порядок отрисовки (выше - поверх)

        BoxCollider2D col = go.AddComponent<BoxCollider2D>(); // Добавляем коллайдер для обнаружения кликов
        col.size = new Vector2(0.95f, 0.95f); // Чуть меньше размера для визуального зазора

        TileView tv = go.AddComponent<TileView>(); // Добавляем компонент представления
        tv.Setup(x, y, palette[type]); // Инициализируем с координатами и цветом

        tileViews[x, y] = tv; // Сохраняем в массив
    }

    // Анимация обмена двух плиток
    private IEnumerator AnimateSwap(TileView a, TileView b, float duration)
    {
        Vector3 aStart = a.transform.position; // Начальная позиция A
        Vector3 bStart = b.transform.position; // Начальная позиция B

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration); // Прогресс анимации (0-1)
            a.transform.position = Vector3.Lerp(aStart, bStart, k); // Плавно перемещаем A к B
            b.transform.position = Vector3.Lerp(bStart, aStart, k); // Плавно перемещаем B к A
            yield return null; // Ждем следующий кадр
        }

        a.transform.position = bStart; // Устанавливаем точные конечные позиции
        b.transform.position = aStart;
    }

    // Обмен данными между двумя плитками в массивах
    private void SwapData(TileView a, TileView b)
    {
        int ax = a.X;
        int ay = a.Y;
        int bx = b.X;
        int by = b.Y;

        // Обмениваем типы в массиве tileTypes
        (tileTypes[ax, ay], tileTypes[bx, by]) = (tileTypes[bx, by], tileTypes[ax, ay]);
        // Обмениваем ссылки в массиве tileViews
        (tileViews[ax, ay], tileViews[bx, by]) = (tileViews[bx, by], tileViews[ax, ay]);

        a.SetGridPosition(bx, by); // Обновляем координаты у плитки A
        b.SetGridPosition(ax, ay); // Обновляем координаты у плитки B
    }

    // Поиск всех совпадений на поле (3 и более в ряд)
    private bool[,] FindMatches()
    {
        bool[,] matched = new bool[width, height]; // Массив для отметки совпавших плиток

        // Поиск горизонтальных совпадений
        for (int y = 0; y < height; y++)
        {
            int runStart = 0;
            while (runStart < width)
            {
                int runType = tileTypes[runStart, y]; // Тип плитки в начале последовательности
                if (runType < 0) // Пустая клетка (после удаления)
                {
                    runStart++;
                    continue;
                }

                int runEnd = runStart + 1;
                while (runEnd < width && tileTypes[runEnd, y] == runType) // Ищем конец последовательности
                {
                    runEnd++;
                }

                if (runEnd - runStart >= 3) // Если длина последовательности 3 или больше
                {
                    for (int x = runStart; x < runEnd; x++) // Отмечаем все плитки в последовательности
                    {
                        matched[x, y] = true;
                    }
                }

                runStart = runEnd; // Продолжаем поиск
            }
        }

        // Поиск вертикальных совпадений (аналогично горизонтальным)
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

    // Проверка, есть ли хоть одно совпадение
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

    // Удаление совпавших плиток
    private int ClearMatches(bool[,] matched)
    {
        int cleared = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!matched[x, y]) // Если плитка не совпала - пропускаем
                {
                    continue;
                }

                cleared++; // Увеличиваем счетчик удаленных
                tileTypes[x, y] = -1; // Помечаем клетку как пустую (-1)
                Destroy(tileViews[x, y].gameObject); // Уничтожаем игровой объект
                tileViews[x, y] = null; // Убираем ссылку
            }
        }

        return cleared;
    }

    // Осаждение плиток вниз после удаления
    private IEnumerator CollapseColumns()
    {
        for (int x = 0; x < width; x++) // Для каждой колонки
        {
            int writeY = 0; // Позиция для записи (куда перемещаем)

            for (int y = 0; y < height; y++) // Проходим снизу вверх
            {
                if (tileTypes[x, y] < 0) // Если клетка пустая - пропускаем
                {
                    continue;
                }

                if (writeY != y) // Если текущая позиция отличается от позиции записи
                {
                    // Перемещаем плитку вниз
                    tileTypes[x, writeY] = tileTypes[x, y];
                    tileTypes[x, y] = -1;

                    tileViews[x, writeY] = tileViews[x, y];
                    tileViews[x, y] = null;

                    TileView moved = tileViews[x, writeY];
                    moved.SetGridPosition(x, writeY); // Обновляем координаты плитки
                }

                writeY++; // Увеличиваем позицию записи
            }
        }

        yield return StartCoroutine(AnimateBoardToGrid(fallDuration)); // Анимируем перемещение
    }

    // Заполнение пустых клеток новыми плитками сверху
    private IEnumerator RefillBoard()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (tileTypes[x, y] >= 0) // Если клетка не пустая - пропускаем
                {
                    continue;
                }

                int type = Random.Range(0, tileTypeCount); // Случайный тип
                tileTypes[x, y] = type; // Запоминаем тип

                // Создаем новую плитку над полем
                GameObject go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(transform);
                go.transform.position = GridToWorld(x, height + 1); // Позиция сверху (над полем)

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tileSprite;
                sr.color = palette[type];
                sr.sortingOrder = 10;

                BoxCollider2D col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(0.95f, 0.95f);

                TileView tv = go.AddComponent<TileView>();
                tv.Setup(x, y, palette[type]);
                tileViews[x, y] = tv; // Сохраняем в массив
            }
        }

        yield return StartCoroutine(AnimateBoardToGrid(fallDuration)); // Анимируем падение
    }

    // Анимация перемещения всех плиток на свои позиции в сетке
    private IEnumerator AnimateBoardToGrid(float duration)
    {
        List<TileView> allTiles = new List<TileView>(); // Список всех плиток
        List<Vector3> starts = new List<Vector3>(); // Их начальные позиции
        List<Vector3> targets = new List<Vector3>(); // Их целевые позиции

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
                targets.Add(GridToWorld(x, y)); // Цель в сетке
            }
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration); // Прогресс анимации

            for (int i = 0; i < allTiles.Count; i++) // Перемещаем все плитки
            {
                allTiles[i].transform.position = Vector3.Lerp(starts[i], targets[i], k);
            }

            yield return null; // Ждем следующий кадр
        }

        // Устанавливаем точные конечные позиции
        for (int i = 0; i < allTiles.Count; i++)
        {
            allTiles[i].transform.position = targets[i];
        }
    }

    // Преобразование координат сетки в мировые координаты
    private Vector3 GridToWorld(int x, int y)
    {
        float xOffset = (width - 1) * tileSpacing * 0.5f; // Смещение по X для центрирования поля
        float yOffset = (height - 1) * tileSpacing * 0.5f; // Смещение по Y для центрирования поля
        return new Vector3(x * tileSpacing - xOffset, y * tileSpacing - yOffset, 0f);
    }

    // Создание простого спрайта для плиток (пиксель 1x1)
    private Sprite CreateTileSprite()
    {
        Texture2D tex = new Texture2D(1, 1); // Создаем текстуру 1x1 пиксель
        tex.SetPixel(0, 0, Color.white); // Устанавливаем белый цвет
        tex.Apply(); // Применяем изменения
        tex.filterMode = FilterMode.Point; // Точечный фильтр (без сглаживания)
        tex.wrapMode = TextureWrapMode.Clamp; // Режим зацикливания текстур
        // Создаем спрайт с центром в середине (0.5, 0.5) и размером пикселя 1
        return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
    }

    // Настройка камеры для корректного отображения
    private void PrepareCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        cam.orthographic = true; // Ортографическая проекция (2D)
        cam.orthographicSize = 5.3f; // Размер области видимости
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.15f); // Темно-синий фон
        cam.transform.position = new Vector3(0f, 0f, -10f); // Позиция камеры (смотрит на поле)
    }
}
