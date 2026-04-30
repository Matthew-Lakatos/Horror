using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.World
{
    using Core;

    /// <summary>
    /// The facility itself as an antagonist.
    /// Learns player routes, pressures them, and manipulates the environment.
    /// Operates on a slow strategic tick to avoid CPU spikes.
    /// Never breaks the "at least one viable path" guarantee.
    /// </summary>
    public class FacilityBrain : MonoBehaviour, ISaveable
    {
        public static FacilityBrain Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Tick Rate")]
        [SerializeField] private float _strategicTickInterval  = 8f;   // slow – it's thinking
        [SerializeField] private float _routeLearningThreshold = 3;     // visits before route flagged

        [Header("Escalation")]
        [SerializeField] private float _escalationPerPhase      = 0.25f;
        [SerializeField] private float _maxGlobalHostility      = 1f;

        [Header("References")]
        [SerializeField] private FacilityGraphManager _graphManager;

        // ── Runtime state ──────────────────────────────────────────────────────
        public float GlobalHostility { get; private set; } = 0f;

        private float _tickTimer;
        private GamePhase _currentPhase = GamePhase.EarlyGame;

        // Rooms currently under active facility manipulation
        private readonly HashSet<string>              _activeManipulations = new();
        private readonly Dictionary<string, float>    _manipulationEndTimes = new();

        // Geometry variants currently shifted
        private readonly Dictionary<string, string>   _geometryOverrides = new();

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EvGamePhaseChanged>(OnPhaseChanged);
            EventBus.Subscribe<EvTensionChanged>  (OnTensionChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EvGamePhaseChanged>(OnPhaseChanged);
            EventBus.Unsubscribe<EvTensionChanged>  (OnTensionChanged);
        }

        private void Update()
        {
            _tickTimer += Time.deltaTime;
            if (_tickTimer >= _strategicTickInterval)
            {
                _tickTimer = 0f;
                StrategicTick();
            }

            // Clean up expired manipulations
            var expired = new List<string>();
            foreach (var kv in _manipulationEndTimes)
                if (Time.time >= kv.Value) expired.Add(kv.Key);
            foreach (var key in expired)
            {
                _activeManipulations.Remove(key);
                _manipulationEndTimes.Remove(key);
                RestoreRoom(key);
            }
        }

        // ── Strategic Tick ─────────────────────────────────────────────────────
        private void StrategicTick()
        {
            if (_graphManager == null) return;

            FacilityGraph graph      = _graphManager.Graph;
            var           wsm        = WorldStateManager.Instance;
            string        playerRoom = wsm?.PlayerCurrentRoomId;

            // Learn and pressure common routes
            PressureCommonRoutes(graph, wsm);

            // Phase-specific behaviours
            switch (_currentPhase)
            {
                case GamePhase.EarlyGame:
                    EarlyGameBehaviour(graph, playerRoom);
                    break;
                case GamePhase.MidGame:
                    MidGameBehaviour(graph, playerRoom);
                    break;
                case GamePhase.LateGame:
                    LateGameBehaviour(graph, playerRoom);
                    break;
            }
        }

        // ── Phase behaviours ───────────────────────────────────────────────────
        private void EarlyGameBehaviour(FacilityGraph graph, string playerRoom)
        {
            // Subtle: occasional light flicker, distant machinery
            if (UnityEngine.Random.value < 0.2f * GlobalHostility)
                TryAction(FacilityAction.FlickerLights, PickDistantRoom(graph, playerRoom, 3), 15f);
        }

        private void MidGameBehaviour(FacilityGraph graph, string playerRoom)
        {
            float r = UnityEngine.Random.value;

            if (r < 0.15f)
                TryAction(FacilityAction.DimLights, PickAdjacentRoom(graph, playerRoom), 30f);
            else if (r < 0.30f)
                TryAction(FacilityAction.VentFog, PickAdjacentRoom(graph, playerRoom), 45f);
            else if (r < 0.40f)
                TryAction(FacilityAction.TriggerMachinery, playerRoom, 20f);
            else if (r < 0.50f && GlobalHostility > 0.4f)
                TryLockDoor(graph, playerRoom);
        }

        private void LateGameBehaviour(FacilityGraph graph, string playerRoom)
        {
            MidGameBehaviour(graph, playerRoom);   // includes all mid behaviours

            float r = UnityEngine.Random.value;
            if (r < 0.20f)
                TryAction(FacilityAction.RaiseHeat, playerRoom, 60f);
            else if (r < 0.35f)
                TryAction(FacilityAction.MislabelRoom, PickAdjacentRoom(graph, playerRoom), 90f);
            else if (r < 0.45f)
                TryGeometryShift(graph, playerRoom);
        }

        // ── Route pressure ─────────────────────────────────────────────────────
        private void PressureCommonRoutes(FacilityGraph graph, WorldStateManager wsm)
        {
            if (wsm == null) return;
            var topRoutes = wsm.GetTopRoutes(3);

            foreach (var routeKey in topRoutes)
            {
                var parts = routeKey.Split('>');
                if (parts.Length != 2) continue;
                string from = parts[0], to = parts[1];
                int    count = wsm.GetRouteCount(from, to);

                if (count >= _routeLearningThreshold && GlobalHostility > 0.3f)
                {
                    // Pressure (NOT permanently lock) the route with fog or heat
                    if (UnityEngine.Random.value < 0.4f)
                        TryAction(FacilityAction.VentFog, from, 60f);
                }
            }
        }

        // ── Actions ────────────────────────────────────────────────────────────
        private void TryAction(FacilityAction action, string roomId, float duration)
        {
            if (roomId == null || _activeManipulations.Contains(roomId)) return;

            // Safety: don't act on player's current safe room in early/mid game
            if (!SafeToManipulate(roomId)) return;

            ApplyAction(action, roomId);
            _activeManipulations.Add(roomId);
            _manipulationEndTimes[roomId] = Time.time + duration;

            EventBus.Publish(new EvFacilityActionTriggered(action, roomId));
        }

        private void TryLockDoor(FacilityGraph graph, string playerRoom)
        {
            if (playerRoom == null) return;
            var node = graph.GetNode(playerRoom);
            if (node == null) return;

            // Only lock one exit, NEVER all exits, NEVER player's only viable path
            var exits = node.GetOpenExits();
            if (exits.Count <= 1) return;   // must keep at least one open

            string targetGoal = ObjectiveManager.Instance?.CurrentTargetRoomId;
            if (targetGoal == null) return;

            // Find the exit we can lock without breaking progress
            foreach (var exit in exits)
            {
                // Simulate locking it and check player still has a path
                node.SetEdgeBlocked(exit, true);
                bool viable = graph.PlayerHasViablePath(playerRoom, targetGoal);
                node.SetEdgeBlocked(exit, false);

                if (viable)
                {
                    TryAction(FacilityAction.LockDoor, exit, 45f);
                    ApplyDoorLock(graph, playerRoom, exit, true);
                    return;
                }
            }
        }

        private void TryGeometryShift(FacilityGraph graph, string playerRoom)
        {
            // Pick a non-adjacent room to shift geometry
            string target = PickDistantRoom(graph, playerRoom, 4);
            if (target == null) return;

            var node = graph.GetNode(target);
            if (node == null) return;

            // Geometry shifts don't break pathfinding – they only affect visuals
            TryAction(FacilityAction.ShiftGeometry, target, 120f);
        }

        // ── Apply / restore helpers ────────────────────────────────────────────
        private void ApplyAction(FacilityAction action, string roomId)
        {
            var graph = _graphManager?.Graph;
            var node  = graph?.GetNode(roomId);
            if (node == null) return;

            switch (action)
            {
                case FacilityAction.DimLights:
                    node.IsLit = false;
                    break;
                case FacilityAction.FlickerLights:
                    node.LightsFlickering = true;
                    break;
                case FacilityAction.VentFog:
                    node.HasFog     = true;
                    node.FogDensity = 0.5f + UnityEngine.Random.value * 0.4f;
                    break;
                case FacilityAction.RaiseHeat:
                    node.IsOverheated = true;
                    node.HeatPressure = 0.5f + UnityEngine.Random.value * 0.4f;
                    break;
                case FacilityAction.DisableStation:
                    node.MedStationActive = false;
                    break;
                case FacilityAction.MislabelRoom:
                    node.Label = GenerateWrongLabel(node.Label);
                    break;
                case FacilityAction.ShiftGeometry:
                    // Handled by world geometry system listening to EventBus
                    break;
            }
        }

        private void RestoreRoom(string roomId)
        {
            var graph = _graphManager?.Graph;
            var node  = graph?.GetNode(roomId);
            if (node == null) return;

            node.IsLit            = true;
            node.LightsFlickering = false;
            node.HasFog           = false;
            node.FogDensity       = 0f;
            node.IsOverheated     = false;
            node.HeatPressure     = 0f;
            // Med station only restored by manual override
            // Label restored gradually
        }

        private void ApplyDoorLock(FacilityGraph graph, string fromId, string toId, bool locked)
        {
            var node = graph.GetNode(fromId);
            node?.SetEdgeBlocked(toId, locked);

            // Also lock the reverse edge
            var target = graph.GetNode(toId);
            target?.SetEdgeBlocked(fromId, locked);

            EventBus.Publish(new EvFacilityActionTriggered(FacilityAction.LockDoor, toId));
        }

        // ── Utilities ──────────────────────────────────────────────────────────
        private bool SafeToManipulate(string roomId)
        {
            var graph = _graphManager?.Graph;
            var node  = graph?.GetNode(roomId);
            if (node == null) return false;

            // Protect safe rooms in early/mid game
            if (node.IsSafeRoom && _currentPhase != GamePhase.LateGame) return false;

            return true;
        }

        private string PickAdjacentRoom(FacilityGraph graph, string playerRoom)
        {
            if (playerRoom == null) return null;
            var node = graph.GetNode(playerRoom);
            if (node == null) return null;
            var exits = node.GetOpenExits();
            if (exits.Count == 0) return null;
            return exits[UnityEngine.Random.Range(0, exits.Count)];
        }

        private string PickDistantRoom(FacilityGraph graph, string playerRoom, int minHops)
        {
            if (playerRoom == null) return null;
            var candidates = graph.GetReachable(playerRoom, 8);
            candidates.RemoveAll(r => graph.GetReachable(playerRoom, minHops - 1).Contains(r));
            if (candidates.Count == 0) return null;
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private string GenerateWrongLabel(string original)
        {
            var alternates = new[] { "SECTOR-7", "LOADING BAY", "ARCHIVE B", "UNIT 4", "STORAGE" };
            foreach (var alt in alternates)
                if (!alt.Equals(original, StringComparison.OrdinalIgnoreCase))
                    return alt;
            return "???";
        }

        // ── Event handlers ─────────────────────────────────────────────────────
        private void OnPhaseChanged(EvGamePhaseChanged evt)
        {
            _currentPhase = evt.Phase;
            GlobalHostility = Mathf.Min(_maxGlobalHostility,
                GlobalHostility + _escalationPerPhase);
        }

        private void OnTensionChanged(EvTensionChanged evt)
        {
            if (evt.Next == TensionState.Panic)
                GlobalHostility = Mathf.Min(_maxGlobalHostility, GlobalHostility + 0.05f);
            else if (evt.Next == TensionState.Recovery)
                GlobalHostility = Mathf.Max(0f, GlobalHostility - 0.02f);
        }

        // ── Public API ─────────────────────────────────────────────────────────
        /// <summary>Called by manual override pickups found by the player.</summary>
        public void PlayerOverride(string roomId, FacilityAction restoreAction)
        {
            _activeManipulations.Remove(roomId);
            _manipulationEndTimes.Remove(roomId);
            RestoreRoom(roomId);
        }

        // ── ISaveable ──────────────────────────────────────────────────────────
        public string SaveKey => "FacilityBrain";

        public object CaptureState()
        {
            return new FacilityBrainSave
            {
                GlobalHostility      = GlobalHostility,
                ActiveManipulations  = new List<string>(_activeManipulations),
                GeometryOverrides    = new Dictionary<string, string>(_geometryOverrides)
            };
        }

        public void RestoreState(object raw)
        {
            if (raw is not FacilityBrainSave data) return;
            GlobalHostility = data.GlobalHostility;
            _activeManipulations.Clear();
            foreach (var r in data.ActiveManipulations) _activeManipulations.Add(r);
            _geometryOverrides.Clear();
            foreach (var kv in data.GeometryOverrides) _geometryOverrides[kv.Key] = kv.Value;
        }
    }

    [System.Serializable]
    public class FacilityBrainSave
    {
        public float                     GlobalHostility;
        public List<string>              ActiveManipulations;
        public Dictionary<string, string> GeometryOverrides;
    }
}
