using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.World
{
    using Core;

    // ─────────────────────────────────────────────────────────────────────────
    //  LORE ENTRY DATA  (ScriptableObject)
    // ─────────────────────────────────────────────────────────────────────────
    [CreateAssetMenu(menuName = "Eidolon/Lore Entry", fileName = "LoreEntry")]
    public class LoreEntry : ScriptableObject
    {
        public string EntryId;
        public string Title;

        [Tooltip("Document type affects visual presentation.")]
        public LoreType Type;

        [TextArea(3, 20)]
        public string Body;

        [Tooltip("Contradicts or reinforces another entry. Leave blank if standalone.")]
        public string ContradictsEntryId;

        [Tooltip("If true, this entry slightly increases player stress on read – bad news.")]
        public bool IsDistressing;
    }

    public enum LoreType
    {
        EmployeeLog,
        ResearchNote,
        InspectorNote,
        CCTV,
        SystemMessage,
        TestChamberRecord,
        SealedSectorFile
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LORE TERMINAL  – scene interactable that reveals a lore entry
    // ─────────────────────────────────────────────────────────────────────────
    public class LoreTerminal : MonoBehaviour, IFacilityInteractive
    {
        [SerializeField] private LoreEntry _entry;
        [SerializeField] private string    _roomId;

        public string InteractiveId => $"Lore_{_entry?.EntryId}";
        public bool   CanInteract   { get; private set; } = true;

        public void Interact(Actors.PlayerController player)
        {
            if (!CanInteract || _entry == null) return;

            LoreManager.Instance?.RevealEntry(_entry, player);

            // Mark as read
            CanInteract = false;
        }

        public void ForceState(bool open) => CanInteract = open;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LORE MANAGER
    // ─────────────────────────────────────────────────────────────────────────
    public class LoreManager : MonoBehaviour, ISaveable
    {
        public static LoreManager Instance { get; private set; }

        private readonly HashSet<string>        _readEntries    = new();
        private readonly List<LoreEntry>        _journal        = new();

        // UI hook – assign in scene
        [SerializeField] private UI.LoreDisplayUI _displayUI;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void RevealEntry(LoreEntry entry, Actors.PlayerController player)
        {
            if (_readEntries.Contains(entry.EntryId)) return;

            _readEntries.Add(entry.EntryId);
            _journal.Add(entry);

            // Show in UI
            _displayUI?.Show(entry);

            // Stress bump for distressing content
            if (entry.IsDistressing)
                PerceptionManager.Instance?.AddStress(0.06f);

            // Objective hook – some archives are primary objectives
            var om = ObjectiveManager.Instance;
            if (om != null)
                foreach (var obj in om.Primaries)
                    if (obj is CollectObjective co && player.HasItem(entry.EntryId))
                        { /* handled by CollectObjective tick */ }

            AudioManager.Instance?.PlayUI("audio_document_read");
        }

        public bool HasRead(string entryId) => _readEntries.Contains(entryId);

        public IReadOnlyList<LoreEntry> Journal => _journal;

        // ── ISaveable ──────────────────────────────────────────────────────────
        public string SaveKey => "Lore";

        public object CaptureState()
            => new List<string>(_readEntries);

        public void RestoreState(object raw)
        {
            if (raw is not List<string> ids) return;
            _readEntries.Clear();
            foreach (var id in ids) _readEntries.Add(id);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  LORE DISPLAY UI – minimal UI stub (wire to actual panel in scene)
// ─────────────────────────────────────────────────────────────────────────────
namespace Eidolon.UI
{
    using World;

    public class LoreDisplayUI : UnityEngine.MonoBehaviour
    {
        [UnityEngine.SerializeField] private UnityEngine.UI.Text  _titleText;
        [UnityEngine.SerializeField] private UnityEngine.UI.Text  _bodyText;
        [UnityEngine.SerializeField] private UnityEngine.UI.Text  _typeLabel;
        [UnityEngine.SerializeField] private UnityEngine.CanvasGroup _panel;
        [UnityEngine.SerializeField] private float _displayDuration = 10f;

        private System.Collections.IEnumerator _hideRoutine;

        public void Show(LoreEntry entry)
        {
            if (_titleText) _titleText.text = entry.Title;
            if (_bodyText)  _bodyText.text  = entry.Body;
            if (_typeLabel) _typeLabel.text = entry.Type.ToString().ToUpper();
            if (_panel)     _panel.alpha     = 1f;

            if (_hideRoutine != null) StopCoroutine(_hideRoutine);
            _hideRoutine = HideAfter(_displayDuration);
            StartCoroutine(_hideRoutine);
        }

        private System.Collections.IEnumerator HideAfter(float delay)
        {
            yield return new UnityEngine.WaitForSeconds(delay);
            if (_panel) _panel.alpha = 0f;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HUD MANAGER – room labels, health/stamina bars, objective tracker
    // ─────────────────────────────────────────────────────────────────────────
    public class HUDManager : UnityEngine.MonoBehaviour
    {
        [UnityEngine.SerializeField] private UnityEngine.UI.Slider _healthBar;
        [UnityEngine.SerializeField] private UnityEngine.UI.Slider _staminaBar;
        [UnityEngine.SerializeField] private UnityEngine.UI.Slider _batteryBar;
        [UnityEngine.SerializeField] private UnityEngine.UI.Text   _roomLabel;
        [UnityEngine.SerializeField] private UnityEngine.UI.Text   _objectiveText;
        [UnityEngine.SerializeField] private float                 _labelCorruptDuration = 3f;

        private Actors.PlayerController  _player;
        private string                   _trueRoomLabel;
        private bool                     _labelCorrupted;

        private void Start()
        {
            _player = UnityEngine.Object.FindObjectOfType<Actors.PlayerController>();
        }

        private void OnEnable()
        {
            Core.EventBus.Subscribe<Core.EvPerceptionEvent>(OnPerceptionEvent);
            Core.EventBus.Subscribe<Core.EvRoomEntered>   (OnRoomEntered);
        }

        private void OnDisable()
        {
            Core.EventBus.Unsubscribe<Core.EvPerceptionEvent>(OnPerceptionEvent);
            Core.EventBus.Unsubscribe<Core.EvRoomEntered>   (OnRoomEntered);
        }

        private void Update()
        {
            if (_player == null) return;

            if (_healthBar)  _healthBar.value  = _player.HealthNormalised;
            if (_staminaBar) _staminaBar.value = _player.StaminaNormalised;
            if (_batteryBar) _batteryBar.value = _player.BatteryNormalised;

            UpdateObjectiveText();
        }

        private void OnRoomEntered(Core.EvRoomEntered evt)
        {
            var graph = World.FacilityGraphManager.Instance?.Graph;
            var node  = graph?.GetNode(evt.RoomId);
            _trueRoomLabel = node?.Label ?? evt.RoomId;

            if (!_labelCorrupted && _roomLabel)
                _roomLabel.text = _trueRoomLabel;
        }

        private void OnPerceptionEvent(Core.EvPerceptionEvent evt)
        {
            if (evt.Type == Core.PerceptionEventType.WrongRoomLabel && _roomLabel)
                StartCoroutine(CorruptLabel());
        }

        private System.Collections.IEnumerator CorruptLabel()
        {
            _labelCorrupted = true;
            string[] fakes = { "SECTOR-7", "UNKNOWN", "B-WING", "???", "LOADING BAY" };
            _roomLabel.text = fakes[UnityEngine.Random.Range(0, fakes.Length)];
            yield return new UnityEngine.WaitForSeconds(_labelCorruptDuration);
            _roomLabel.text = _trueRoomLabel;
            _labelCorrupted = false;
        }

        private void UpdateObjectiveText()
        {
            var om = Core.ObjectiveManager.Instance;
            if (om == null || _objectiveText == null) return;

            var sb = new System.Text.StringBuilder();
            foreach (var p in om.Primaries)
                if (!p.IsComplete)
                {
                    sb.AppendLine($"▶ {p.DisplayName} ({p.Progress*100:F0}%)");
                    break;
                }
            _objectiveText.text = sb.ToString();
        }
    }
}
