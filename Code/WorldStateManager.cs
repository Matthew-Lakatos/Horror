using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Single source of truth for runtime world state:
    /// visit counts, route heatmaps, noise history, danger ratings.
    /// All systems read from here; only a few write (player, FacilityBrain).
    /// </summary>
    public class WorldStateManager : MonoBehaviour, ISaveable
    {
        public static WorldStateManager Instance { get; private set; }

        // ── Room visit data ────────────────────────────────────────────────────
        private readonly Dictionary<string, int>   _visitCounts   = new();
        private readonly Dictionary<string, float> _lastVisitTime = new();
        private readonly Dictionary<string, float> _dangerRatings = new();

        // ── Route heatmap (transitions A→B) ───────────────────────────────────
        private readonly Dictionary<string, int> _routeHeatmap = new();

        // ── Noise events ───────────────────────────────────────────────────────
        private readonly List<NoiseEvent> _noiseHistory = new();
        private const int MaxNoiseHistory = 64;

        // ── Globally known state ───────────────────────────────────────────────
        public string PlayerCurrentRoomId  { get; private set; }
        public string PlayerPreviousRoomId { get; private set; }

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EvRoomEntered> (OnRoomEntered);
            EventBus.Subscribe<EvPlayerNoise> (OnPlayerNoise);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EvRoomEntered>(OnRoomEntered);
            EventBus.Unsubscribe<EvPlayerNoise>(OnPlayerNoise);
        }

        // ── Room tracking ──────────────────────────────────────────────────────
        private void OnRoomEntered(EvRoomEntered evt)
        {
            string id = evt.RoomId;

            if (!_visitCounts.ContainsKey(id)) _visitCounts[id] = 0;
            _visitCounts[id]++;
            _lastVisitTime[id] = Time.time;

            // Route heatmap
            if (PlayerCurrentRoomId != null && PlayerCurrentRoomId != id)
            {
                string routeKey = $"{PlayerCurrentRoomId}>{id}";
                if (!_routeHeatmap.ContainsKey(routeKey)) _routeHeatmap[routeKey] = 0;
                _routeHeatmap[routeKey]++;
            }

            PlayerPreviousRoomId = PlayerCurrentRoomId;
            PlayerCurrentRoomId  = id;
        }

        // ── Noise history ──────────────────────────────────────────────────────
        private void OnPlayerNoise(EvPlayerNoise evt)
        {
            _noiseHistory.Add(new NoiseEvent(evt.Origin, evt.Level, Time.time));
            if (_noiseHistory.Count > MaxNoiseHistory)
                _noiseHistory.RemoveAt(0);
        }

        // ── Query API ──────────────────────────────────────────────────────────
        public int   GetVisitCount(string roomId)
            => _visitCounts.TryGetValue(roomId, out int v) ? v : 0;

        public float GetLastVisitTime(string roomId)
            => _lastVisitTime.TryGetValue(roomId, out float t) ? t : -1f;

        public int   GetRouteCount(string fromId, string toId)
            => _routeHeatmap.TryGetValue($"{fromId}>{toId}", out int v) ? v : 0;

        public float GetDangerRating(string roomId)
            => _dangerRatings.TryGetValue(roomId, out float v) ? v : 0f;

        public void SetDangerRating(string roomId, float value)
            => _dangerRatings[roomId] = Mathf.Clamp01(value);

        /// <summary>Returns the most visited room ID (player habit). </summary>
        public string GetMostVisitedRoom()
        {
            string best = null;
            int    max  = 0;
            foreach (var kv in _visitCounts)
                if (kv.Value > max) { max = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>Returns top N route keys by frequency.</summary>
        public List<string> GetTopRoutes(int n)
        {
            var sorted = new List<KeyValuePair<string, int>>(_routeHeatmap);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            var result = new List<string>();
            for (int i = 0; i < Mathf.Min(n, sorted.Count); i++)
                result.Add(sorted[i].Key);
            return result;
        }

        /// <summary>All noise events within the last N seconds.</summary>
        public List<NoiseEvent> GetRecentNoise(float windowSeconds)
        {
            float cutoff = Time.time - windowSeconds;
            var   result = new List<NoiseEvent>();
            foreach (var n in _noiseHistory)
                if (n.Time >= cutoff) result.Add(n);
            return result;
        }

        public IReadOnlyDictionary<string, int> RouteHeatmap => _routeHeatmap;

        // ── ISaveable ──────────────────────────────────────────────────────────
        public string SaveKey => "WorldState";

        public object CaptureState()
        {
            return new WorldSaveData
            {
                VisitCounts    = new Dictionary<string, int>(_visitCounts),
                LastVisitTimes = new Dictionary<string, float>(_lastVisitTime),
                RouteHeatmap   = new Dictionary<string, int>(_routeHeatmap),
                DangerRatings  = new Dictionary<string, float>(_dangerRatings),
                PlayerRoom     = PlayerCurrentRoomId
            };
        }

        public void RestoreState(object raw)
        {
            if (raw is not WorldSaveData data) return;
            _visitCounts.Clear();
            _lastVisitTime.Clear();
            _routeHeatmap.Clear();
            _dangerRatings.Clear();

            foreach (var kv in data.VisitCounts)    _visitCounts[kv.Key]    = kv.Value;
            foreach (var kv in data.LastVisitTimes) _lastVisitTime[kv.Key]  = kv.Value;
            foreach (var kv in data.RouteHeatmap)   _routeHeatmap[kv.Key]   = kv.Value;
            foreach (var kv in data.DangerRatings)  _dangerRatings[kv.Key]  = kv.Value;
            PlayerCurrentRoomId = data.PlayerRoom;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DATA STRUCTURES
    // ─────────────────────────────────────────────────────────────────────────
    [System.Serializable]
    public class NoiseEvent
    {
        public Vector3    Origin;
        public NoiseLevel Level;
        public float      Time;
        public NoiseEvent(Vector3 o, NoiseLevel l, float t) { Origin = o; Level = l; Time = t; }
    }

    [System.Serializable]
    public class WorldSaveData
    {
        public Dictionary<string, int>   VisitCounts;
        public Dictionary<string, float> LastVisitTimes;
        public Dictionary<string, int>   RouteHeatmap;
        public Dictionary<string, float> DangerRatings;
        public string                    PlayerRoom;
    }
}
