#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using PinPlugin.Nexus;
using UnityEditor;
using UnityEngine;

namespace PinPlugin.Nexus.Editor
{
    /// <summary>
    /// Nexus 服務樹 Runtime 視覺化視窗：把 <see cref="Nexus.GetNodeSnapshot"/> 依 owner→child（ParentId）組成 foldout 樹，
    /// 即時確認當前作用中 context（<see cref="Nexus.Instance"/>）的 Global / Local 服務與建立中 pending。
    /// <para>預設手動 Refresh；勾 Auto 走 ~10Hz（OnInspectorUpdate）自動重抓，抓得到無事件的 pending 出現 / 取消。</para>
    /// <para>讀的是 <c>Nexus.Instance</c>——測試 scope（CreateScope）期間會顯示推入的 mock context。</para>
    /// </summary>
    public sealed class NexusServiceTreeWindow : EditorWindow
    {
        private const int MaxDepth = 64;   // 防禦：擁有邊理論上無環，仍設深度上限避免意外死迴圈

        private IReadOnlyList<NexusNode> _nodes = System.Array.Empty<NexusNode>();
        private ILookup<int, NexusNode> _childrenByParent;
        private readonly Dictionary<int, bool> _expanded = new();
        private Vector2 _scroll;
        private bool _auto;
        private bool _showId = true;
        private readonly SearchBar _searchBar = new();
        private int _rowIndex;   // zebra 計數，每次重畫歸零
        private int _selectedId; // 最近點選的節點 id（0 = 無）；UnityObject 與 POCO 皆送原生 Inspector（POCO 經 proxy）
        private Nexus _subscribed; // 目前已掛 OnLifecycle 的 Nexus 實例；Instance 在 PlayMode/scope 會換，需重掛
        private NexusPocoProxy _proxy; // 把 POCO 包成 ScriptableObject 送進原生 Inspector 的 proxy（lazy 建，DontSave，OnDisable 銷毀）

        private const float ToolbarHeight = 20f;          // 鍵盤捲動換算可視高度時扣掉的工具列高
        private readonly List<int> _visibleOrder = new(); // 本幀實際畫出的列順序（含過濾/展開），↑/↓ 鍵以此移動選取
        private float _rowSpacing; // 每幀自 pref 快取的列間距
        private readonly List<bool> _treeCont = new(); // 樹枝繪製：各祖代層是否仍有後續兄弟（決定畫虛線 ┊ 或空白），DrawNode 遞迴時 Add/RemoveAt 回溯
        private float _leftMargin; // 小固定左留白，避免列貼左邊
        private bool _scrollToSel; // 鍵盤移動選取後置位：下次 Repaint 把選取列捲入可視範圍
        private bool _firstCaptured; // 本幀是否已記錄第一列 y（用於把列 y 正規化成 content 座標）
        private float _firstRowY, _selRowY, _selRowH; // 第一列 / 選取列的版面位置，供 _scrollToSel 計算

        [MenuItem("Tools/Pin/Nexus/Service Tree")]
        private static void Open()
        {
            var w = GetWindow<NexusServiceTreeWindow>("Nexus Tree");
            w.minSize = new Vector2(320, 200);
            w.Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SubscribeLifecycle();
            Refresh();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnsubscribeLifecycle();
            if (_proxy != null) DestroyImmediate(_proxy);   // DontSave 物件不會自動回收，手動銷毀
        }

        // ~10Hz。只有勾 Auto 才重抓+重畫；否則維持手動快照（非 Auto 走 OnNexusLifecycle 事件驅動）。
        private void OnInspectorUpdate()
        {
            if (!_auto) return;
            Refresh();
            Repaint();
        }

        // 掛到當前 Nexus.Instance 的生命週期事件。Instance 會在 PlayMode（_default 被 ResetStatics 換新）
        // 或測試 scope（Push/Pop）時換實例，舊訂閱即失效，故記住已掛的實例、換了就重掛。
        private void SubscribeLifecycle()
        {
            var cur = Nexus.Instance;
            if (ReferenceEquals(cur, _subscribed)) return;
            if (_subscribed != null) _subscribed.OnLifecycle -= OnNexusLifecycle;
            _subscribed = cur;
            _subscribed.OnLifecycle += OnNexusLifecycle;
        }

