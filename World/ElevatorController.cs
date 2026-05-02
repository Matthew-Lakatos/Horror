using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Eidolon.Core;

namespace Eidolon.World
{
    /// <summary>
    /// Building A Central Elevator.
    /// 
    /// Three floors: Basement (0), Ground (1), Mezzanine (2).
    /// 
    /// Rules:
    ///   - Requires ElevatorPass item to use at all (optional discovery).
    ///   - Each floor button is independently locked until the corresponding
    ///     key item is acquired in the main gameplay loop.
    ///   - Ground floor (index 1) is always unlocked — it is the starting floor.
    ///   - Generates PlayerNoiseEvent while moving (unlubricated, loud machinery).
    ///   - FacilityBrain can apply a slow debuff via the inherited pattern.
    ///   - Carries the player by parenting them to the cabin while inside.
    ///   - Doors close before moving, open on arrival.
    /// </summary>
    public class ElevatorController : MonoBehaviour
    {
        // ─── Identity ────────────────────────────────────────────────────────

        [Header("Identity")]
        [SerializeField] private string _elevatorId = "elevator_building_a";

        // ─── Floor Configuration ─────────────────────────────────────────────

        [Header("Floors — must match index order: 0=Basement, 1=Ground, 2=Mezzanine")]
        [SerializeField] private List<ElevatorFloorConfig> _floors = new List<ElevatorFloorConfig>();

        // ─── Cabin ───────────────────────────────────────────────────────────

        [Header("Cabin")]
        [SerializeField] private Transform  _cabin;              // The moving platform
        [SerializeField] private float      _normalSpeed   = 1.8f;
        [SerializeField] private float      _slowMultiplier = 0.35f;
        [SerializeField] private float      _doorOpenTime  = 1.2f;
        [SerializeField] private Animator   _doorAnimator;
        private static readonly int _animOpen  = Animator.StringToHash("Open");
        private static readonly int _animClose = Animator.StringToHash("Close");

        // ─── Noise ───────────────────────────────────────────────────────────

        [Header("Noise (unlubricated machinery)")]
        [SerializeField] private float _noiseEmitInterval = 0.6f;   // noise pulse while moving
        [SerializeField] private float _noiseRadius        = 20f;    // enemies hear this from far
        [SerializeField] private float _arrivalNoiseRadius = 15f;    // clunk on floor arrival

        // ─── Audio ───────────────────────────────────────────────────────────

        [Header("Audio")]
        [SerializeField] private AudioSource _motorSource;
        [SerializeField] private AudioSource _mechanicalSource;
        [SerializeField] private AudioClip   _motorLoopClip;         // constant whirring
        [SerializeField] private AudioClip   _grindClip;             // unlubricated squeal
        [SerializeField] private AudioClip   _arrivalClunkClip;      // heavy floor arrival
        [SerializeField] private AudioClip   _doorOpenClip;
        [SerializeField] private AudioClip   _doorCloseClip;
        [SerializeField] private AudioClip   _deniedClip;            // access denied beep
        [SerializeField] private AudioClip   _acceptedClip;          // access granted ding

        // ─── Access Denied UI ────────────────────────────────────────────────

        [Header("Panel UI")]
        [SerializeField] private UnityEngine.UI.Text _statusText;
        [SerializeField] private List<ElevatorButtonUI> _buttonUIs = new List<ElevatorButtonUI>();

        // ─── Runtime State ───────────────────────────────────────────────────

        private int   _currentFloor    = 1; // start on Ground
        private int   _targetFloor     = 1;
        private bool  _isMoving;
        private bool  _isSlowed;
        private float _slowExpiry;
        private float _noiseTimer;
        private bool  _playerInside;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            // Validate floor list
            if (_floors.Count != 3)
                Debug.LogError("[ElevatorController] Exactly 3 floor configs required " +
                               "(Basement, Ground, Mezzanine).");

