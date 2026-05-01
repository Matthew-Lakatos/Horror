using System.Collections.Generic;
using UnityEngine;
using Eidolon.Core;
using Eidolon.Data;

namespace Eidolon.Core
{
    /// <summary>
    /// Manages the player's inventory, resource scarcity, item caches,
    /// and lore log collection. Resources create tension — never annoyance.
    /// Progress will never be softlocked due to resource scarcity.
    /// </summary>
    public class ResourceManager : MonoBehaviour
    {
        public static ResourceManager Instance { get; private set; }

        [Header("Starting Resources")]
        [SerializeField] private int   _startingBandages     = 2;
        [SerializeField] private int   _startingBatteries    = 1;
        [SerializeField] private int   _startingDecoys       = 1;
        [SerializeField] private bool  _startWithWrench      = true;

        [Header("Inventory Limits")]
        [SerializeField] private int   _maxBandages          = 6;
        [SerializeField] private int   _maxBatteries         = 4;
        [SerializeField] private int   _maxDecoys            = 3;

        [Header("Healing")]
        [SerializeField] private float _bandageHealAmount    = 30f;

        // ─── Inventory State ─────────────────────────────────────────────────

        private int _bandages;
        private int _batteries;
        private int _decoys;
        private bool _hasWrench;

        private readonly List<string> _collectedLogIds = new List<string>();
        private readonly List<string> _collectedCacheIds = new List<string>();

        // ─── Properties ──────────────────────────────────────────────────────

        public int  Bandages  => _bandages;
        public int  Batteries => _batteries;
        public int  Decoys    => _decoys;
        public bool HasWrench => _hasWrench;
        public IReadOnlyList<string> CollectedLogs => _collectedLogIds;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _bandages  = _startingBandages;
            _batteries = _startingBatteries;
            _decoys    = _startingDecoys;
            _hasWrench = _startWithWrench;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerDamagedEvent>(OnPlayerDamaged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerDamagedEvent>(OnPlayerDamaged);
        }

        private void Update()
        {
            HandleItemHotkeys();
        }

        // ─── Hotkeys ────────────────────────────────────────────────────────

        private void HandleItemHotkeys()
        {
            // Bandage: Q
            if (Input.GetKeyDown(KeyCode.Q))
                TryUseBandage();

            // Battery swap: B
            if (Input.GetKeyDown(KeyCode.B))
                TrySwapBattery();

            // Throw decoy: G
            if (Input.GetKeyDown(KeyCode.G))
                TryThrowDecoy();
        }

        // ─── Bandage ─────────────────────────────────────────────────────────

        public bool TryUseBandage()
        {
            if (_bandages <= 0) return false;

            var player = Actors.PlayerController.Instance;
            if (player == null) return false;

            _bandages--;
            EventBus.Publish(new PlayerHealedEvent { Amount = _bandageHealAmount });

            // Using bandage while Nurse is active is risky — she targets injured players
            // Recovery clears bleed condition
            EventBus.Publish(new ConditionAppliedEvent
            {
                ConditionType = ConditionType.Bleed,
                Duration      = 0f,
                Magnitude     = 0f
            });

            Debug.Log($"[ResourceManager] Bandage used. Remaining: {_bandages}");
            return true;
        }

        // ─── Battery ─────────────────────────────────────────────────────────

        public bool TrySwapBattery()
        {
            if (_batteries <= 0) return false;

            var player = Actors.PlayerController.Instance;
            if (player == null) return false;

            _batteries--;
            // Restore flashlight battery via direct field — PlayerController exposes this
            // through a public method in production; here via event signal
            EventBus.Publish(new BatteryReplacedEvent { RestoreAmount = 100f });
            Debug.Log($"[ResourceManager] Battery swapped. Remaining: {_batteries}");
            return true;
        }

        // ─── Decoy ───────────────────────────────────────────────────────────

        public bool TryThrowDecoy()
        {
            if (_decoys <= 0) return false;

            var player = Actors.PlayerController.Instance;
            if (player == null) return false;

            _decoys--;

            // Throw in look direction
            var cam   = Camera.main;
            var pos   = cam != null ? cam.transform.position + cam.transform.forward * 6f : transform.position;
            SpawnDecoyNoise(pos);
            Debug.Log($"[ResourceManager] Decoy thrown. Remaining: {_decoys}");
            return true;
        }

        private void SpawnDecoyNoise(Vector3 position)
        {
            EventBus.Publish(new PlayerNoiseEvent
            {
                Origin = position,
                Radius = 18f,
                Type   = NoiseType.Decoy
            });
            AIManager.Instance?.BroadcastNoiseEvent(new PlayerNoiseEvent
            {
                Origin = position,
                Radius = 18f,
                Type   = NoiseType.Decoy
            });
        }

        // ─── Cache Collection ────────────────────────────────────────────────