        private void UnsubscribeLifecycle()
        {
            if (_subscribed != null) _subscribed.OnLifecycle -= OnNexusLifecycle;
            _subscribed = null;
        }

        // 進 / 出 PlayMode 時 _default 會被換掉（進場 ResetStatics 換新；退場 Nexus 的 EnteredEditMode hook 換新，
        // 連「Enter Play Mode without Domain Reload」也清乾淨）。重掛到當前 Instance 並立刻重抓+重畫，反映換新後的空圖。
        // 註：Nexus 退場重置的 hook 在 editor 載入時即訂閱（早於本視窗 OnEnable），故先於此 Refresh 跑、讀到的是清空後狀態。
        private void OnPlayModeChanged(PlayModeStateChange _)
        {
            SubscribeLifecycle();
            Refresh();
            Repaint();
        }

        // 非 Auto 模式下，Global/Local 新建立、釋放或回池（Created/Released/PoolReturned）即時重抓+重畫一次。
        // Auto 模式已 ~10Hz 輪詢，跳過避免雙重 Refresh。
        private void OnNexusLifecycle(NexusLifecyclePhase phase, System.Type type)
        {
            if (_auto) return;
            Refresh();
            Repaint();
        }

        private void Refresh()
        {
            _nodes = Nexus.Instance.GetNodeSnapshot();
            _childrenByParent = _nodes.ToLookup(n => n.ParentId);
        }

        private void OnGUI()
        {
            _showId = NexusTreePrefs.ShowId;   // Id 開關移到設定頁，每幀讀 pref 才能即時反映設定變更
            _rowSpacing = NexusTreePrefs.RowSpacing;
            _leftMargin = 4f;   // 小固定左留白，名稱貼近列首
            DrawToolbar();
            HandleKeyboardNav();   // 用上一幀的 _visibleOrder 處理 ↑/↓，再於下方重建本幀順序

            _visibleOrder.Clear();
            _firstCaptured = false;

            if (_nodes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "目前 Nexus.Instance 沒有作用中服務。\n進 Play Mode 並待服務建立後按 Refresh（或勾 Auto）。",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _rowIndex = 0;

            if (!_searchBar.IsEmpty)
                DrawFlatFiltered();
            else
            {
                _treeCont.Clear();
                var roots = _nodes.Where(n => n.IsGlobal).OrderBy(n => n.Id).ToList();
                // 虛擬頂層：roots 視為某不可見根的子代 → 最左欄（col0）成為「root 群」中軸，
                // root 之後仍有 root 時，其整棵子樹左側都畫續接虛線，使深層節點與 root 群對齊（多一層）。
                for (int i = 0; i < roots.Count; i++)
                {
                    _treeCont.Add(false);   // 佔位使 root 成為 line-depth 1（值不參與線條判定，col0 續接讀的是下一層）
                    DrawNode(roots[i], 1, i == roots.Count - 1);
                    _treeCont.RemoveAt(_treeCont.Count - 1);
                }
            }

            EditorGUILayout.EndScrollView();

            // 鍵盤移動後把選取列捲入可視範圍：列 y 以「選取列 - 第一列」正規化成 content 座標，免依賴 scrollview 原點。
            if (_scrollToSel && _firstCaptured && Event.current.type == EventType.Repaint)
            {
                float y = _selRowY - _firstRowY;
                float viewH = position.height - ToolbarHeight;
                if (y < _scroll.y) _scroll.y = y;
                else if (y + _selRowH > _scroll.y + viewH) _scroll.y = y + _selRowH - viewH;
                _scrollToSel = false;
                Repaint();
            }
        }

        // ↑/↓ 在本視窗 focus 時沿 _visibleOrder 移動選取；找不到目前選取時自邊界起跳。pending 列無實例仍可選取（只移高亮）。
        private void HandleKeyboardNav()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.DownArrow) return;
            if (_visibleOrder.Count == 0) return;

