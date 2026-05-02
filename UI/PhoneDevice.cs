using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Eidolon.Core;
using Eidolon.Objectives;

namespace Eidolon.UI
{
    /// <summary>
    /// Diegetic player phone device.
    /// 
    /// Tabs: Tasks | Map | Messages | Evidence | Notes
    /// 
    /// Opened with Tab key. Slows time slightly while open (tension preserved).
    /// Populates automatically from ObjectiveManager, ResourceManager lore logs,
    /// and system messages pushed by game events.
    /// 
    /// Map tab renders the Building A room graph as a simple 2D schematic
    /// using the WorldStateManager data — visited rooms shown, unvisited dark.
    /// </summary>
    public class PhoneDevice : MonoBehaviour
    {
        public static PhoneDevice Instance { get; private set; }

        // ─── Canvas ──────────────────────────────────────────────────────────

        [Header("Root")]
        [SerializeField] private CanvasGroup _phoneCanvas;
        [SerializeField] private float       _openTimeScale = 0.6f; // slightly slowed, not paused

        // ─── Tab Buttons ─────────────────────────────────────────────────────

        [Header("Tab Buttons")]
        [SerializeField] private Button _tabTasks;
        [SerializeField] private Button _tabMap;
        [SerializeField] private Button _tabMessages;
        [SerializeField] private Button _tabEvidence;
        [SerializeField] private Button _tabNotes;

        [Header("Tab Panels")]
        [SerializeField] private GameObject _panelTasks;
        [SerializeField] private GameObject _panelMap;
        [SerializeField] private GameObject _panelMessages;
        [SerializeField] private GameObject _panelEvidence;
        [SerializeField] private GameObject _panelNotes;

        // ─── Tasks Tab ───────────────────────────────────────────────────────

        [Header("Tasks")]
        [SerializeField] private Transform   _tasksContainer;
        [SerializeField] private GameObject  _taskEntryPrefab;  // Text component expected

        // ─── Map Tab ─────────────────────────────────────────────────────────

        [Header("Map")]
        [SerializeField] private RectTransform _mapContainer;
        [SerializeField] private GameObject    _mapRoomNodePrefab;  // small square UI element
        [SerializeField] private GameObject    _mapConnectionPrefab; // thin line UI element
        [SerializeField] private Color         _visitedRoomColor   = new Color(0.6f, 0.9f, 0.6f);
        [SerializeField] private Color         _unvisitedRoomColor = new Color(0.2f, 0.2f, 0.2f);
        [SerializeField] private Color         _currentRoomColor   = new Color(1f,   0.9f, 0.2f);

        // Map layout positions — set in inspector to match greybox room positions
        // (2D screen positions corresponding to Building A room layout)
        [SerializeField] private List<MapRoomPin> _mapPins = new List<MapRoomPin>();

        // ─── Messages Tab ────────────────────────────────────────────────────

        [Header("Messages")]
        [SerializeField] private Transform   _messagesContainer;
        [SerializeField] private GameObject  _messageEntryPrefab;
        [SerializeField] private ScrollRect  _messagesScroll;

        // ─── Evidence Tab ────────────────────────────────────────────────────

        [Header("Evidence")]
        [SerializeField] private Transform   _evidenceContainer;
        [SerializeField] private GameObject  _evidenceEntryPrefab;

        // ─── Notes Tab ───────────────────────────────────────────────────────

        [Header("Notes")]
        [SerializeField] private Text        _notesText;
        [SerializeField] private string      _defaultNotes =
            "Government inspection — Eidolon Industrial.\n" +
            "Contract number: EIC-7741-INSP\n" +
            "Compensation: $84,000 on report completion.\n\n" +
            "Something is wrong here.";

        // ─── Runtime ────────────────────────────────────────────────────────

        private bool _isOpen;
        private PhoneTab _currentTab = PhoneTab.Tasks;

        private readonly List<string> _messages  = new List<string>();
        private readonly List<string> _evidenceLogs = new List<string>();

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (_phoneCanvas) _phoneCanvas.alpha = 0f;
            if (_notesText)   _notesText.text    = _defaultNotes;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<ObjectiveActivatedEvent>(OnObjectiveUpdated);
            EventBus.Subscribe<ObjectiveCompletedEvent>(OnObjectiveUpdated);
            EventBus.Subscribe<KeyItemAcquiredEvent>(OnKeyItemAcquired);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ObjectiveActivatedEvent>(OnObjectiveUpdated);
            EventBus.Unsubscribe<ObjectiveCompletedEvent>(OnObjectiveUpdated);
            EventBus.Unsubscribe<KeyItemAcquiredEvent>(OnKeyItemAcquired);
        }