        public void CollectCache(ItemCache cache)
        {
            if (cache == null || _collectedCacheIds.Contains(cache.CacheId)) return;

            _collectedCacheIds.Add(cache.CacheId);
            AddBandages(cache.Bandages);
            AddBatteries(cache.Batteries);
            AddDecoys(cache.Decoys);
            if (cache.ContainsWrench) _hasWrench = true;

            cache.SetCollected();
            Debug.Log($"[ResourceManager] Collected cache: {cache.CacheId}");
        }

        // ─── Lore Logs ───────────────────────────────────────────────────────

        public void CollectLog(LoreLogData log)
        {
            if (log == null || _collectedLogIds.Contains(log.LogId)) return;
            _collectedLogIds.Add(log.LogId);
            Debug.Log($"[ResourceManager] Collected log: {log.LogId} — {log.Title}");
        }

        // ─── Add Helpers ────────────────────────────────────────────────────

        public void AddBandages(int count)  => _bandages  = Mathf.Min(_bandages  + count, _maxBandages);
        public void AddBatteries(int count) => _batteries = Mathf.Min(_batteries + count, _maxBatteries);
        public void AddDecoys(int count)    => _decoys    = Mathf.Min(_decoys    + count, _maxDecoys);

        // ─── Softlock Prevention ────────────────────────────────────────────

        /// <summary>
        /// Called by progression checks — guarantees the player never runs out
        /// of resources required to complete a mandatory objective.
        /// </summary>
        public void EnsureMinimumViability()
        {
            if (_batteries <= 0 && _bandages <= 0)
            {
                _bandages  = 1;
                _batteries = 1;
                Debug.Log("[ResourceManager] Minimum viability enforced — 1 bandage + 1 battery restored.");
            }
        }

        // ─── Event Handlers ──────────────────────────────────────────────────

        private void OnPlayerDamaged(PlayerDamagedEvent evt)
        {
            EnsureMinimumViability();
        }

        // ─── Save/Load ───────────────────────────────────────────────────────

        public ResourceSaveData ExportState() => new ResourceSaveData
        {
            Bandages         = _bandages,
            Batteries        = _batteries,
            Decoys           = _decoys,
            HasWrench        = _hasWrench,
            CollectedLogIds  = new List<string>(_collectedLogIds),
            CollectedCacheIds = new List<string>(_collectedCacheIds)
        };

        public void ImportState(ResourceSaveData data)
        {
            _bandages  = data.Bandages;
            _batteries = data.Batteries;
            _decoys    = data.Decoys;
            _hasWrench = data.HasWrench;
            _collectedLogIds.Clear();
            _collectedLogIds.AddRange(data.CollectedLogIds ?? new List<string>());
            _collectedCacheIds.Clear();
            _collectedCacheIds.AddRange(data.CollectedCacheIds ?? new List<string>());
        }
    }

    // ─── Supporting Types ────────────────────────────────────────────────────

    public struct BatteryReplacedEvent
    {
        public float RestoreAmount;
    }

    [System.Serializable]
    public class ResourceSaveData
    {
        public int           Bandages;
        public int           Batteries;
        public int           Decoys;
        public bool          HasWrench;
        public List<string>  CollectedLogIds;
        public List<string>  CollectedCacheIds;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ITEM CACHE — World-placed resource pickup
// ─────────────────────────────────────────────────────────────────────────────

namespace Eidolon.World
{
    public class ItemCache : MonoBehaviour
    {
        [Header("Cache Identity")]
        [SerializeField] private string _cacheId;

        [Header("Contents")]
        [SerializeField] public int  Bandages  = 0;
        [SerializeField] public int  Batteries = 0;
        [SerializeField] public int  Decoys    = 0;
        [SerializeField] public bool ContainsWrench = false;

        [Header("Visual")]
        [SerializeField] private GameObject _intactVisuals;
        [SerializeField] private GameObject _emptyVisuals;

        public string CacheId   => _cacheId;
        public bool   IsCollected { get; private set; }

        private void OnTriggerEnter(Collider other)
        {
            if (IsCollected || !other.CompareTag("Player")) return;
            Core.ResourceManager.Instance?.CollectCache(this);
        }

        public void SetCollected()
        {
            IsCollected = true;
            if (_intactVisuals) _intactVisuals.SetActive(false);
            if (_emptyVisuals)  _emptyVisuals.SetActive(true);
        }
    }

    // ─── Lore Log Pickup ────────────────────────────────────────────────────

    public class LoreLogPickup : MonoBehaviour
    {
        [SerializeField] private Data.LoreLogData _logData;
        [SerializeField] private GameObject       _visual;

        private bool _collected;

        private void OnTriggerEnter(Collider other)
        {
            if (_collected || !other.CompareTag("Player")) return;
            _collected = true;
            Core.ResourceManager.Instance?.CollectLog(_logData);
            if (_visual) _visual.SetActive(false);
        }
    }
}
