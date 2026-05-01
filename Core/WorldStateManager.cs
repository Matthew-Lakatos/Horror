using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Manages the room-node graph representing the facility.
    /// Tracks per-room state, provides pathfinding queries, and exposes
    /// route heatmap data used by AI and FacilityBrain.
    /// </summary>
    public class WorldStateManager : MonoBehaviour
    {
        public static WorldStateManager Instance { get; private set; }

        [SerializeField] private List<World.RoomNode> _allRooms = new List<World.RoomNode>();

        // Adjacency list: roomId → set of connected roomIds
        private readonly Dictionary<string, HashSet<string>> _adjacency
            = new Dictionary<string, HashSet<string>>();

        // Visit heatmap: roomId → visit count
        private readonly Dictionary<string, int> _visitHeatmap
            = new Dictionary<string, int>();

        // Cache of all rooms by id
        private readonly Dictionary<string, World.RoomNode> _roomById
            = new Dictionary<string, World.RoomNode>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildGraph();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<RoomStateChangedEvent>(OnRoomStateChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<RoomStateChangedEvent>(OnRoomStateChanged);
        }

        // ─── Graph Construction ──────────────────────────────────────────────

        private void BuildGraph()
        {
            _roomById.Clear();
            _adjacency.Clear();

            foreach (var room in _allRooms)
            {
                if (room == null || string.IsNullOrEmpty(room.RoomId)) continue;
                _roomById[room.RoomId] = room;
                _adjacency[room.RoomId] = new HashSet<string>();
                _visitHeatmap[room.RoomId] = 0;
            }

            foreach (var room in _allRooms)
            {
                if (room == null) continue;
                foreach (var exit in room.Exits)
                {
                    if (!string.IsNullOrEmpty(exit) && _roomById.ContainsKey(exit))
                    {
                        _adjacency[room.RoomId].Add(exit);
                        _adjacency[exit].Add(room.RoomId); // undirected
                    }
                }
            }
        }

        // ─── Room Queries ────────────────────────────────────────────────────

        public World.RoomNode GetRoom(string roomId)
            => _roomById.TryGetValue(roomId, out var room) ? room : null;

        public IReadOnlyCollection<string> GetNeighbours(string roomId)
        {
            if (_adjacency.TryGetValue(roomId, out var neighbours))
                return neighbours;
            return System.Array.Empty<string>();
        }

        public IEnumerable<World.RoomNode> GetAllRooms() => _allRooms.Where(r => r != null);

        // ─── Pathfinding (BFS) ───────────────────────────────────────────────

        /// <summary>
        /// Returns shortest passable path from start to goal.
        /// FacilityBrain-locked doors are treated as impassable (unless ignoreLocks = true).
        /// Guarantees at least one viable route always exists (as per design spec).
        /// </summary>
        public List<string> FindPath(string startId, string goalId, bool ignoreLocks = false)
        {
            if (!_roomById.ContainsKey(startId) || !_roomById.ContainsKey(goalId))
                return null;

            var visited  = new HashSet<string>();
            var parent   = new Dictionary<string, string>();
            var queue    = new Queue<string>();

            queue.Enqueue(startId);
            visited.Add(startId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == goalId)
                    return ReconstructPath(parent, startId, goalId);

                foreach (var neighbour in GetNeighbours(current))
                {
                    if (visited.Contains(neighbour)) continue;
                    var neighbourRoom = GetRoom(neighbour);
                    if (!ignoreLocks && neighbourRoom != null && neighbourRoom.IsLocked) continue;
                    visited.Add(neighbour);
                    parent[neighbour] = current;
                    queue.Enqueue(neighbour);
                }
            }

            // No path found — try with locks ignored (guarantee at least one path exists)
            if (!ignoreLocks)
                return FindPath(startId, goalId, ignoreLocks: true);

            return null; // Graph is fully disconnected (should never happen with valid level design)
        }

        private static List<string> ReconstructPath(Dictionary<string, string> parent, string start, string goal)
        {
            var path = new List<string>();
            var current = goal;
            while (current != start)
            {
                path.Add(current);
                current = parent[current];
            }
            path.Add(start);
            path.Reverse();
            return path;
        }

        // ─── Heatmap ────────────────────────────────────────────────────────

        public void RecordVisit(string roomId)
        {
            if (!_visitHeatmap.ContainsKey(roomId)) _visitHeatmap[roomId] = 0;
            _visitHeatmap[roomId]++;
        }

        public int GetVisitCount(string roomId)
            => _visitHeatmap.TryGetValue(roomId, out int c) ? c : 0;

        public IReadOnlyDictionary<string, int> GetHeatmap() => _visitHeatmap;

        /// <summary>Returns top N most-visited rooms (used by FacilityBrain to target hot routes).</summary>
        public List<string> GetMostVisitedRooms(int topN)
        {
            return _visitHeatmap
                .OrderByDescending(kv => kv.Value)
                .Take(topN)
                .Select(kv => kv.Key)
                .ToList();
        }

        // ─── Room State Mutations ────────────────────────────────────────────

        public void SetRoomLocked(string roomId, bool locked)
        {
            var room = GetRoom(roomId);
            if (room == null) return;
            room.IsLocked = locked;
            EventBus.Publish(new RoomStateChangedEvent
            {
                RoomId      = roomId,
                ChangedFlag = RoomStateFlag.Locked
            });
        }

        public void SetRoomLit(string roomId, bool lit)
        {
            var room = GetRoom(roomId);
            if (room == null) return;
            room.IsLit = lit;
            EventBus.Publish(new RoomStateChangedEvent
            {
                RoomId      = roomId,
                ChangedFlag = RoomStateFlag.Lit
            });
        }

        public void SetRoomFogged(string roomId, bool fogged)
        {
            var room = GetRoom(roomId);
            if (room == null) return;
            room.IsFogged = fogged;
            EventBus.Publish(new RoomStateChangedEvent
            {
                RoomId      = roomId,
                ChangedFlag = RoomStateFlag.Fogged
            });
        }

        public void SetRoomHeated(string roomId, bool heated)
        {
            var room = GetRoom(roomId);
            if (room == null) return;
            room.IsHeated = heated;
            EventBus.Publish(new RoomStateChangedEvent
            {
                RoomId      = roomId,
                ChangedFlag = RoomStateFlag.Heated
            });
        }

        public void SetLabelCorrupted(string roomId, bool corrupted)
        {
            var room = GetRoom(roomId);
            if (room == null) return;
            room.IsLabelCorrupted = corrupted;
            EventBus.Publish(new RoomStateChangedEvent
            {
                RoomId      = roomId,
                ChangedFlag = RoomStateFlag.LabelCorrupted
            });
        }

        // ─── Sector / Building Queries ───────────────────────────────────────

        public List<World.RoomNode> GetRoomsInBuilding(BuildingType building)
            => _allRooms.Where(r => r != null && r.Building == building).ToList();

        public bool AreConnected(string roomA, string roomB)
            => _adjacency.TryGetValue(roomA, out var n) && n.Contains(roomB);

        // ─── Event Handlers ──────────────────────────────────────────────────

        private void OnRoomStateChanged(RoomStateChangedEvent evt)
        {
            // Rebuild adjacency if geometry changed
            if (evt.ChangedFlag == RoomStateFlag.GeometryVariant)
                BuildGraph();
        }

        // ─── Save/Load Integration ───────────────────────────────────────────

        public Dictionary<string, int> ExportHeatmap() => new Dictionary<string, int>(_visitHeatmap);

        public void ImportHeatmap(Dictionary<string, int> data)
        {
            foreach (var kv in data)
                if (_visitHeatmap.ContainsKey(kv.Key))
                    _visitHeatmap[kv.Key] = kv.Value;
        }
    }

    public enum BuildingType { BuildingA, BuildingB, BuildingC, Yard, Skybridge, Tunnel }
}