            bool down = e.keyCode == KeyCode.DownArrow;
            int idx = _visibleOrder.IndexOf(_selectedId);
            if (idx < 0) idx = down ? -1 : _visibleOrder.Count;   // 未選取：↓ 從頭、↑ 從尾
            idx = Mathf.Clamp(idx + (down ? 1 : -1), 0, _visibleOrder.Count - 1);

            _selectedId = _visibleOrder[idx];
            SelectInstance(_selectedId);   // live 實例送 Inspector；pending no-op，高亮已靠上面的 _selectedId
            _scrollToSel = true;
            e.Use();
            Repaint();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Refresh();

            var newAuto = GUILayout.Toggle(_auto, "Auto", EditorStyles.toolbarButton, GUILayout.Width(44));
            if (newAuto != _auto) { _auto = newAuto; if (_auto) Refresh(); }

            GUILayout.Space(8);
            var active = _nodes.Count(n => !n.IsPending);
            var pending = _nodes.Count - active;
            GUILayout.Label($"active {active}" + (pending > 0 ? $"  pending {pending}" : ""), EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            _searchBar.DrawToolbar();

            // Id 顯示等開關已移到設定頁，這裡直接開設定（同 Preferences/Pin Tools/Nexus Service Tree）
            if (GUILayout.Button("設定", EditorStyles.toolbarButton, GUILayout.Width(44)))
                NexusTreePrefs.OpenSettings();

            EditorGUILayout.EndHorizontal();
        }

        // 搜尋時走扁平過濾（型別名 / IdentityKey），不組樹。
        private void DrawFlatFiltered()
        {
            foreach (var n in _nodes
                         .Where(n => _searchBar.Matches(n.TypeName) || _searchBar.Matches(n.IdentityKey))
                         .OrderBy(n => n.IsGlobal ? 0 : 1).ThenBy(n => n.Id))
                DrawRow(n, foldout: false, expanded: false, isLast: true, drawTree: false);
        }

        private void DrawNode(NexusNode node, int depth, bool isLast)
        {
            if (depth > MaxDepth) return;

            var children = _childrenByParent[node.Id].OrderBy(c => c.Id).ToList();
            var hasChildren = children.Count > 0;

            var expanded = !_expanded.TryGetValue(node.Id, out var e) || e;   // 預設展開
            var newExpanded = DrawRow(node, foldout: hasChildren, expanded: expanded, isLast: isLast, drawTree: true);
            if (hasChildren && newExpanded != expanded) _expanded[node.Id] = newExpanded;

            if (hasChildren && newExpanded)
            {
                // 子代繪製前推入本節點該層的延續旗標：本節點非最後兄弟 → 子代列在此欄補 │，直連到下方兄弟
                _treeCont.Add(!isLast);
                for (int i = 0; i < children.Count; i++)
                    DrawNode(children[i], depth + 1, isLast: i == children.Count - 1);
                _treeCont.RemoveAt(_treeCont.Count - 1);
            }
        }

