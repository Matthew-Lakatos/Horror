using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.World
{
    using Core;

    // ─────────────────────────────────────────────────────────────────────────
    //  ROOM NODE DATA  (ScriptableObject – authored in editor)
    // ─────────────────────────────────────────────────────────────────────────
    [CreateAssetMenu(menuName = "Eidolon/Room Node Data", fileName = "RoomNodeData")]
    public class RoomNodeData : ScriptableObject
    {
        public string   RoomId;
        public string   DisplayLabel;
        public RoomType Type;
        public bool     IsSafeRoomDefault;
        public bool     HasMedStation;
        public float    DefaultDangerRating;    // 0–1
        public string[] DefaultExits;           // IDs of adjacent rooms
        public string[] PossibleGeometryVariants;
        public string   AmbientSoundProfile;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  RUNTIME ROOM NODE
    // ─────────────────────────────────────────────────────────────────────────
    public class RoomNode
    {
        // ── Identity ───────────────────────────────────────────────────────────
        public string   RoomId    { get; }
        public string   Label     { get; set; }         // may be corrupted
        public RoomType Type      { get; }
        public bool     IsSafeRoom { get; set; }

        // ── Physical state (controlled by FacilityBrain) ───────────────────────
        public bool  IsLit              { get; set; } = true;
        public bool  LightsFlickering   { get; set; }
        public bool  HasFog             { get; set; }
        public bool  IsOverheated       { get; set; }
        public bool  MedStationActive   { get; set; }
        public float HeatPressure       { get; set; }   // 0–1  stamina drain multiplier
        public float FogDensity         { get; set; }   // 0–1  vision reduction

        // ── Danger ────────────────────────────────────────────────────────────
        public float DangerRating       { get; set; }   // 0–1

        // ── Connectivity ──────────────────────────────────────────────────────
        private readonly List<RoomEdge> _edges = new();
        public  IReadOnlyList<RoomEdge> Edges  => _edges;

        // ── Active entities ────────────────────────────────────────────────────
        private readonly HashSet<string> _occupants = new();   // entity IDs

        // ── Geometry variant ──────────────────────────────────────────────────
        public string ActiveGeometryVariant { get; set; }

        // ── Constructor ────────────────────────────────────────────────────────
        public RoomNode(RoomNodeData data)
        {
            RoomId               = data.RoomId;
            Label                = data.DisplayLabel;
            Type                 = data.Type;
            IsSafeRoom           = data.IsSafeRoomDefault;
            DangerRating         = data.DefaultDangerRating;
            MedStationActive     = data.HasMedStation;
            ActiveGeometryVariant = data.PossibleGeometryVariants?.Length > 0
                                    ? data.PossibleGeometryVariants[0] : "";
        }

        // ── Edge management ────────────────────────────────────────────────────
        public void AddEdge(RoomEdge edge)      => _edges.Add(edge);
        public void RemoveEdge(string targetId) => _edges.RemoveAll(e => e.TargetId == targetId);
        public bool HasEdgeTo(string targetId)  => _edges.Exists(e => e.TargetId == targetId);

        // ── Open exits (respects locked doors) ────────────────────────────────
        public List<string> GetOpenExits()
        {
            var result = new List<string>();
            foreach (var e in _edges)
                if (!e.IsBlocked) result.Add(e.TargetId);
            return result;
        }

        // ── Occupancy ──────────────────────────────────────────────────────────
        public void AddOccupant(string id)    => _occupants.Add(id);
        public void RemoveOccupant(string id) => _occupants.Remove(id);
        public bool HasOccupant(string id)    => _occupants.Contains(id);
        public bool HasPlayer()               => _occupants.Contains("Player");
        public IReadOnlyCollection<string> Occupants => _occupants;

        // ── Blocking an exit (FacilityBrain door locks) ───────────────────────
        public void SetEdgeBlocked(string targetId, bool blocked)
        {
            var edge = _edges.Find(e => e.TargetId == targetId);
            if (edge != null) edge.IsBlocked = blocked;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ROOM EDGE
    // ─────────────────────────────────────────────────────────────────────────
    public class RoomEdge
    {
        public string TargetId;
        public bool   IsBlocked;
        public bool   RequiresKey;
        public string KeyItemId;
        public float  TraversalCost;   // for pathfinding

        public RoomEdge(string targetId, float cost = 1f)
        {
            TargetId      = targetId;
            TraversalCost = cost;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FACILITY GRAPH
    // ─────────────────────────────────────────────────────────────────────────
    public class FacilityGraph
    {
        private readonly Dictionary<string, RoomNode> _nodes = new();

        public void AddNode(RoomNode node)       => _nodes[node.RoomId] = node;
        public bool TryGetNode(string id, out RoomNode node) => _nodes.TryGetValue(id, out node);
        public RoomNode GetNode(string id)       => _nodes.TryGetValue(id, out var n) ? n : null;
        public IEnumerable<RoomNode> AllNodes    => _nodes.Values;

        // ── BFS pathfinding ───────────────────────────────────────────────────
        /// <summary>Returns the shortest open path from startId to goalId, or null if none.</summary>
        public List<string> FindPath(string startId, string goalId,
                                     bool respectBlocked = true)
        {
            if (startId == goalId) return new List<string> { startId };

            var visited = new HashSet<string>();
            var queue   = new Queue<List<string>>();
            queue.Enqueue(new List<string> { startId });

            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                string curr = path[^1];

                if (!_nodes.TryGetValue(curr, out var node)) continue;

                foreach (var edge in node.Edges)
                {
                    if (respectBlocked && edge.IsBlocked) continue;
                    if (visited.Contains(edge.TargetId))  continue;

                    var newPath = new List<string>(path) { edge.TargetId };
                    if (edge.TargetId == goalId) return newPath;

                    visited.Add(edge.TargetId);
                    queue.Enqueue(newPath);
                }
            }
            return null;
        }

        /// <summary>Returns all rooms reachable from startId within maxHops.</summary>
        public List<string> GetReachable(string startId, int maxHops)
        {
            var visited = new HashSet<string> { startId };
            var frontier = new List<string> { startId };

            for (int hop = 0; hop < maxHops; hop++)
            {
                var next = new List<string>();
                foreach (var id in frontier)
                {
                    if (!_nodes.TryGetValue(id, out var node)) continue;
                    foreach (var edge in node.Edges)
                    {
                        if (!edge.IsBlocked && !visited.Contains(edge.TargetId))
                        {
                            visited.Add(edge.TargetId);
                            next.Add(edge.TargetId);
                        }
                    }
                }
                frontier = next;
                if (frontier.Count == 0) break;
            }

            visited.Remove(startId);
            return new List<string>(visited);
        }

        /// <summary>Checks at least one viable path exists for the player. Used by FacilityBrain.</summary>
        public bool PlayerHasViablePath(string playerRoomId, string goalRoomId)
            => FindPath(playerRoomId, goalRoomId) != null;
    }
}
