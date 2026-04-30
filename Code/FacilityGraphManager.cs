using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.World
{
    using Core;

    /// <summary>
    /// Scene-level manager that owns the FacilityGraph, maps RoomNodeData assets
    /// to runtime RoomNodes, and resolves room world positions for AI navigation.
    /// Also drives room-entered events when the player triggers room volumes.
    /// </summary>
    public class FacilityGraphManager : MonoBehaviour
    {
        public static FacilityGraphManager Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Graph Data")]
        [SerializeField] private RoomNodeData[] _roomDataAssets;

        [Header("Scene Anchors")]
        [Tooltip("One RoomAnchor component per room – provides world position for NavMesh.")]
        [SerializeField] private RoomAnchor[] _roomAnchors;

        // ── Runtime ────────────────────────────────────────────────────────────
        public FacilityGraph Graph { get; private set; }

        private readonly Dictionary<string, Vector3> _worldPositions = new();

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            BuildGraph();
            IndexWorldPositions();
        }

        // ── Graph construction ─────────────────────────────────────────────────
        private void BuildGraph()
        {
            Graph = new FacilityGraph();

            if (_roomDataAssets == null) return;

            // First pass: create all nodes
            foreach (var data in _roomDataAssets)
            {
                if (data == null) continue;
                Graph.AddNode(new RoomNode(data));
            }

            // Second pass: wire edges (bidirectional)
            foreach (var data in _roomDataAssets)
            {
                if (data?.DefaultExits == null) continue;
                foreach (var exitId in data.DefaultExits)
                {
                    if (!Graph.TryGetNode(data.RoomId, out var from)) continue;
                    if (!Graph.TryGetNode(exitId,      out var to  )) continue;

                    if (!from.HasEdgeTo(exitId))
                        from.AddEdge(new RoomEdge(exitId, 1f));

                    if (!to.HasEdgeTo(data.RoomId))
                        to.AddEdge(new RoomEdge(data.RoomId, 1f));
                }
            }

            Debug.Log($"[FacilityGraphManager] Built graph: {_roomDataAssets.Length} rooms.");
        }

        private void IndexWorldPositions()
        {
            if (_roomAnchors == null) return;
            foreach (var anchor in _roomAnchors)
            {
                if (anchor != null && !string.IsNullOrEmpty(anchor.RoomId))
                    _worldPositions[anchor.RoomId] = anchor.transform.position;
            }
        }

        // ── World position query ───────────────────────────────────────────────
        public Vector3 GetRoomWorldPos(string roomId)
        {
            if (_worldPositions.TryGetValue(roomId, out var pos)) return pos;
            Debug.LogWarning($"[FacilityGraphManager] No world position for room: {roomId}");
            return Vector3.zero;
        }

        // ── Occupancy update (called by RoomVolume triggers) ───────────────────
        public void OnEntityEnteredRoom(string entityId, string roomId)
        {
            // Update old room
            foreach (var node in Graph.AllNodes)
                node.RemoveOccupant(entityId);

            if (Graph.TryGetNode(roomId, out var newNode))
            {
                newNode.AddOccupant(entityId);

                // Ambient sound for player
                if (entityId == "Player" && !string.IsNullOrEmpty(newNode.Type.ToString()))
                    AudioManager.Instance?.SetRoomAmbience(newNode.Type.ToString().ToLower());
            }

            if (entityId == "Player")
            {
                var player = FindObjectOfType<Actors.PlayerController>();
                if (player) player.CurrentRoomId = roomId;
                EventBus.Publish(new EvRoomEntered(roomId));
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ROOM ANCHOR  – one per room in the scene, positioned at nav centre
    // ─────────────────────────────────────────────────────────────────────────
    public class RoomAnchor : MonoBehaviour
    {
        [Tooltip("Must match a RoomNodeData.RoomId exactly.")]
        public string RoomId;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            UnityEditor.Handles.Label(transform.position + Vector3.up, RoomId);
        }
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ROOM VOLUME  – trigger collider that fires room-entered events
    // ─────────────────────────────────────────────────────────────────────────
    public class RoomVolume : MonoBehaviour
    {
        [Tooltip("Must match a RoomNodeData.RoomId exactly.")]
        public string RoomId;

        private void OnTriggerEnter(Collider other)
        {
            string entityId = ResolveEntityId(other);
            if (entityId == null) return;
            FacilityGraphManager.Instance?.OnEntityEnteredRoom(entityId, RoomId);
        }

        private string ResolveEntityId(Collider col)
        {
            var player = col.GetComponentInParent<Actors.PlayerController>();
            if (player) return "Player";

            var enemy = col.GetComponentInParent<Actors.EnemyActor>();
            if (enemy) return enemy.EntityId;

            return null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.12f);
            var col = GetComponent<Collider>();
            if (col is BoxCollider box)
                Gizmos.DrawCube(transform.position, box.size);
        }
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FACILITY BRAIN EXTENSION – TryDisableStation
    //  Added here to avoid circular dependency with NurseController
    // ─────────────────────────────────────────────────────────────────────────
    public partial class FacilityBrain
    {
        public void TryDisableStation(string roomId, float duration)
        {
            var graph = _graphManager?.Graph;
            var node  = graph?.GetNode(roomId);
            if (node == null) return;
            node.MedStationActive = false;
            StartCoroutine(RestoreStationAfter(node, duration));
        }

        private System.Collections.IEnumerator RestoreStationAfter(RoomNode node, float delay)
        {
            yield return new UnityEngine.WaitForSeconds(delay);
            node.MedStationActive = true;
        }

        // Give Nurse access to _graphManager reference
        [SerializeField] private FacilityGraphManager _graphManager;
    }
}
