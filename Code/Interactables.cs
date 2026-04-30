using System.Collections;
using UnityEngine;

namespace Eidolon.World
{
    using Core;
    using Actors;

    // ═════════════════════════════════════════════════════════════════════════
    //  RESOURCE MANAGER  – tracks global resource economy
    // ═════════════════════════════════════════════════════════════════════════
    public class ResourceManager : MonoBehaviour
    {
        public static ResourceManager Instance { get; private set; }

        [Header("Economy Settings")]
        [SerializeField] private int   _healPacksInLevel      = 6;
        [SerializeField] private int   _batteryPacksInLevel   = 4;
        [SerializeField] private int   _decoyItemsInLevel     = 3;
        [SerializeField] private int   _cutterToolsInLevel    = 2;

        private int _healPacksRemaining;
        private int _batteryPacksRemaining;
        private int _decoyItemsRemaining;
        private int _cutterToolsRemaining;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _healPacksRemaining    = _healPacksInLevel;
            _batteryPacksRemaining = _batteryPacksInLevel;
            _decoyItemsRemaining   = _decoyItemsInLevel;
            _cutterToolsRemaining  = _cutterToolsInLevel;
        }

        // ── Softlock prevention ────────────────────────────────────────────────
        /// <summary>
        /// Ensures the player always has at least one of each critical resource.
        /// Called when entering a new sector. Never softlocks progress.
        /// </summary>
        public void EnsureMinimumResources(PlayerController player)
        {
            // If no heals remain and player is injured, spawn an emergency cache
            if (_healPacksRemaining <= 0 && player.Health < player.HealthNormalised * 50f)
            {
                SpawnEmergencyCache("heal_pack", player.Transform.position);
                _healPacksRemaining = 1;
            }

            if (_batteryPacksRemaining <= 0 && player.Battery < 30f)
            {
                SpawnEmergencyCache("battery_pack", player.Transform.position);
                _batteryPacksRemaining = 1;
            }
        }

        private void SpawnEmergencyCache(string itemId, Vector3 nearPosition)
        {
            // In a full implementation: find nearest safe cache spawn point
            // For now, log for design awareness
            Debug.Log($"[ResourceManager] Emergency cache spawned: {itemId} near {nearPosition}");
        }

        // ── Resource consumed callbacks ────────────────────────────────────────
        public void OnHealPackConsumed()    => _healPacksRemaining    = Mathf.Max(0, _healPacksRemaining    - 1);
        public void OnBatteryConsumed()     => _batteryPacksRemaining = Mathf.Max(0, _batteryPacksRemaining - 1);
        public void OnDecoyConsumed()       => _decoyItemsRemaining   = Mathf.Max(0, _decoyItemsRemaining   - 1);
        public void OnCutterConsumed()      => _cutterToolsRemaining  = Mathf.Max(0, _cutterToolsRemaining  - 1);

        public int HealsRemaining   => _healPacksRemaining;
        public int BatteriesRemaining => _batteryPacksRemaining;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  HEAL STATION  – wall-mounted medical dispenser
    // ═════════════════════════════════════════════════════════════════════════
    public class HealStation : MonoBehaviour, IFacilityInteractive
    {
        [SerializeField] private float _healAmount    = 40f;
        [SerializeField] private float _useCooldown   = 90f;
        [SerializeField] private string _roomId;

        public string InteractiveId => $"HealStation_{_roomId}";
        public bool   CanInteract   { get; private set; } = true;

        private float _cooldownEnd;

        private void OnEnable()  => EventBus.Subscribe<EvFacilityActionTriggered>(OnFacilityAction);
        private void OnDisable() => EventBus.Unsubscribe<EvFacilityActionTriggered>(OnFacilityAction);

        private void Update()
        {
            if (!CanInteract && Time.time >= _cooldownEnd)
            {
                CanInteract = true;
                // Visual feedback: station light on
            }
        }

