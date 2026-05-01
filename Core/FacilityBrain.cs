using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Eidolon.World;

namespace Eidolon.Core
{
    /// <summary>
    /// The Facility Brain — the Industrial Machine manifesting as environmental antagonist.
    /// Strategically manipulates the campus to pressure the player.
    /// Learns hot routes and targets them. Always preserves at least one viable path.
    /// Registered as an EnemyActor of type FacilityBrain with a very slow tick.
    /// </summary>
    public class FacilityBrain : MonoBehaviour
    {
        public static FacilityBrain Instance { get; private set; }

        [Header("Action Budgets (per tick)")]
        [SerializeField] private int _maxActionsPerTick = 2;
        [SerializeField] private float _actionCooldownBase = 8f;

        [Header("Learning Parameters")]
        [SerializeField] private int   _hotRouteTopN        = 3;
        [SerializeField] private int   _visitsBeforeTargeting = 3;
        [SerializeField] private float _escalationRate      = 0.05f; // per tick

        [Header("References")]
        [SerializeField] private List<DoorController>  _allDoors  = new List<DoorController>();
        [SerializeField] private List<LiftController>  _allLifts  = new List<LiftController>();
        [SerializeField] private List<MedStation>      _allMedStations = new List<MedStation>();

        // ─── State ──────────────────────────────────────────────────────────

        private float _hostilityLevel; // 0-1, increases over time
        private float _lastActionTime;
        private readonly Dictionary<FacilityActionType, float> _actionCooldowns
            = new Dictionary<FacilityActionType, float>();

        // Geometry variants currently swapped
        private readonly HashSet<string> _swappedGeometryRooms = new HashSet<string>();
        private readonly HashSet<string> _corruptedLabelRooms  = new HashSet<string>();

        // Signage misdirection: false label swaps
        private readonly Dictionary<string, string> _falseLabels = new Dictionary<string, string>();

        public float HostilityLevel => _hostilityLevel;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<TensionStateChangedEvent>(OnTensionChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<TensionStateChangedEvent>(OnTensionChanged);
        }

        // Called by AIManager on the slow facility tick interval
        public void Tick()
        {
            _hostilityLevel = Mathf.Clamp01(_hostilityLevel + _escalationRate);

            SelectAndExecuteActions();
        }

        // ─── Action Selection ────────────────────────────────────────────────

        private void SelectAndExecuteActions()
        {
            if (Time.time - _lastActionTime < _actionCooldownBase) return;

            var candidates = BuildActionCandidates();
            candidates = candidates.OrderByDescending(a => a.Priority).ToList();

            int executed = 0;
            foreach (var action in candidates)
            {
                if (executed >= _maxActionsPerTick) break;
                if (IsOnCooldown(action.ActionType)) continue;
                if (!FairnessValidator.Instance.QuickValidate(
                        FairnessThreatType.GeometryShift,
                        hadWarning: action.HasWarning,
                        hasCounterplay: action.HasCounterplay)) continue;

                ExecuteAction(action);
                SetCooldown(action.ActionType, action.Cooldown);
                executed++;
            }

            if (executed > 0)
                _lastActionTime = Time.time;
        }

        private List<FacilityAction> BuildActionCandidates()
        {
            var actions = new List<FacilityAction>();
            var hotRooms = WorldStateManager.Instance?.GetMostVisitedRooms(_hotRouteTopN)
                           ?? new List<string>();

            // Target hot routes
            foreach (var roomId in hotRooms)
            {
                int visits = WorldStateManager.Instance?.GetVisitCount(roomId) ?? 0;
                if (visits < _visitsBeforeTargeting) continue;

                var room = WorldStateManager.Instance?.GetRoom(roomId);
                if (room == null) continue;

                // Dim lights on overused rooms
                if (room.IsLit && _hostilityLevel > 0.3f)
                    actions.Add(new FacilityAction(FacilityActionType.DimLights, roomId, priority: 3f, cooldown: 30f, hasWarning: true, hasCounterplay: true));

                // Fog high-visit corridors
                if (!room.IsFogged && _hostilityLevel > 0.4f)
                    actions.Add(new FacilityAction(FacilityActionType.RaiseFog, roomId, priority: 2.5f, cooldown: 45f, hasWarning: true, hasCounterplay: true));

                // Label corruption (mid+ hostility)
                if (_hostilityLevel > 0.55f && !_corruptedLabelRooms.Contains(roomId))
                    actions.Add(new FacilityAction(FacilityActionType.CorruptLabel, roomId, priority: 2f, cooldown: 120f, hasWarning: false, hasCounterplay: true));
            }

            // Heat pressure
            if (_hostilityLevel > 0.5f)
                actions.Add(new FacilityAction(FacilityActionType.RaiseHeat, SelectRandomHotRoom(hotRooms), priority: 2f, cooldown: 60f, hasWarning: true, hasCounterplay: true));

            // Door closures (always preserves a path)
            if (_hostilityLevel > 0.4f)
                actions.Add(new FacilityAction(FacilityActionType.LockDoor, SelectLockableDoor(), priority: 3.5f, cooldown: 60f, hasWarning: true, hasCounterplay: true));

            // Geometry shifts (late hostility only)
            if (_hostilityLevel > 0.75f && GameDirector.Instance?.CurrentPhase == GamePhase.LateGame)
                actions.Add(new FacilityAction(FacilityActionType.SwapGeometry, SelectSwappableRoom(hotRooms), priority: 4f, cooldown: 180f, hasWarning: false, hasCounterplay: false));

            // Machinery roar (masking cues)
            actions.Add(new FacilityAction(FacilityActionType.TriggerMachinery, null, priority: 1f, cooldown: 15f, hasWarning: true, hasCounterplay: true));

            return actions.Where(a => a.TargetId != null).ToList();
        }