        private void Start()
        {
            // Wire tab buttons
            _tabTasks?.onClick.AddListener(()    => SwitchTab(PhoneTab.Tasks));
            _tabMap?.onClick.AddListener(()      => SwitchTab(PhoneTab.Map));
            _tabMessages?.onClick.AddListener(() => SwitchTab(PhoneTab.Messages));
            _tabEvidence?.onClick.AddListener(() => SwitchTab(PhoneTab.Evidence));
            _tabNotes?.onClick.AddListener(()    => SwitchTab(PhoneTab.Notes));

            // Seed initial message
            AddSystemMessage($"CONTRACT ACTIVE — Eidolon Industrial Cognitive Facility\n" +
                             $"Report to entrance. Compensation: $84,000.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
                Toggle();
        }

        // ─── Open / Close ────────────────────────────────────────────────────

        public void Toggle()
        {
            if (_isOpen) Close(); else Open();
        }

        public void Open()
        {
            _isOpen = true;
            if (_phoneCanvas) _phoneCanvas.alpha = 1f;
            Time.timeScale = _openTimeScale;
            SwitchTab(_currentTab);
        }

        public void Close()
        {
            _isOpen = false;
            if (_phoneCanvas) _phoneCanvas.alpha = 0f;
            Time.timeScale = 1f;
        }

        // ─── Tab Switching ───────────────────────────────────────────────────

        private void SwitchTab(PhoneTab tab)
        {
            _currentTab = tab;

            if (_panelTasks)    _panelTasks.SetActive(tab    == PhoneTab.Tasks);
            if (_panelMap)      _panelMap.SetActive(tab      == PhoneTab.Map);
            if (_panelMessages) _panelMessages.SetActive(tab == PhoneTab.Messages);
            if (_panelEvidence) _panelEvidence.SetActive(tab == PhoneTab.Evidence);
            if (_panelNotes)    _panelNotes.SetActive(tab    == PhoneTab.Notes);

            switch (tab)
            {
                case PhoneTab.Tasks:    RefreshTasks();    break;
                case PhoneTab.Map:      RefreshMap();      break;
                case PhoneTab.Messages: RefreshMessages(); break;
                case PhoneTab.Evidence: RefreshEvidence(); break;
            }
        }

        // ─── Tasks ───────────────────────────────────────────────────────────

        private void RefreshTasks()
        {
            if (_tasksContainer == null || _taskEntryPrefab == null) return;

            // Clear existing
            foreach (Transform child in _tasksContainer)
                Destroy(child.gameObject);

            var activeObjectives = ObjectiveManager.Instance?.GetActive();
            if (activeObjectives == null) return;

            foreach (var obj in activeObjectives)
            {
                var go  = Instantiate(_taskEntryPrefab, _tasksContainer);
                var txt = go.GetComponentInChildren<Text>();
                if (txt == null) continue;

                string prefix = obj.Data.Tier == ObjectiveTier.Primary ? "▶ " : "  ◦ ";
                txt.text  = $"{prefix}{obj.Data.DisplayName}";
                txt.color = obj.Data.Tier == ObjectiveTier.Primary
                    ? new Color(1f, 0.9f, 0.3f)
                    : new Color(0.8f, 0.8f, 0.8f);
            }
        }

        // ─── Map ─────────────────────────────────────────────────────────────

        private void RefreshMap()
        {
            if (_mapContainer == null) return;

            foreach (Transform child in _mapContainer)
                Destroy(child.gameObject);

            var worldMgr = WorldStateManager.Instance;
            if (worldMgr == null) return;

            // Track which player room is current
            var player = Actors.PlayerController.Instance;

            // Draw connections first (behind nodes)
            foreach (var pin in _mapPins)
            {
                if (pin == null) continue;
                foreach (var neighbourId in worldMgr.GetNeighbours(pin.RoomId))
                {
                    var neighbourPin = _mapPins.Find(p => p != null && p.RoomId == neighbourId);
                    if (neighbourPin == null) continue;

                    // Only draw each connection once
                    if (string.Compare(pin.RoomId, neighbourId, System.StringComparison.Ordinal) > 0)
                        continue;

                    DrawMapLine(pin.MapPosition, neighbourPin.MapPosition);
                }
            }

            // Draw room nodes on top
            foreach (var pin in _mapPins)
            {
                if (pin == null) continue;

                int visits    = worldMgr.GetVisitCount(pin.RoomId);
                bool visited  = visits > 0;
                bool isCurrent = IsPlayerInRoom(pin.RoomId);

                DrawMapNode(pin, visited, isCurrent);
            }
        }

        private void DrawMapNode(MapRoomPin pin, bool visited, bool isCurrent)
        {
            if (_mapRoomNodePrefab == null) return;
            var go  = Instantiate(_mapRoomNodePrefab, _mapContainer);
            var rt  = go.GetComponent<RectTransform>();
            var img = go.GetComponent<Image>();
            var lbl = go.GetComponentInChildren<Text>();

            if (rt)  rt.anchoredPosition = pin.MapPosition;
            if (img) img.color = isCurrent  ? _currentRoomColor
                               : visited    ? _visitedRoomColor
                               : _unvisitedRoomColor;
            if (lbl) lbl.text  = visited ? pin.ShortLabel : "?";
        }

        private void DrawMapLine(Vector2 from, Vector2 to)
        {
            if (_mapConnectionPrefab == null) return;
            var go = Instantiate(_mapConnectionPrefab, _mapContainer);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;

            Vector2 dir    = to - from;
            float   length = dir.magnitude;
            float   angle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            rt.anchoredPosition = from + dir * 0.5f;
            rt.sizeDelta        = new Vector2(length, 2f);
            rt.localEulerAngles = new Vector3(0, 0, angle);
        }

        private bool IsPlayerInRoom(string roomId)
        {
            // PlayerController._currentRoomId is private — use WorldStateManager heatmap
            // as proxy: if this room has been visited this session AND no other room
            // has been visited more recently, it's likely current.
            // For production, expose _currentRoomId via public property on PlayerController.
            return false; // placeholder — wire up when PlayerController._currentRoomId is public
        }

        // ─── Messages ────────────────────────────────────────────────────────

        public void AddSystemMessage(string message)
        {
            _messages.Add($"[SYSTEM]  {message}");
            if (_isOpen && _currentTab == PhoneTab.Messages)
                RefreshMessages();
        }

        public void AddContractMessage(string message)
        {
            _messages.Add($"[CONTRACT]  {message}");
            if (_isOpen && _currentTab == PhoneTab.Messages)
                RefreshMessages();
        }

        private void RefreshMessages()
        {
            if (_messagesContainer == null || _messageEntryPrefab == null) return;
            foreach (Transform child in _messagesContainer) Destroy(child.gameObject);

            foreach (var msg in _messages)
            {
                var go  = Instantiate(_messageEntryPrefab, _messagesContainer);
                var txt = go.GetComponentInChildren<Text>();
                if (txt) txt.text = msg;
            }

            // Scroll to bottom
            if (_messagesScroll != null)
                Canvas.ForceUpdateCanvases();
        }

        // ─── Evidence ────────────────────────────────────────────────────────

        public void AddEvidence(string description)
        {
            _evidenceLogs.Add(description);
            if (_isOpen && _currentTab == PhoneTab.Evidence)
                RefreshEvidence();
        }

        private void RefreshEvidence()
        {
            if (_evidenceContainer == null || _evidenceEntryPrefab == null) return;
            foreach (Transform child in _evidenceContainer) Destroy(child.gameObject);

            foreach (var evidence in _evidenceLogs)
            {
                var go  = Instantiate(_evidenceEntryPrefab, _evidenceContainer);
                var txt = go.GetComponentInChildren<Text>();
                if (txt) txt.text = evidence;
            }
        }

        // ─── Event Handlers ──────────────────────────────────────────────────

        private void OnObjectiveUpdated(ObjectiveActivatedEvent evt)  => RefreshTasks();
        private void OnObjectiveUpdated(ObjectiveCompletedEvent evt)
        {
            RefreshTasks();
            AddSystemMessage($"Task complete: {ObjectiveManager.Instance?.GetObjective(evt.ObjectiveId)?.Data.DisplayName}");
        }

        private void OnKeyItemAcquired(KeyItemAcquiredEvent evt)
        {
            AddEvidence($"Item acquired: {evt.ItemType}  [ID: {evt.ItemId}]");
        }

        // ─── Save / Load ─────────────────────────────────────────────────────

        public PhoneSaveData ExportState() => new PhoneSaveData
        {
            Messages  = new List<string>(_messages),
            Evidence  = new List<string>(_evidenceLogs)
        };

        public void ImportState(PhoneSaveData data)
        {
            _messages.Clear();
            _messages.AddRange(data.Messages ?? new List<string>());
            _evidenceLogs.Clear();
            _evidenceLogs.AddRange(data.Evidence ?? new List<string>());
        }
    }

    // ─── Supporting Types ────────────────────────────────────────────────────

    public enum PhoneTab { Tasks, Map, Messages, Evidence, Notes }

    [System.Serializable]
    public class MapRoomPin
    {
        public string  RoomId;
        public string  ShortLabel;   // e.g. "HUB", "MED", "SAFE"
        public Vector2 MapPosition;  // Anchored position on map panel (pixels)
    }

    [System.Serializable]
    public class PhoneSaveData
    {
        public List<string> Messages;
        public List<string> Evidence;
    }
}