            // Snap cabin to starting floor
            if (_cabin != null && _floors.Count > _currentFloor)
                _cabin.position = _floors[_currentFloor].CabinPosition;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<KeyItemAcquiredEvent>(OnKeyItemAcquired);
            EventBus.Subscribe<FacilityBrainActionEvent>(OnFacilityAction);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<KeyItemAcquiredEvent>(OnKeyItemAcquired);
            EventBus.Unsubscribe<FacilityBrainActionEvent>(OnFacilityAction);
        }

        private void Start()
        {
            RefreshButtonUI();
            SetStatus("EIDOLON INDUSTRIAL\nELEVATOR SYSTEM");
        }

        private void Update()
        {
            if (_isSlowed && Time.time >= _slowExpiry)
                _isSlowed = false;

            if (_isMoving)
                TickMovement();
        }

        // ─── Public Call Interface ────────────────────────────────────────────

        /// <summary>
        /// Called by a button press (floor index 0/1/2).
        /// Validates pass + floor lock before moving.
        /// </summary>
        public void RequestFloor(int floorIndex)
        {
            if (_isMoving)
            {
                SetStatus("IN TRANSIT");
                return;
            }
            if (floorIndex == _currentFloor) return;

            // Gate 1: ElevatorPass required
            if (!KeyItemManager.Instance.HasKey(KeyItemType.ElevatorPass))
            {
                PlayClip(_mechanicalSource, _deniedClip);
                SetStatus("ACCESS DENIED\nELEVATOR PASS REQUIRED");
                EventBus.Publish(new ElevatorCalledEvent
                {
                    ElevatorId  = _elevatorId,
                    TargetFloor = floorIndex,
                    WasGranted  = false
                });
                return;
            }

            // Gate 2: Floor must be unlocked
            if (!_floors[floorIndex].IsUnlocked)
            {
                PlayClip(_mechanicalSource, _deniedClip);
                SetStatus($"FLOOR {_floors[floorIndex].DisplayName} RESTRICTED\n" +
                          $"{_floors[floorIndex].LockHint}");
                EventBus.Publish(new ElevatorCalledEvent
                {
                    ElevatorId  = _elevatorId,
                    TargetFloor = floorIndex,
                    WasGranted  = false
                });
                return;
            }

            // Granted
            PlayClip(_mechanicalSource, _acceptedClip);
            StartCoroutine(TravelSequence(floorIndex));
            EventBus.Publish(new ElevatorCalledEvent
            {
                ElevatorId  = _elevatorId,
                TargetFloor = floorIndex,
                WasGranted  = true
            });
        }

        // ─── Travel Sequence ─────────────────────────────────────────────────

        private IEnumerator TravelSequence(int targetFloor)
        {
            _targetFloor = targetFloor;
            _isMoving    = true;

            // 1. Close doors
            SetStatus("DOORS CLOSING...");
            PlayClip(_mechanicalSource, _doorCloseClip);
            if (_doorAnimator) _doorAnimator.SetTrigger(_animClose);
            yield return new WaitForSeconds(_doorOpenTime);

            // 2. Parent player to cabin so they travel with it
            if (_playerInside)
                ParentPlayerToCabin(true);

            // 3. Start motor audio
            if (_motorSource != null && _motorLoopClip != null)
            {
                _motorSource.clip = _motorLoopClip;
                _motorSource.loop = true;
                _motorSource.Play();
            }

            SetStatus($"TRAVELLING TO {_floors[targetFloor].DisplayName}...");

            // 4. Movement handled in TickMovement() — wait until arrived
            yield return new WaitUntil(() => !_isMoving || HasReachedTarget());

            // 5. Stop motor
            _motorSource?.Stop();
            _isMoving = false;

            // 6. Arrival clunk + noise burst
            PlayClip(_mechanicalSource, _arrivalClunkClip);
            EmitNoise(_cabin.position, _arrivalNoiseRadius, NoiseType.Collision);

            // 7. Unparent player
            if (_playerInside)
                ParentPlayerToCabin(false);

            // 8. Update floor
            _currentFloor = targetFloor;

            // 9. Open doors
            SetStatus($"FLOOR {_floors[_currentFloor].DisplayName}");
            PlayClip(_mechanicalSource, _doorOpenClip);
            if (_doorAnimator) _doorAnimator.SetTrigger(_animOpen);
            yield return new WaitForSeconds(_doorOpenTime);

            RefreshButtonUI();
        }

