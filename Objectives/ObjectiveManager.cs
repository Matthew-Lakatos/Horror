using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Eidolon.Core;

namespace Eidolon.Objectives
{
    /// <summary>
    /// Manages the layered objective structure:
    ///   Primary   — required for progress (restore power, upload data, etc.)
    ///   Secondary — optional shortcuts, caches, lore logs
    ///   Optional  — high-risk anomaly directives with strong rewards
    /// Objectives move the player through evolving danger zones.
    /// </summary>
    public class ObjectiveManager : MonoBehaviour
    {
        public static ObjectiveManager Instance { get; private set; }

        [Header("Objective Definitions")]
        [SerializeField] private List<ObjectiveData> _primaryObjectives   = new List<ObjectiveData>();
        [SerializeField] private List<ObjectiveData> _secondaryObjectives = new List<ObjectiveData>();
        [SerializeField] private List<ObjectiveData> _optionalDirectives  = new List<ObjectiveData>();

        // ─── Runtime State ───────────────────────────────────────────────────

        private readonly Dictionary<string, ObjectiveInstance> _instances
            = new Dictionary<string, ObjectiveInstance>();

        public float NormalisedProgress { get; private set; }

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            InitialiseObjectives();
        }

        // ─── Initialisation ──────────────────────────────────────────────────

        private void InitialiseObjectives()
        {
            RegisterAll(_primaryObjectives,   ObjectiveTier.Primary);
            RegisterAll(_secondaryObjectives, ObjectiveTier.Secondary);
            RegisterAll(_optionalDirectives,  ObjectiveTier.Optional);

            // Activate first primary objective
            var first = _instances.Values.FirstOrDefault(i => i.Data.Tier == ObjectiveTier.Primary);
            if (first != null) Activate(first.Data.ObjectiveId);

            RecalculateProgress();
        }

        private void RegisterAll(List<ObjectiveData> data, ObjectiveTier tier)
        {
            foreach (var d in data)
            {
                if (d == null || string.IsNullOrEmpty(d.ObjectiveId)) continue;
                d.Tier = tier;
                _instances[d.ObjectiveId] = new ObjectiveInstance(d);
            }
        }

        // ─── Public API ──────────────────────────────────────────────────────

        public void Activate(string id)
        {
            if (!_instances.TryGetValue(id, out var inst)) return;
            if (inst.State != ObjectiveState.Inactive) return;

            inst.State = ObjectiveState.Active;
            EventBus.Publish(new ObjectiveActivatedEvent { ObjectiveId = id });
            Debug.Log($"[ObjectiveManager] Activated: {inst.Data.DisplayName}");
        }

        public void Complete(string id)
        {
            if (!_instances.TryGetValue(id, out var inst)) return;
            if (inst.State == ObjectiveState.Completed) return;

            inst.State = ObjectiveState.Completed;
            EventBus.Publish(new ObjectiveCompletedEvent { ObjectiveId = id });
            Debug.Log($"[ObjectiveManager] Completed: {inst.Data.DisplayName}");

            RecalculateProgress();
            ActivateNextPrimary(id);
        }

        public void Fail(string id)
        {
            if (!_instances.TryGetValue(id, out var inst)) return;
            inst.State = ObjectiveState.Failed;
        }

        // ─── Progress ────────────────────────────────────────────────────────

        private void RecalculateProgress()
        {
            var primaries = _instances.Values.Where(i => i.Data.Tier == ObjectiveTier.Primary).ToList();
            if (primaries.Count == 0) return;

            int completed = primaries.Count(i => i.State == ObjectiveState.Completed);
            NormalisedProgress = (float)completed / primaries.Count;

            GameDirector.Instance?.UpdateObjectiveProgress(NormalisedProgress);
        }

        private void ActivateNextPrimary(string completedId)
        {
            var ordered = _primaryObjectives
                .Where(d => d != null)
                .OrderBy(d => d.SortOrder)
                .ToList();

            int idx = ordered.FindIndex(d => d.ObjectiveId == completedId);
            if (idx < 0 || idx + 1 >= ordered.Count) return;

            Activate(ordered[idx + 1].ObjectiveId);
        }

        // ─── Queries ────────────────────────────────────────────────────────

        public ObjectiveInstance GetObjective(string id)
            => _instances.TryGetValue(id, out var inst) ? inst : null;

        public List<ObjectiveInstance> GetActive()
            => _instances.Values.Where(i => i.State == ObjectiveState.Active).ToList();

        public bool AllPrimariesComplete()
            => _instances.Values
                .Where(i => i.Data.Tier == ObjectiveTier.Primary)
                .All(i => i.State == ObjectiveState.Completed);

        // ─── Save/Load ───────────────────────────────────────────────────────

        public List<ObjectiveSaveEntry> ExportState()
        {
            return _instances.Select(kv => new ObjectiveSaveEntry
            {
                ObjectiveId = kv.Key,
                State       = kv.Value.State
            }).ToList();
        }

        public void ImportState(List<ObjectiveSaveEntry> data)
        {
            foreach (var entry in data)
                if (_instances.TryGetValue(entry.ObjectiveId, out var inst))
                    inst.State = entry.State;

            RecalculateProgress();
        }
    }

    // ─── Data Models ────────────────────────────────────────────────────────

    [CreateAssetMenu(menuName = "Eidolon/Objectives/ObjectiveData")]
    public class ObjectiveData : ScriptableObject
    {
        public string       ObjectiveId;
        public string       DisplayName;
        [TextArea] public string Description;
        public ObjectiveTier Tier;
        public int          SortOrder;
        public string       TargetRoomId;
        public string[]     RequiredItemIds;
        public string[]     UnlockOnComplete;  // IDs to activate on completion
        public bool         IsHighRisk;        // Optional directive flag
    }

    public class ObjectiveInstance
    {
        public ObjectiveData   Data;
        public ObjectiveState  State = ObjectiveState.Inactive;

        public ObjectiveInstance(ObjectiveData data) { Data = data; }
    }

    public enum ObjectiveTier  { Primary, Secondary, Optional }
    public enum ObjectiveState { Inactive, Active, Completed, Failed }

    [System.Serializable]
    public class ObjectiveSaveEntry
    {
        public string        ObjectiveId;
        public ObjectiveState State;
    }
}
