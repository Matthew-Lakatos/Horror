using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Manages the full objective stack: primary, secondary, and optional directives.
    /// Drives phase transition checks after each completion.
    /// Objectives move the player through evolving danger zones by design.
    /// </summary>
    public class ObjectiveManager : MonoBehaviour, ISaveable
    {
        public static ObjectiveManager Instance { get; private set; }

        // ── State ──────────────────────────────────────────────────────────────
        private readonly List<IObjective> _primaries  = new();
        private readonly List<IObjective> _secondaries = new();
        private readonly List<IObjective> _optionals   = new();

        public IReadOnlyList<IObjective> Primaries   => _primaries;
        public IReadOnlyList<IObjective> Secondaries => _secondaries;
        public IReadOnlyList<IObjective> Optionals   => _optionals;

        // The room ID that the current primary objective requires the player to reach
        public string CurrentTargetRoomId { get; private set; }

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            TickAll(_primaries);
            TickAll(_secondaries);
            TickAll(_optionals);
        }

        private void TickAll(List<IObjective> list)
        {
            foreach (var obj in list)
            {
                if (!obj.IsActive || obj.IsComplete) continue;
                obj.Tick(Time.deltaTime);
                if (obj.IsComplete)
                    OnObjectiveComplete(obj);
            }
        }

        // ── Registration ───────────────────────────────────────────────────────
        public void AddPrimary  (IObjective obj) { _primaries.Add(obj);   obj.Activate(); EventBus.Publish(new EvObjectiveActivated(obj.ObjectiveId)); }
        public void AddSecondary(IObjective obj) { _secondaries.Add(obj); obj.Activate(); EventBus.Publish(new EvObjectiveActivated(obj.ObjectiveId)); }
        public void AddOptional (IObjective obj) { _optionals.Add(obj);   obj.Activate(); EventBus.Publish(new EvObjectiveActivated(obj.ObjectiveId)); }

        // ── Completion ─────────────────────────────────────────────────────────
        private void OnObjectiveComplete(IObjective obj)
        {
            EventBus.Publish(new EvObjectiveCompleted(obj.ObjectiveId));
            float progress = GetTotalProgress();
            GameDirector.Instance?.CheckPhaseTransition(progress);
            UpdateTargetRoom();
        }

        // ── Progress ───────────────────────────────────────────────────────────
        public float GetTotalProgress()
        {
            if (_primaries.Count == 0) return 0f;

            int    total     = _primaries.Count;
            float  completed = 0f;

            foreach (var p in _primaries)
                completed += p.IsComplete ? 1f : p.Progress;

            return completed / total;
        }

        public bool AllPrimariesComplete()
        {
            foreach (var p in _primaries)
                if (!p.IsComplete) return false;
            return true;
        }

        // ── Target room update ─────────────────────────────────────────────────
        private void UpdateTargetRoom()
        {
            foreach (var p in _primaries)
            {
                if (!p.IsComplete && p is RoomObjective ro)
                {
                    CurrentTargetRoomId = ro.TargetRoomId;
                    return;
                }
            }
            CurrentTargetRoomId = null;
        }

        // ── ISaveable ──────────────────────────────────────────────────────────
        public string SaveKey => "Objectives";

        public object CaptureState()
        {
            var data = new ObjectiveSaveData();
            void CaptureList(List<IObjective> list, List<ObjectiveEntry> target)
            {
                foreach (var o in list)
                    target.Add(new ObjectiveEntry
                    {
                        Id       = o.ObjectiveId,
                        Complete = o.IsComplete,
                        Progress = o.Progress
                    });
            }
            CaptureList(_primaries,   data.Primaries);
            CaptureList(_secondaries, data.Secondaries);
            CaptureList(_optionals,   data.Optionals);
            return data;
        }

        public void RestoreState(object raw)
        {
            // Objectives are re-built from scene data on load.
            // RestoreState marks completion flags only.
            if (raw is not ObjectiveSaveData data) return;
            RestoreList(data.Primaries,   _primaries);
            RestoreList(data.Secondaries, _secondaries);
            RestoreList(data.Optionals,   _optionals);
            UpdateTargetRoom();
        }

        private void RestoreList(List<ObjectiveEntry> entries, List<IObjective> targets)
        {
            foreach (var entry in entries)
            {
                var match = targets.Find(o => o.ObjectiveId == entry.Id);
                if (match is RestoredObjective ro)
                    ro.SetRestoredState(entry.Complete, entry.Progress);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  OBJECTIVE IMPLEMENTATIONS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Room-based objective: reach a room and perform an interaction N times.
    /// </summary>
    public class RoomObjective : IObjective
    {
        public string ObjectiveId  { get; }
        public string DisplayName  { get; }
        public bool   IsComplete   { get; private set; }
        public bool   IsActive     { get; private set; }
        public float  Progress     => _interactionsRequired > 0
                                       ? (float)_interactionsDone / _interactionsRequired
                                       : IsComplete ? 1f : 0f;

        public string TargetRoomId { get; }

        private readonly int  _interactionsRequired;
        private int           _interactionsDone;

        public RoomObjective(string id, string displayName, string targetRoomId, int interactions = 1)
        {
            ObjectiveId           = id;
            DisplayName           = displayName;
            TargetRoomId          = targetRoomId;
            _interactionsRequired = interactions;
        }

        public void Activate()
        {
            IsActive = true;
            EventBus.Subscribe<EvRoomEntered>(OnRoomEntered);
        }

        public void Tick(float dt) { }

        // ── Called by interactive objects in scene ─────────────────────────────
        public void RegisterInteraction()
        {
            if (!IsActive || IsComplete) return;
            _interactionsDone++;
            if (_interactionsDone >= _interactionsRequired)
                Complete();
        }

        private void OnRoomEntered(EvRoomEntered evt)
        {
            if (evt.RoomId == TargetRoomId && _interactionsRequired == 0)
                Complete();
        }

        private void Complete()
        {
            IsComplete = true;
            EventBus.Unsubscribe<EvRoomEntered>(OnRoomEntered);
        }
    }

    /// <summary>
    /// Item-collection objective: find and pick up required items.
    /// </summary>
    public class CollectObjective : IObjective
    {
        public string ObjectiveId  { get; }
        public string DisplayName  { get; }
        public bool   IsComplete   { get; private set; }
        public bool   IsActive     { get; private set; }
        public float  Progress     => _collected.Count / (float)_requiredItems.Count;

        private readonly List<string> _requiredItems;
        private readonly HashSet<string> _collected = new();
        private Actors.PlayerController _player;

        public CollectObjective(string id, string displayName, List<string> requiredItems)
        {
            ObjectiveId    = id;
            DisplayName    = displayName;
            _requiredItems = requiredItems;
        }

        public void Activate()
        {
            IsActive = true;
            _player  = Object.FindObjectOfType<Actors.PlayerController>();
        }

        public void Tick(float dt)
        {
            if (_player == null) return;
            foreach (var item in _requiredItems)
            {
                if (!_collected.Contains(item) && _player.HasItem(item))
                {
                    _collected.Add(item);
                    if (_collected.Count >= _requiredItems.Count)
                        IsComplete = true;
                }
            }
        }
    }

    /// <summary>Placeholder used during save restore.</summary>
    public class RestoredObjective : IObjective
    {
        public string ObjectiveId  { get; }
        public string DisplayName  { get; }
        public bool   IsComplete   { get; private set; }
        public bool   IsActive     => true;
        public float  Progress     { get; private set; }

        public RestoredObjective(string id) { ObjectiveId = id; DisplayName = id; }
        public void Activate()  { }
        public void Tick(float dt) { }
        public void SetRestoredState(bool complete, float progress)
        {
            IsComplete = complete;
            Progress   = progress;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SAVE DATA
    // ─────────────────────────────────────────────────────────────────────────
    [System.Serializable]
    public class ObjectiveSaveData
    {
        public List<ObjectiveEntry> Primaries   = new();
        public List<ObjectiveEntry> Secondaries = new();
        public List<ObjectiveEntry> Optionals   = new();
    }

    [System.Serializable]
    public class ObjectiveEntry
    {
        public string Id;
        public bool   Complete;
        public float  Progress;
    }
}