        // 回傳 foldout 展開狀態（無 foldout 時原樣回傳 expanded）。
        // 版面：[每層一格縮排（樹枝線 DrawRect 疊繪）][本節點箭頭▶/葉節點空] [G/L] TypeName('sub') [pending][container][prefab][pool] …flex… #Id（靠右）
        private bool DrawRow(NexusNode node, bool foldout, bool expanded, bool isLast, bool drawTree)
        {
            _visibleOrder.Add(node.Id);   // 記入本幀畫出的列順序，供 ↑/↓ 鍵移動

            var rowRect = EditorGUILayout.BeginHorizontal();
            // 列底色：選取 > zebra（奇數列）。Repaint 時用上一輪 layout 的 rowRect（IMGUI 慣用法）。
            int depth = drawTree ? _treeCont.Count : 0;
            if (Event.current.type == EventType.Repaint)
            {
                if (!_firstCaptured) { _firstRowY = rowRect.y; _firstCaptured = true; }   // 第一列基準，用於捲動正規化
                if (node.Id == _selectedId && _selectedId != 0)
                {
                    EditorGUI.DrawRect(rowRect, NexusTreePrefs.Selected);
                    _selRowY = rowRect.y; _selRowH = rowRect.height;   // 記錄選取列位置供鍵盤捲動
                }
                else if ((_rowIndex & 1) == 1)
                    EditorGUI.DrawRect(rowRect, NexusTreePrefs.Zebra);

                if (depth > 0) DrawTreeLines(rowRect, depth, foldout, isLast);   // 樹枝以 DrawRect 畫像素線（續接虛線 + 實線水平），免字型缺字/重疊
            }
            _rowIndex++;

            GUILayout.Space(_leftMargin);                       // 小固定左留白
            if (depth > 0) GUILayout.Space((depth - 1) * TreeCellWidth);   // 祖代欄縮排（本節點欄 = 下方 foldRect 那格）

            // 本節點欄（恆佔 14px = 最後一格）：container 畫 foldout 箭頭 ▼ 取代彎角；葉節點留空，由樹枝畫 ├/└ 彎角。
            bool result = expanded;
            var foldRect = GUILayoutUtility.GetRect(14f, 16f, GUILayout.Width(14f), GUILayout.Height(16f));
            if (foldout)
                result = EditorGUI.Foldout(foldRect, expanded, GUIContent.none, true, EditorStyles.foldout);

            // G/L 圖示（連接符後、名稱前）
            Badge(node.IsGlobal ? "G" : "L", node.IsGlobal ? NexusTreePrefs.GlobalBadge : NexusTreePrefs.LocalBadge);

            var sub = string.IsNullOrEmpty(node.IdentityKey) ? "" : $"('{node.IdentityKey}')";
            var prev = GUI.color;
            if (node.IsPending) GUI.color = NexusTreePrefs.PendingBadge;   // pending 名稱染色
            GUILayout.Label($"{node.TypeName}{sub}", EditorStyles.label);
            GUI.color = prev;

            DrawBadges(node);

            GUILayout.FlexibleSpace();

            // #Id 固定靠右：FlexibleSpace 後繪製 → 永遠貼列右緣，與樹深無關
            if (_showId)
            {
                var prevId = GUI.color;
                GUI.color = NexusTreePrefs.IdColor;
                GUILayout.Label($"#{node.Id}", EditorStyles.label, GUILayout.Width(40));
                GUI.color = prevId;
            }

            EditorGUILayout.EndHorizontal();

            if (_rowSpacing > 0f) GUILayout.Space(_rowSpacing);   // 列間距（pref）

            // 左鍵點列 → 送 Inspector（foldout 自己的 click 已 Use 事件，不會走到這）。
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && rowRect.Contains(Event.current.mousePosition))
            {
                SelectInstance(node.Id);
                Event.current.Use();
                Repaint();
            }
            return result;
        }

        // 取 live 實例（快照仍 ref-free），送原生 Inspector。UnityEngine.Object 直接選；
        // POCO（大宗純 C# 服務）包成 NexusPocoProxy（ScriptableObject）讓原生 Inspector 經 custom editor 反射顯示。
        // pending / 已釋放 → no-op。
        private void SelectInstance(int id)
        {
            var inst = Nexus.Instance.ById<object>(id);
            if (inst == null) return;   // pending / 已釋放：無實例可看

            _selectedId = id;
            if (inst is UnityEngine.Object uo)
            {
                Selection.activeObject = uo;
                EditorGUIUtility.PingObject(uo);
                return;
            }

            // POCO：proxy 只記 id（custom editor 每幀由 id 重抓 live，不持實例參考 → 釋放後自動顯示已釋放，不釘記憶體）。
            if (_proxy == null)
            {
                _proxy = ScriptableObject.CreateInstance<NexusPocoProxy>();
                _proxy.hideFlags = HideFlags.DontSave;
            }
            _proxy.Bind(id, $"{inst.GetType().Name} #{id}");
            Selection.activeObject = _proxy;
        }

        private static void DrawBadges(NexusNode node)
        {
            if (node.IsPending) Badge("pending", NexusTreePrefs.PendingBadge);
            if (node.IsContainer) Badge("container", NexusTreePrefs.ContainerBadge);
            if (node.IsPrefabMono) Badge("prefab", NexusTreePrefs.PrefabBadge);
            if (node.IsScriptable) Badge("so", NexusTreePrefs.ScriptableBadge);
            if (node.IsPoolable) Badge("pool", NexusTreePrefs.PoolBadge);
        }

