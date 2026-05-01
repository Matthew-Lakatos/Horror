using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Eidolon.Core;
using Eidolon.Actors;
using Eidolon.AI;
using Eidolon.Conditions;
using Eidolon.Objectives;
using Eidolon.World;

namespace Eidolon.Save
{
    /// <summary>
    /// Handles full game save/load serialisation to JSON.
    /// Persists all required state: objectives, room states, player conditions,
    /// AI learned habits, route heatmaps, director phase, facility hostility.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private string _saveDirectory = "EidolonSaves";
        [SerializeField] private int    _maxSlots      = 3;

        private string SaveBasePath =>
            Path.Combine(Application.persistentDataPath, _saveDirectory);

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Directory.CreateDirectory(SaveBasePath);
        }

        private void OnEnable()  => EventBus.Subscribe<SaveRequestEvent>(OnSaveRequested);
        private void OnDisable() => EventBus.Unsubscribe<SaveRequestEvent>(OnSaveRequested);

        // ─── Save ────────────────────────────────────────────────────────────

        public void Save(string slot = "slot_0")
        {
            var data = CollectSaveData();
            data.SaveSlot      = slot;
            data.SaveTimestamp = System.DateTime.UtcNow.ToString("O");
            data.PlaytimeSeconds += Time.time; // accumulate

            string json = JsonUtility.ToJson(data, prettyPrint: true);
            string path = GetSavePath(slot);
            File.WriteAllText(path, json);

            Debug.Log($"[SaveManager] Saved to {path}");
        }

        // ─── Load ────────────────────────────────────────────────────────────

        public bool Load(string slot = "slot_0")
        {
            string path = GetSavePath(slot);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SaveManager] No save found at {path}");
                return false;
            }

            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<EidolonSaveData>(json);
            if (data == null) return false;

            ApplySaveData(data);
            Debug.Log($"[SaveManager] Loaded from {path}");
            return true;
        }

        public bool SlotExists(string slot) => File.Exists(GetSavePath(slot));

        public void DeleteSlot(string slot)
        {
            var path = GetSavePath(slot);
            if (File.Exists(path)) File.Delete(path);
        }

        // ─── Collect ─────────────────────────────────────────────────────────

        private EidolonSaveData CollectSaveData()
        {
            var data = new EidolonSaveData();

            // Player
            var player = PlayerController.Instance;
            if (player != null)
                data.Player = player.ExportState();

            // Objectives
            var objMgr = ObjectiveManager.Instance;
            if (objMgr != null)
                data.Objectives = objMgr.ExportState();

            // Room states
            var worldMgr = WorldStateManager.Instance;
            if (worldMgr != null)
            {
                data.RoomHeatmap = SerialiseHeatmap(worldMgr.ExportHeatmap());
                data.RoomStates  = CollectRoomStates(worldMgr);
            }

            // Conditions
            var condMgr = ConditionManager.Instance;
            if (condMgr != null)
                data.Conditions = condMgr.ExportConditions();

            // AI habits (Big E)
            var bigEs = AIManager.Instance?.GetEnemiesOfType(EnemyType.BigE);
            if (bigEs != null && bigEs.Count > 0 && bigEs[0] is BigEActor bigE)
                data.BigEState = bigE.ExportState();

            // Director
            var dir = GameDirector.Instance;
            if (dir != null)
            {
                data.CurrentPhase       = dir.CurrentPhase;
                data.CurrentTension     = dir.CurrentTension;
                data.ObjectiveProgress  = dir.ObjectiveProgress;
            }

            // Facility hostility
            var brain = FacilityBrain.Instance;
            if (brain != null)
                data.FacilityHostility = brain.ExportHostility();

            return data;
        }

        // ─── Apply ───────────────────────────────────────────────────────────

        private void ApplySaveData(EidolonSaveData data)
        {
            // Player
            if (data.Player != null)
                PlayerController.Instance?.ImportState(data.Player);

            // Objectives
            if (data.Objectives != null)
                ObjectiveManager.Instance?.ImportState(data.Objectives);

            // Room heatmap
            if (data.RoomHeatmap != null)
            {
                var heatmap = DeserialiseHeatmap(data.RoomHeatmap);
                WorldStateManager.Instance?.ImportHeatmap(heatmap);
            }

            // Room states
            if (data.RoomStates != null)
                ApplyRoomStates(data.RoomStates);

            // Conditions
            if (data.Conditions != null)
                ConditionManager.Instance?.ImportConditions(data.Conditions);

            // Big E
            if (data.BigEState != null)
            {
                var bigEs = AIManager.Instance?.GetEnemiesOfType(EnemyType.BigE);
                if (bigEs != null && bigEs.Count > 0 && bigEs[0] is BigEActor bigE)
                    bigE.ImportState(data.BigEState);
            }

            // Facility hostility
            FacilityBrain.Instance?.ImportHostility(data.FacilityHostility);
        }

        // ─── Room State Helpers ──────────────────────────────────────────────

        private List<RoomSaveData> CollectRoomStates(WorldStateManager worldMgr)
        {
            var result = new List<RoomSaveData>();
            foreach (var room in worldMgr.GetAllRooms())
                result.Add(room.ExportState());
            return result;
        }

        private void ApplyRoomStates(List<RoomSaveData> states)
        {
            foreach (var state in states)
            {
                var room = WorldStateManager.Instance?.GetRoom(state.RoomId);
                room?.ImportState(state);
            }
        }

        // ─── Heatmap Serialisation ───────────────────────────────────────────

        private List<HeatmapEntry> SerialiseHeatmap(Dictionary<string, int> heatmap)
        {
            var result = new List<HeatmapEntry>();
            foreach (var kv in heatmap)
                result.Add(new HeatmapEntry { RoomId = kv.Key, Count = kv.Value });
            return result;
        }

        private Dictionary<string, int> DeserialiseHeatmap(List<HeatmapEntry> entries)
        {
            var result = new Dictionary<string, int>();
            foreach (var e in entries)
                result[e.RoomId] = e.Count;
            return result;
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private string GetSavePath(string slot) => Path.Combine(SaveBasePath, $"{slot}.json");

        private void OnSaveRequested(SaveRequestEvent evt) => Save(evt.Slot);
    }

    // ─── Save Data Schema ────────────────────────────────────────────────────

    [System.Serializable]
    public class EidolonSaveData
    {
        public string                     SaveSlot;
        public string                     SaveTimestamp;
        public float                      PlaytimeSeconds;

        // Player
        public PlayerSaveData             Player;

        // Objectives
        public List<ObjectiveSaveEntry>   Objectives;

        // World
        public List<HeatmapEntry>         RoomHeatmap;
        public List<RoomSaveData>         RoomStates;

        // Conditions
        public List<ConditionSaveEntry>   Conditions;

        // AI
        public BigESaveData               BigEState;

        // Director
        public GamePhase                  CurrentPhase;
        public TensionState               CurrentTension;
        public float                      ObjectiveProgress;

        // Facility
        public float                      FacilityHostility;
    }

    [System.Serializable]
    public class HeatmapEntry
    {
        public string RoomId;
        public int    Count;
    }
}