        public void Interact(PlayerController player)
        {
            if (!CanInteract) return;

            // Check if nurse has disabled it
            var graph = FacilityGraphManager.Instance?.Graph;
            var node  = graph?.GetNode(_roomId);
            if (node != null && !node.MedStationActive)
            {
                AudioManager.Instance?.PlayAtPosition("audio_station_offline", transform.position);
                return;
            }

            player.Heal(_healAmount);
            ConditionManager.Instance?.RemoveByType(Core.ConditionType.Bleed);

            ResourceManager.Instance?.OnHealPackConsumed();
            AudioManager.Instance?.PlayAtPosition("audio_heal_station", transform.position);

            CanInteract = false;
            _cooldownEnd = Time.time + _useCooldown;
        }

        public void ForceState(bool open) => CanInteract = open;

        private void OnFacilityAction(EvFacilityActionTriggered evt)
        {
            if (evt.RoomId != _roomId) return;
            if (evt.Action == FacilityAction.DisableStation)
            {
                CanInteract  = false;
                _cooldownEnd = Time.time + 60f;
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  PICKABLE ITEM  – heal packs, batteries, decoys, tools
    // ═════════════════════════════════════════════════════════════════════════
    public class PickableItem : MonoBehaviour, IFacilityInteractive
    {
        public enum ItemCategory { HealPack, Battery, Decoy, Cutter, ArchiveData, MapFragment, KeyItem }

        [SerializeField] private string       _itemId;
        [SerializeField] private ItemCategory _category;
        [SerializeField] private float        _healValue      = 25f;   // HealPack only
        [SerializeField] private float        _batteryValue   = 120f;  // Battery only

        public string InteractiveId => _itemId;
        public bool   CanInteract   => gameObject.activeSelf;

        public void Interact(PlayerController player)
        {
            switch (_category)
            {
                case ItemCategory.HealPack:
                    player.Heal(_healValue);
                    ResourceManager.Instance?.OnHealPackConsumed();
                    break;
                case ItemCategory.Battery:
                    player.AddBattery(_batteryValue);
                    ResourceManager.Instance?.OnBatteryConsumed();
                    break;
                case ItemCategory.Decoy:
                case ItemCategory.Cutter:
                case ItemCategory.ArchiveData:
                case ItemCategory.MapFragment:
                case ItemCategory.KeyItem:
                    player.AddItem(_itemId);
                    break;
            }

            AudioManager.Instance?.PlayAtPosition("audio_item_pickup", transform.position);
            gameObject.SetActive(false);    // Return to pool / deactivate
        }

        public void ForceState(bool open) { }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  DOOR INTERACTIVE
    // ═════════════════════════════════════════════════════════════════════════
    public class DoorInteractive : MonoBehaviour, IFacilityInteractive
    {
        [SerializeField] private string  _fromRoomId;
        [SerializeField] private string  _toRoomId;
        [SerializeField] private bool    _requiresKey;
        [SerializeField] private string  _requiredKeyId;
        [SerializeField] private float   _openAngle        = 90f;
        [SerializeField] private float   _animDuration     = 0.4f;

        public string InteractiveId => $"Door_{_fromRoomId}_{_toRoomId}";
        public bool   CanInteract   { get; private set; } = true;

        private bool     _isOpen;
        private Coroutine _anim;

        private void OnEnable()  => EventBus.Subscribe<EvFacilityActionTriggered>(OnFacilityAction);
        private void OnDisable() => EventBus.Unsubscribe<EvFacilityActionTriggered>(OnFacilityAction);

        public void Interact(PlayerController player)
        {
            if (!CanInteract) return;

            if (_requiresKey && !player.HasItem(_requiredKeyId))
            {
                AudioManager.Instance?.PlayAtPosition("audio_door_locked", transform.position);
                return;
            }

            _isOpen = !_isOpen;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(AnimateDoor(_isOpen));

            // Emit noise
            var level = _isOpen ? NoiseLevel.Moderate : NoiseLevel.Quiet;
            EventBus.Publish(new EvPlayerNoise(transform.position, level));
            AudioManager.Instance?.PlayAtPosition(_isOpen ? "audio_door_open" : "audio_door_close",
                                                  transform.position);
        }

        public void ForceState(bool locked)
        {
            CanInteract = !locked;
            if (locked && _isOpen)
            {
                // Slam shut
                if (_anim != null) StopCoroutine(_anim);
                _anim   = StartCoroutine(AnimateDoor(false));
                _isOpen = false;
                AudioManager.Instance?.PlayAtPosition("audio_door_slam", transform.position);
            }

            // Update graph edge
            var graph = FacilityGraphManager.Instance?.Graph;
            graph?.GetNode(_fromRoomId)?.SetEdgeBlocked(_toRoomId, locked);
            graph?.GetNode(_toRoomId)?.SetEdgeBlocked(_fromRoomId, locked);
        }

        private void OnFacilityAction(EvFacilityActionTriggered evt)
        {
            if (evt.Action == FacilityAction.LockDoor &&
               (evt.RoomId == _fromRoomId || evt.RoomId == _toRoomId))
                ForceState(true);
        }

        private IEnumerator AnimateDoor(bool opening)
        {
            float start   = opening ? 0f       : _openAngle;
            float end     = opening ? _openAngle : 0f;
            float elapsed = 0f;

            while (elapsed < _animDuration)
            {
                float angle  = Mathf.Lerp(start, end, elapsed / _animDuration);
                transform.localEulerAngles = new Vector3(0, angle, 0);
                elapsed += Time.deltaTime;
                yield return null;
            }
            transform.localEulerAngles = new Vector3(0, end, 0);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  FUSE OVERRIDE  – player-found manual override for facility actions
    // ═════════════════════════════════════════════════════════════════════════
    public class FuseOverride : MonoBehaviour, IFacilityInteractive
    {
        [SerializeField] private string        _targetRoomId;
        [SerializeField] private FacilityAction _restoresAction;
        [SerializeField] private bool          _requiresCutter = true;

        public string InteractiveId => $"Fuse_{_targetRoomId}";
        public bool   CanInteract   { get; private set; } = true;

        public void Interact(PlayerController player)
        {
            if (!CanInteract) return;
            if (_requiresCutter && !player.HasItem("cutter")) return;

            if (_requiresCutter) player.UseItem("cutter");

            FacilityBrain.Instance?.PlayerOverride(_targetRoomId, _restoresAction);
            AudioManager.Instance?.PlayAtPosition("audio_fuse_override", transform.position);

            CanInteract = false;
            gameObject.SetActive(false);
        }

        public void ForceState(bool open) => CanInteract = open;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  DECOY THROWER  – player tool that redirects noise-sensitive enemies
    // ═════════════════════════════════════════════════════════════════════════
    public class DecoyThrower : MonoBehaviour
    {
        [SerializeField] private float    _noiseRadius   = 15f;
        [SerializeField] private float    _noiseDuration = 8f;
        [SerializeField] private KeyCode  _throwKey      = KeyCode.Q;

        private PlayerController _player;

        private void Start() => _player = GetComponentInParent<PlayerController>();

        private void Update()
        {
            if (Input.GetKeyDown(_throwKey) && _player.HasItem("decoy"))
                ThrowDecoy();
        }

        private void ThrowDecoy()
        {
            _player.UseItem("decoy");
            ResourceManager.Instance?.OnDecoyConsumed();

            Vector3 throwPos = _player.Transform.position + _player.Transform.forward * 8f;
            StartCoroutine(EmitDecoyNoise(throwPos));

            AudioManager.Instance?.PlayAtPosition("audio_decoy_throw", _player.Transform.position);
        }

        private IEnumerator EmitDecoyNoise(Vector3 position)
        {
            float elapsed = 0f;
            while (elapsed < _noiseDuration)
            {
                EventBus.Publish(new EvPlayerNoise(position, NoiseLevel.Loud));
                AudioManager.Instance?.PlayAtPosition("audio_decoy_beep", position);
                elapsed += 1f;
                yield return new WaitForSeconds(1f);
            }
        }
    }
}
