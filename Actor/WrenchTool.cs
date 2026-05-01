using System.Collections;
using UnityEngine;
using Eidolon.Core;
using Eidolon.World;
using Eidolon.AI;

namespace Eidolon.Actors
{
    /// <summary>
    /// Wrench system — player counter-agency tool against the hostile facility.
    /// Allows forced door entry, panel access, trap disabling, and lock-breaking.
    /// All uses generate noise and drain stamina. Includes optional durability/cooldown.
    /// </summary>
    public class WrenchTool : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _useRange        = 2.5f;
        [SerializeField] private float _staminaCost     = 20f;
        [SerializeField] private float _cooldown        = 3f;
        [SerializeField] private float _useTime         = 1.2f;

        [Header("Durability (optional)")]
        [SerializeField] private bool  _hasDurability   = false;
        [SerializeField] private int   _maxDurability   = 10;
        [SerializeField] private int   _currentDurability;

        [Header("Noise")]
        [SerializeField] private float _noiseRadius     = 12f;

        [Header("Audio")]
        [SerializeField] private AudioClip _wrenchSwingClip;
        [SerializeField] private AudioClip _wrenchHitClip;
        [SerializeField] private AudioClip _wrenchFailClip;
        [SerializeField] private AudioClip _wrenchBrokenClip;

        private float       _lastUseTime = -999f;
        private bool        _inProgress;
        private AudioSource _audioSource;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            _audioSource      = GetComponent<AudioSource>();
            _currentDurability = _maxDurability;
        }

        // ─── Use ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E) && !_inProgress)
                TryUseWrench();
        }

        public void TryUseWrench()
        {
            if (_inProgress) return;
            if (Time.time - _lastUseTime < _cooldown) return;
            if (_hasDurability && _currentDurability <= 0)
            {
                PlayClip(_wrenchBrokenClip);
                return;
            }

            var player = PlayerController.Instance;
            if (player == null || player.Stamina < _staminaCost)
            {
                PlayClip(_wrenchFailClip);
                return;
            }

            // Raycast for interactable target
            if (Camera.main == null) return;
            Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, _useRange)) return;

            var interactable = hit.collider.GetComponent<IWrenchInteractable>();
            if (interactable == null || !interactable.CanBeWrenched())
            {
                PlayClip(_wrenchFailClip);
                return;
            }

            StartCoroutine(ExecuteWrench(interactable, player));
        }

        // ─── Execution ───────────────────────────────────────────────────────

        private IEnumerator ExecuteWrench(IWrenchInteractable target, PlayerController player)
        {
            _inProgress = true;
            PlayClip(_wrenchSwingClip);

            // Time delay (loud, telegraphed)
            yield return new WaitForSeconds(_useTime);

            // Noise burst
            EventBus.Publish(new PlayerNoiseEvent
            {
                Origin = transform.position,
                Radius = _noiseRadius,
                Type   = NoiseType.Wrench
            });
            AIManager.Instance?.BroadcastNoiseEvent(new PlayerNoiseEvent
            {
                Origin = transform.position,
                Radius = _noiseRadius,
                Type   = NoiseType.Wrench
            });

            PlayClip(_wrenchHitClip);

            // Drain stamina
            // Direct manipulation — PlayerController exposes stamina drain via event
            EventBus.Publish(new ConditionAppliedEvent
            {
                ConditionType = ConditionType.StaminaDrain,
                Duration      = 2f,
                Magnitude     = _staminaCost / 100f
            });

            // Execute the interaction
            target.OnWrenched();

            // Durability
            if (_hasDurability)
                _currentDurability = Mathf.Max(0, _currentDurability - 1);

            _lastUseTime = Time.time;
            _inProgress  = false;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        public bool IsBroken => _hasDurability && _currentDurability <= 0;
        public int  Durability => _currentDurability;

        private void PlayClip(AudioClip clip)
        {
            if (_audioSource && clip) _audioSource.PlayOneShot(clip);
        }

        // ─── Save/Load ───────────────────────────────────────────────────────

        public WrenchSaveData ExportState() => new WrenchSaveData { Durability = _currentDurability };
        public void ImportState(WrenchSaveData data) => _currentDurability = data.Durability;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WRENCH INTERACTABLE INTERFACE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Implement on any object the player can interact with using the wrench.</summary>
    public interface IWrenchInteractable
    {
        bool CanBeWrenched();
        void OnWrenched();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WRENCH-COMPATIBLE DOOR (extends DoorController)
    // ─────────────────────────────────────────────────────────────────────────

    public class WrenchableDoor : DoorController, IWrenchInteractable
    {
        [SerializeField] private bool _canBeForced = true;

        public bool CanBeWrenched() => IsLocked && _canBeForced;

        public void OnWrenched() => ForceOpen();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MAINTENANCE PANEL — Wrench-openable access point
    // ─────────────────────────────────────────────────────────────────────────

    public class MaintenancePanel : MonoBehaviour, IWrenchInteractable
    {
        [SerializeField] private string    _panelId;
        [SerializeField] private Transform _revealedRoute;
        [SerializeField] private bool      _opened = false;
        [SerializeField] private AudioClip _openClip;

        private AudioSource _audioSource;

        private void Awake() => _audioSource = GetComponent<AudioSource>();

        public bool CanBeWrenched() => !_opened;

        public void OnWrenched()
        {
            if (_opened) return;
            _opened = true;
            if (_revealedRoute) _revealedRoute.gameObject.SetActive(true);
            if (_audioSource && _openClip) _audioSource.PlayOneShot(_openClip);
            Debug.Log($"[MaintenancePanel] {_panelId} opened via wrench");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TRAP DISABLER — Wrench can dismantle traps faster
    // ─────────────────────────────────────────────────────────────────────────

    public class WrenchDisarmableTrap : TrapInstance, IWrenchInteractable
    {
        public bool CanBeWrenched() => gameObject.activeSelf;

        public void OnWrenched()
        {
            // Disarm silently — no noise burst, no damage
            Debug.Log("[WrenchDisarmableTrap] Trap disarmed by wrench");
            Destroy(gameObject);
        }
    }

    [System.Serializable]
    public class WrenchSaveData
    {
        public int Durability;
    }
}