        // 以 DrawRect 畫像素級樹枝（取代 box-drawing 字元：字型常缺 ┊ 虛線字元、且字元易與箭頭重疊）。
        // 一格 = cellW（= 14px）；第 k 欄中軸對齊深度 k 節點的本節點欄中心。
        //   祖代欄（k < depth-1）：該祖代之後仍有同級兄弟（_treeCont[k+1]）才畫整列虛線 ┊，否則留白。
        //   本節點欄（k = depth-1）：container 不畫線（由 foldout ▼ 占位取代彎角）；
        //     葉節點畫彎角——上半段 ┬ 接上層、實線水平接 G/L，!isLast 再補下半段成 ├（否則 └）。
        private void DrawTreeLines(Rect rowRect, int depth, bool foldout, bool isLast)
        {
            float cw = TreeCellWidth;
            float treeX = rowRect.x + _leftMargin;
            float midY = Mathf.Round(rowRect.y + rowRect.height * 0.5f);
            var c = NexusTreePrefs.IdColor;
            for (int k = 0; k < depth; k++)
            {
                float cx = Mathf.Round(treeX + k * cw + cw * 0.5f);
                if (k < depth - 1)
                {
                    if (_treeCont[k + 1]) DashedVLine(cx, rowRect.y, rowRect.yMax, c);   // 祖代欄：仍有後續兄弟才續接
                    continue;
                }
                if (foldout) continue;   // container 本節點欄：箭頭 ▼ 取代彎角，不畫線
                float vEnd = isLast ? midY : rowRect.yMax;                 // 末筆止於中軸（└）、否則整列（├）
                EditorGUI.DrawRect(new Rect(cx, rowRect.y, 1f, vEnd - rowRect.y), c);     // 彎角垂直：實線 ├ / └
                EditorGUI.DrawRect(new Rect(cx, midY, treeX + depth * cw - cx, 1f), c);   // 實線水平接到 G/L
            }
        }

        // 垂直虛線：2px 線段 + 2px 間隔，1px 寬。
        private static void DashedVLine(float x, float y0, float y1, Color c)
        {
            const float seg = 2f, gap = 2f;
            for (float y = y0; y < y1; y += seg + gap)
                EditorGUI.DrawRect(new Rect(x, y, 1f, Mathf.Min(seg, y1 - y)), c);
        }

        private const float TreeCellWidth = 14f;   // = foldout 箭頭寬，每層縮排一格、中軸對齊上層箭頭中心