        // ─── Movement Tick ───────────────────────────────────────────────────

        private void TickMovement()
        {
            if (_cabin == null || _floors.Count <= _targetFloor) return;

            Vector3 target = _floors[_targetFloor].CabinPosition;
            float   speed  = _isSlowed ? _normalSpeed * _slowMultiplier : _normalSpeed;

            _cabin.position = Vector3.MoveTowards(_cabin.position, target, speed * Time.deltaTime);

            // Periodic noise while moving
            _noiseTimer += Time.deltaTime;
            if (_noiseTimer >= _noiseEmitInterval)
            {
                _noiseTimer = 0f;
                EmitNoise(_cabin.position, _noiseRadius, NoiseType.Collision);

                // Random grind audio (unlubricated squeal)
                if (_mechanicalSource != null && _grindClip != null && Random.value < 0.4f)
                    _mechanicalSource.PlayOneShot(_grindClip, Random.Range(0.3f, 0.7f));
            }
        }

        private bool HasReachedTarget()
        {
            if (_cabin == null || _floors.Count <= _targetFloor) return true;
            return Vector3.Distance(_cabin.position, _floors[_targetFloor].CabinPosition) < 0.05f;
        }

        // ─── Player Carry ────────────────────────────────────────────────────

        private void ParentPlayerToCabin(bool parent)
        {
            var player = Actors.PlayerController.Instance;
            if (player == null || _cabin == null) return;
            player.transform.SetParent(parent ? _cabin : null, worldPositionStays: true);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player")) _playerInside = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                _playerInside = false;
                ParentPlayerToCabin(false); // safety unparent
            }
        }

        // ─── Noise Emission ──────────────────────────────────────────────────

        private void EmitNoise(Vector3 origin, float radius, NoiseType type)
        {
            var evt = new PlayerNoiseEvent { Origin = origin, Radius = radius, Type = type };
            EventBus.Publish(evt);
            AIManager.Instance?.BroadcastNoiseEvent(evt);
        }

        // ─── Floor Unlock ────────────────────────────────────────────────────

        /// <summary>Unlocks a floor by index. Called by FloorLockSystem.</summary>
        public void UnlockFloor(int floorIndex)
        {
            if (floorIndex < 0 || floorIndex >= _floors.Count) return;
            _floors[floorIndex].IsUnlocked = true;
            RefreshButtonUI();
            Debug.Log($"[ElevatorController] Floor {_floors[floorIndex].DisplayName} unlocked.");
        }

        // ─── FacilityBrain Slow ──────────────────────────────────────────────

        public void ApplySlow(float duration)
        {
            _isSlowed   = true;
            _slowExpiry = Time.time + duration;
        }

        // ─── UI Helpers ──────────────────────────────────────────────────────

        private void RefreshButtonUI()
        {
            bool hasPass = KeyItemManager.Instance?.HasKey(KeyItemType.ElevatorPass) ?? false;

            for (int i = 0; i < _buttonUIs.Count && i < _floors.Count; i++)
            {
                var btn = _buttonUIs[i];
                if (btn == null) continue;

                bool accessible = hasPass && _floors[i].IsUnlocked;
                btn.SetState(
                    accessible:    accessible,
                    isCurrent:     i == _currentFloor,
                    displayName:   _floors[i].DisplayName,
                    lockedReason:  !hasPass ? "PASS REQUIRED" : _floors[i].LockHint
                );
            }
        }

        private void SetStatus(string message)
        {
            if (_statusText) _statusText.text = message;
        }

        private void PlayClip(AudioSource source, AudioClip clip)
        {
            if (source && clip) source.PlayOneShot(clip);
        }

        // ─── Event Handlers ──────────────────────────────────────────────────

        private void OnKeyItemAcquired(KeyItemAcquiredEvent evt)
        {
            switch (evt.ItemType)
            {
                case KeyItemType.SecurityKeycard:
                    UnlockFloor(2); // Mezzanine
                    break;
                case KeyItemType.UtilityKey:
                    UnlockFloor(0); // Basement
                    break;
                case KeyItemType.ElevatorPass:
                    RefreshButtonUI();
                    SetStatus("ACCESS GRANTED\nWELCOME");
                    break;
            }
        }

        private void OnFacilityAction(FacilityBrainActionEvent evt)
        {
            if (evt.ActionType == FacilityActionType.SlowLift &&
                (evt.TargetId == _elevatorId || evt.TargetId == null))
                ApplySlow(15f);
        }

        // ─── Save / Load ─────────────────────────────────────────────────────

        public ElevatorSaveData ExportState() => new ElevatorSaveData
        {
            CurrentFloor    = _currentFloor,
            UnlockedFloors  = GetUnlockedFloorList()
        };

        public void ImportState(ElevatorSaveData data)
        {
            _currentFloor = data.CurrentFloor;
            if (_cabin != null && _floors.Count > _currentFloor)
                _cabin.position = _floors[_currentFloor].CabinPosition;

            foreach (int idx in data.UnlockedFloors)
                if (idx >= 0 && idx < _floors.Count)
                    _floors[idx].IsUnlocked = true;

            RefreshButtonUI();
        }

        private List<int> GetUnlockedFloorList()
        {
            var result = new List<int>();
            for (int i = 0; i < _floors.Count; i++)
                if (_floors[i].IsUnlocked) result.Add(i);
            return result;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ELEVATOR FLOOR CONFIG — Inspector data per floor
    // ─────────────────────────────────────────────────────────────────────────

    [System.Serializable]
    public class ElevatorFloorConfig
    {
        public string      DisplayName;    // "BASEMENT", "GROUND FLOOR", "MEZZANINE"
        public Vector3     CabinPosition;  // World position the cabin stops at
        public bool        IsUnlocked;     // Set ground floor true by default in inspector
        [TextArea(1, 2)]
        public string      LockHint;       // e.g. "UTILITY KEY REQUIRED"
        public Transform   ExitPoint;      // Where player walks out on this floor
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ELEVATOR BUTTON UI — Per-button panel component
    // Attach to each physical button object in the elevator cabin or lobby panel
    // ─────────────────────────────────────────────────────────────────────────

    public class ElevatorButtonUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UnityEngine.UI.Text   _labelText;
        [SerializeField] private UnityEngine.UI.Image  _buttonImage;
        [SerializeField] private int                   _floorIndex;

        [Header("Colours")]
        [SerializeField] private Color _unlockedColor = new Color(0.2f, 0.8f, 0.2f);
        [SerializeField] private Color _lockedColor   = new Color(0.8f, 0.1f, 0.1f);
        [SerializeField] private Color _currentColor  = new Color(1.0f, 0.85f, 0.1f);

        private ElevatorController _elevator;

        private void Awake()
            => _elevator = GetComponentInParent<ElevatorController>();

        // E key or trigger interaction
        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.E)) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (Physics.Raycast(cam.transform.position, cam.transform.forward,
                    out RaycastHit hit, 2.5f) && hit.collider.gameObject == gameObject)
                _elevator?.RequestFloor(_floorIndex);
        }

        public void SetState(bool accessible, bool isCurrent, string displayName, string lockedReason)
        {
            if (_labelText)
                _labelText.text = isCurrent
                    ? $"► {displayName}"
                    : accessible ? displayName : $"[LOCKED]\n{lockedReason}";

            if (_buttonImage)
                _buttonImage.color = isCurrent   ? _currentColor
                                   : accessible  ? _unlockedColor
                                   : _lockedColor;
        }
    }

    // ─── Save Data ───────────────────────────────────────────────────────────

    [System.Serializable]
    public class ElevatorSaveData
    {
        public int        CurrentFloor;
        public List<int>  UnlockedFloors;
    }
}