        // ─── Action Execution ────────────────────────────────────────────────

        private void ExecuteAction(FacilityAction action)
        {
            Debug.Log($"[FacilityBrain] Executing {action.ActionType} on {action.TargetId ?? "global"}");

            EventBus.Publish(new FacilityBrainActionEvent
            {
                ActionType = action.ActionType,
                TargetId   = action.TargetId
            });

            switch (action.ActionType)
            {
                case FacilityActionType.LockDoor:
                    LockDoorSafely(action.TargetId);
                    break;

                case FacilityActionType.DimLights:
                    WorldStateManager.Instance?.SetRoomLit(action.TargetId, false);
                    break;

                case FacilityActionType.RaiseFog:
                    WorldStateManager.Instance?.SetRoomFogged(action.TargetId, true);
                    StartCoroutine(ClearFogAfter(action.TargetId, 30f));
                    break;

                case FacilityActionType.RaiseHeat:
                    WorldStateManager.Instance?.SetRoomHeated(action.TargetId, true);
                    StartCoroutine(CoolRoomAfter(action.TargetId, 45f));
                    break;

                case FacilityActionType.SlowLift:
                    SlowLifts();
                    break;

                case FacilityActionType.DisableMedStation:
                    DisableMedStation(action.TargetId);
                    break;

                case FacilityActionType.SwapGeometry:
                    TriggerGeometryShift(action.TargetId);
                    break;

                case FacilityActionType.CorruptLabel:
                    CorruptRoomLabel(action.TargetId);
                    break;

                case FacilityActionType.TriggerMachinery:
                    EventBus.Publish(new FacilityBrainActionEvent
                    {
                        ActionType = FacilityActionType.TriggerMachinery,
                        TargetId   = null
                    });
                    break;

                case FacilityActionType.ActivateSiren:
                    StartCoroutine(SirenBurst(3f));
                    break;

                case FacilityActionType.FloodSteam:
                    FloodYardSteam(action.TargetId);
                    break;

                case FacilityActionType.DisableFloodlights:
                    DisableYardFloodlights();
                    break;
            }
        }

        // ─── Door Locking (Safe) ─────────────────────────────────────────────

        private void LockDoorSafely(string doorId)
        {
            if (string.IsNullOrEmpty(doorId)) return;

            // Verify at least one path remains after locking
            var door = _allDoors.FirstOrDefault(d => d != null && d.DoorId == doorId);
            if (door == null) return;

            // Simulate lock and check connectivity
            door.SetLocked(true);
            bool stillConnected = WorldStateManager.Instance?
                .FindPath(door.RoomA, door.RoomB, ignoreLocks: false) != null;

            if (!stillConnected)
            {
                door.SetLocked(false); // revert — would disconnect the map
                Debug.Log($"[FacilityBrain] Door lock on {doorId} reverted — would disconnect map.");
            }
        }

        // ─── Geometry Shift ──────────────────────────────────────────────────

        private void TriggerGeometryShift(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            var room = WorldStateManager.Instance?.GetRoom(roomId);
            if (room == null) return;

            room.CycleGeometryVariant();
            _swappedGeometryRooms.Add(roomId);
            EventBus.Publish(new RoomStateChangedEvent
            {
                RoomId      = roomId,
                ChangedFlag = RoomStateFlag.GeometryVariant
            });
        }

        // ─── Label Corruption ────────────────────────────────────────────────