        private static void Badge(string text, Color c)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = c;
            GUILayout.Label(text, EditorStyles.helpBox, GUILayout.Height(16));
            GUI.backgroundColor = prev;
        }
    }

    /// <summary>
    /// Nexus 服務樹視窗的視覺設定，存 <see cref="EditorPrefs"/>（per-machine，不進 VCS）。
    /// 色彩以 HTML RGBA 字串存（含 alpha，zebra 需要）。在 Preferences/Nexus Service Tree 頁編輯。
    /// </summary>
    internal static class NexusTreePrefs
    {
        private const string Prefix = "Nexus.Tree.";
        public const string SettingsPath = "Preferences/Pin Tools/Nexus Service Tree";

        /// <summary>從視窗工具列直接開本設定頁。</summary>
        public static void OpenSettings() => SettingsService.OpenUserPreferences(SettingsPath);

        // 預設值（與舊硬編一致）
        private static readonly Color DefZebra = new(1f, 1f, 1f, 0.04f);
        private static readonly Color DefSelected = new(0.24f, 0.48f, 0.90f, 0.45f);   // 選取列底色（半透明藍）
        private static readonly Color DefId = new(0.55f, 0.55f, 0.55f);
        private static readonly Color DefGlobal = new(0.4f, 0.7f, 1f);
        private static readonly Color DefLocal = new(0.6f, 0.85f, 0.5f);
        private static readonly Color DefPending = new(1f, 0.78f, 0.3f);
        private static readonly Color DefContainer = new(0.8f, 0.6f, 1f);
        private static readonly Color DefPrefab = new(1f, 0.6f, 0.6f);
        private static readonly Color DefScriptable = new(1f, 0.8f, 0.5f);
        private static readonly Color DefPool = new(0.6f, 0.8f, 0.8f);

        public static bool ShowId
        {
            get => EditorPrefs.GetBool(Prefix + "showId", true);
            set => EditorPrefs.SetBool(Prefix + "showId", value);
        }

        /// <summary>列與列之間的垂直間距（px）。預設 0（緊貼）。</summary>
        public static float RowSpacing
        {
            get => EditorPrefs.GetFloat(Prefix + "rowSpacing", 0f);
            set => EditorPrefs.SetFloat(Prefix + "rowSpacing", value);
        }

        public static Color Zebra { get => Get("zebra", DefZebra); set => Set("zebra", value); }
        public static Color Selected { get => Get("selected", DefSelected); set => Set("selected", value); }
        public static Color IdColor { get => Get("id", DefId); set => Set("id", value); }
        public static Color GlobalBadge { get => Get("global", DefGlobal); set => Set("global", value); }
        public static Color LocalBadge { get => Get("local", DefLocal); set => Set("local", value); }
        public static Color PendingBadge { get => Get("pending", DefPending); set => Set("pending", value); }
        public static Color ContainerBadge { get => Get("container", DefContainer); set => Set("container", value); }
        public static Color PrefabBadge { get => Get("prefab", DefPrefab); set => Set("prefab", value); }
        public static Color ScriptableBadge { get => Get("scriptable", DefScriptable); set => Set("scriptable", value); }
        public static Color PoolBadge { get => Get("pool", DefPool); set => Set("pool", value); }

        private static Color Get(string k, Color def)
            => ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString(Prefix + k, ""), out var c) ? c : def;

        private static void Set(string k, Color v)
            => EditorPrefs.SetString(Prefix + k, ColorUtility.ToHtmlStringRGBA(v));

        public static void ResetAll()
        {
            foreach (var k in new[] { "showId", "rowSpacing", "zebra", "selected", "id", "global", "local", "pending", "container", "prefab", "scriptable", "pool" })
                EditorPrefs.DeleteKey(Prefix + k);
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            // Preferences 下的 Pin Tools 分類（色彩為 per-machine 個人偏好，故走 User scope / EditorPrefs）。
            return new SettingsProvider(SettingsPath, SettingsScope.User)
            {
                label = "Nexus Service Tree",
                keywords = new[] { "nexus", "service", "tree", "color", "zebra", "badge", "id", "indent", "spacing", "間距", "縮排" },
                guiHandler = _ =>
                {
                    EditorGUILayout.LabelField("顯示", EditorStyles.boldLabel);
                    ShowId = EditorGUILayout.Toggle("顯示 Id 欄 (#)", ShowId);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("間距", EditorStyles.boldLabel);
                    RowSpacing = EditorGUILayout.Slider("列間距", RowSpacing, 0f, 12f);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("列", EditorStyles.boldLabel);
                    Zebra = EditorGUILayout.ColorField("奇數列疊色 (Zebra)", Zebra);
                    Selected = EditorGUILayout.ColorField("選取列底色", Selected);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("文字 / 圖示", EditorStyles.boldLabel);
                    IdColor = EditorGUILayout.ColorField("Id 文字", IdColor);
                    GlobalBadge = EditorGUILayout.ColorField("Global 圖示 (G)", GlobalBadge);
                    LocalBadge = EditorGUILayout.ColorField("Local 圖示 (L)", LocalBadge);
                    PendingBadge = EditorGUILayout.ColorField("Pending", PendingBadge);
                    ContainerBadge = EditorGUILayout.ColorField("Container", ContainerBadge);
                    PrefabBadge = EditorGUILayout.ColorField("Prefab", PrefabBadge);
                    ScriptableBadge = EditorGUILayout.ColorField("Scriptable", ScriptableBadge);
                    PoolBadge = EditorGUILayout.ColorField("Pool", PoolBadge);

                    EditorGUILayout.Space();
                    if (GUILayout.Button("重置為預設", GUILayout.Width(120)))
                        ResetAll();
                }
            };
        }
    }
}
#endif