        private void CorruptRoomLabel(string roomId)
        {
            WorldStateManager.Instance?.SetLabelCorrupted(roomId, true);
            _corruptedLabelRooms.Add(roomId);
            StartCoroutine(RestoreLabelAfter(roomId, 60f));
        }

        // ─── Med Station ─────────────────────────────────────────────────────

        private void DisableMedStation(string stationId)
        {
            var station = _allMedStations.FirstOrDefault(m => m != null && m.StationId == stationId);
            station?.Disable(30f);
        }

        // ─── Lift ────────────────────────────────────────────────────────────

        private void SlowLifts()
        {
            foreach (var lift in _allLifts)
                lift?.ApplySlow(15f);
        }

        // ─── Yard Actions ────────────────────────────────────────────────────

        private void FloodYardSteam(string yardZoneId)
        {
            // Notify yard zone objects via event; they handle visual/gameplay
            EventBus.Publish(new FacilityBrainActionEvent
            {
                ActionType = FacilityActionType.FloodSteam,
                TargetId   = yardZoneId
            });
        }

        private void DisableYardFloodlights()
        {
            EventBus.Publish(new FacilityBrainActionEvent
            {
                ActionType = FacilityActionType.DisableFloodlights,
                TargetId   = "yard_global"
            });
        }

        // ─── Coroutines ──────────────────────────────────────────────────────

        private IEnumerator ClearFogAfter(string roomId, float delay)
        {
            yield return new WaitForSeconds(delay);
            WorldStateManager.Instance?.SetRoomFogged(roomId, false);
        }

        private IEnumerator CoolRoomAfter(string roomId, float delay)
        {
            yield return new WaitForSeconds(delay);
            WorldStateManager.Instance?.SetRoomHeated(roomId, false);
        }

        private IEnumerator RestoreLabelAfter(string roomId, float delay)
        {
            yield return new WaitForSeconds(delay);
            WorldStateManager.Instance?.SetLabelCorrupted(roomId, false);
            _corruptedLabelRooms.Remove(roomId);
        }

        private IEnumerator SirenBurst(float duration)
        {
            EventBus.Publish(new FacilityBrainActionEvent { ActionType = FacilityActionType.ActivateSiren, TargetId = "siren_on" });
            yield return new WaitForSeconds(duration);
            EventBus.Publish(new FacilityBrainActionEvent { ActionType = FacilityActionType.ActivateSiren, TargetId = "siren_off" });
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private string SelectLockableDoor()
        {
            var candidate = _allDoors.Where(d => d != null && !d.IsLocked).OrderBy(_ => Random.value).FirstOrDefault();
            return candidate?.DoorId;
        }

        private string SelectRandomHotRoom(List<string> hot)
            => hot.Count > 0 ? hot[Random.Range(0, hot.Count)] : null;

        private string SelectSwappableRoom(List<string> hot)
        {
            foreach (var id in hot)
            {
                var room = WorldStateManager.Instance?.GetRoom(id);
                if (room != null && room.HasGeometryVariants && !_swappedGeometryRooms.Contains(id))
                    return id;
            }
            return null;
        }

        private bool IsOnCooldown(FacilityActionType type)
            => _actionCooldowns.TryGetValue(type, out float until) && Time.time < until;

        private void SetCooldown(FacilityActionType type, float duration)
            => _actionCooldowns[type] = Time.time + duration;

        // ─── Event Handlers ──────────────────────────────────────────────────

        private void OnTensionChanged(TensionStateChangedEvent evt)
        {
            // Escalate hostility faster during Panic
            if (evt.Current == TensionState.Panic)
                _hostilityLevel = Mathf.Min(_hostilityLevel + 0.15f, 1f);
            // Slight relief during Recovery
            if (evt.Current == TensionState.Recovery)
                _hostilityLevel = Mathf.Max(_hostilityLevel - 0.05f, 0f);
        }

        // ─── Save/Load ───────────────────────────────────────────────────────

        public float ExportHostility() => _hostilityLevel;
        public void  ImportHostility(float value) => _hostilityLevel = Mathf.Clamp01(value);
    }

    // ─── Internal Action Model ───────────────────────────────────────────────

    internal class FacilityAction
    {
        public FacilityActionType ActionType;
        public string             TargetId;
        public float              Priority;
        public float              Cooldown;
        public bool               HasWarning;
        public bool               HasCounterplay;

        public FacilityAction(FacilityActionType type, string targetId, float priority,
                               float cooldown, bool hasWarning, bool hasCounterplay)
        {
            ActionType     = type;
            TargetId       = targetId;
            Priority       = priority;
            Cooldown       = cooldown;
            HasWarning     = hasWarning;
            HasCounterplay = hasCounterplay;
        }
    }
}
