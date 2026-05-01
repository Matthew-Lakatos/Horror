using System.Collections;
using UnityEngine;
using Eidolon.Core;

namespace Eidolon.Actors
{
    /// <summary>
    /// First-person player controller.
    /// Handles: walk/crouch/sprint, stamina, health, injury states,
    /// flashlight, noise emission, hidden stress/trauma metrics.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        // ─── Movement Settings ───────────────────────────────────────────────

        [Header("Movement")]
        [SerializeField] private float _walkSpeed    = 3.5f;
        [SerializeField] private float _crouchSpeed  = 1.8f;
        [SerializeField] private float _sprintSpeed  = 7f;
        [SerializeField] private float _crouchHeight = 1.1f;
        [SerializeField] private float _standHeight  = 1.8f;
        [SerializeField] private float _gravity      = -18f;

        [Header("Look")]
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private float _mouseSensitivity = 2f;
        [SerializeField] private float _verticalLookClamp = 80f;

        // ─── Stamina ─────────────────────────────────────────────────────────

        [Header("Stamina")]
        [SerializeField] private float _maxStamina     = 100f;
        [SerializeField] private float _sprintDrain    = 20f;
        [SerializeField] private float _heatDrain      = 8f;
        [SerializeField] private float _staminaRegen   = 12f;
        [SerializeField] private float _minSprintStamina = 15f;

        // ─── Health ──────────────────────────────────────────────────────────

        [Header("Health")]
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private float _bleedRate = 2f;

        // ─── Noise ───────────────────────────────────────────────────────────

        [Header("Noise Emission")]
        [SerializeField] private float _walkNoiseRadius   = 5f;
        [SerializeField] private float _sprintNoiseRadius = 10f;
        [SerializeField] private float _crouchNoiseRadius = 1.5f;
        [SerializeField] private float _noiseEmitInterval = 0.4f;

        // ─── Flashlight ──────────────────────────────────────────────────────

        [Header("Flashlight")]
        [SerializeField] private Light  _flashlight;
        [SerializeField] private float  _maxBattery     = 100f;
        [SerializeField] private float  _batteryDrain   = 2f;

        // ─── Runtime State ───────────────────────────────────────────────────

        public float  Health          { get; private set; }
        public float  Stamina         { get; private set; }
        public float  Battery         { get; private set; }
        public bool   IsMoving        { get; private set; }
        public bool   IsSprinting     { get; private set; }
        public bool   IsCrouching     { get; private set; }
        public bool   IsAlive         { get; private set; } = true;
        public float  HiddenStress    { get; private set; } // 0-1
        public float  TraumaLevel     { get; private set; } // 0-1

        private CharacterController _cc;
        private float _verticalLook;
        private float _verticalVelocity;
        private float _noiseTimer;
        private bool  _flashlightOn;

        // Condition flags
        private bool  _isLimping;
        private bool  _isBleeding;
        private bool  _isLouderFootsteps;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _cc = GetComponent<CharacterController>();
            Health  = _maxHealth;
            Stamina = _maxStamina;
            Battery = _maxBattery;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerDamagedEvent>(OnDamaged);
            EventBus.Subscribe<PlayerHealedEvent>(OnHealed);
            EventBus.Subscribe<ConditionAppliedEvent>(OnConditionApplied);
            EventBus.Subscribe<RoomStateChangedEvent>(OnRoomStateChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerDamagedEvent>(OnDamaged);
            EventBus.Unsubscribe<PlayerHealedEvent>(OnHealed);
            EventBus.Unsubscribe<ConditionAppliedEvent>(OnConditionApplied);
            EventBus.Unsubscribe<RoomStateChangedEvent>(OnRoomStateChanged);
        }

        private void Update()
        {
            if (!IsAlive) return;

            HandleLook();
            HandleMovement();
            HandleFlashlight();
            HandleStamina();
            HandleBleeding();
            HandleNoiseEmission();
            UpdateStressMetrics();
        }

        // ─── Look ────────────────────────────────────────────────────────────

        private void HandleLook()
        {
            float mouseX = Input.GetAxis("Mouse X") * _mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * _mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX);
            _verticalLook = Mathf.Clamp(_verticalLook - mouseY, -_verticalLookClamp, _verticalLookClamp);
            if (_cameraTransform) _cameraTransform.localEulerAngles = Vector3.right * _verticalLook;
        }

        // ─── Movement ────────────────────────────────────────────────────────

        private void HandleMovement()
        {
            bool crouchInput  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
            bool sprintInput  = Input.GetKey(KeyCode.LeftShift) && Stamina > _minSprintStamina;

            IsCrouching = crouchInput;
            IsSprinting = sprintInput && !crouchInput;

            // Crouch height
            float targetHeight = IsCrouching ? _crouchHeight : _standHeight;
            _cc.height = Mathf.Lerp(_cc.height, targetHeight, 8f * Time.deltaTime);

            float speed = IsCrouching ? _crouchSpeed
                        : IsSprinting ? _sprintSpeed
                        : _walkSpeed;

            // Limp condition debuff
            if (_isLimping) speed *= 0.6f;

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            var  moveDir = transform.right * h + transform.forward * v;
            moveDir = Vector3.ClampMagnitude(moveDir, 1f);
            IsMoving = moveDir.sqrMagnitude > 0.01f;

            // Gravity
            if (_cc.isGrounded && _verticalVelocity < 0)
                _verticalVelocity = -2f;
            _verticalVelocity += _gravity * Time.deltaTime;

            var finalMove = moveDir * speed + Vector3.up * _verticalVelocity;
            _cc.Move(finalMove * Time.deltaTime);

            // Record room visit
            var roomId = GetCurrentRoomId();
            if (roomId != null)
                WorldStateManager.Instance?.RecordVisit(roomId);
        }

        // ─── Flashlight ──────────────────────────────────────────────────────

        private void HandleFlashlight()
        {
            if (Input.GetKeyDown(KeyCode.F) && Battery > 0)
                _flashlightOn = !_flashlightOn;

            if (_flashlight) _flashlight.enabled = _flashlightOn && Battery > 0;

            if (_flashlightOn && Battery > 0)
                Battery = Mathf.Max(0f, Battery - _batteryDrain * Time.deltaTime);
        }

        // ─── Stamina ────────────────────────────────────────────────────────

        private void HandleStamina()
        {
            bool inHeatRoom = IsInHeatedRoom();

            if (IsSprinting && IsMoving)
                Stamina = Mathf.Max(0f, Stamina - _sprintDrain * Time.deltaTime);
            else if (inHeatRoom)
                Stamina = Mathf.Max(0f, Stamina - _heatDrain * Time.deltaTime);
            else
                Stamina = Mathf.Min(_maxStamina, Stamina + _staminaRegen * Time.deltaTime);

            if (Stamina <= 0f) IsSprinting = false;
        }

        // ─── Bleeding ───────────────────────────────────────────────────────

        private void HandleBleeding()
        {
            if (!_isBleeding) return;
            TakeDamage(_bleedRate * Time.deltaTime, DamageSource.Environment);

            // Leave noise trail (blood sound effect / visual)
            if (IsMoving)
            {
                _noiseTimer += Time.deltaTime;
                // Bleed trail noise handled inside noise emission at louder radius
            }
        }

        // ─── Noise Emission ──────────────────────────────────────────────────

        private void HandleNoiseEmission()
        {
            if (!IsMoving) return;

            _noiseTimer += Time.deltaTime;
            if (_noiseTimer < _noiseEmitInterval) return;
            _noiseTimer = 0f;

            float baseRadius = IsSprinting ? _sprintNoiseRadius
                             : IsCrouching ? _crouchNoiseRadius
                             : _walkNoiseRadius;

            // Louder footsteps condition multiplier
            if (_isLouderFootsteps) baseRadius *= 1.6f;

            var noiseType = IsSprinting ? NoiseType.Sprint : NoiseType.Footstep;

            EventBus.Publish(new PlayerNoiseEvent
            {
                Origin = transform.position,
                Radius = baseRadius,
                Type   = noiseType
            });

            AIManager.Instance?.BroadcastNoiseEvent(new PlayerNoiseEvent
            {
                Origin = transform.position,
                Radius = baseRadius,
                Type   = noiseType
            });
        }

        // ─── Stress / Trauma ────────────────────────────────────────────────

        private void UpdateStressMetrics()
        {
            bool inDanger = AIManager.Instance?.AnyEnemyInChase() ?? false;
            bool inPanic  = GameDirector.Instance?.CurrentTension == TensionState.Panic;

            float stressTarget = 0f;
            if (inDanger) stressTarget += 0.6f;
            if (inPanic)  stressTarget += 0.3f;
            if (Health < _maxHealth * 0.35f) stressTarget += 0.2f;

            HiddenStress = Mathf.Clamp01(HiddenStress + (stressTarget - HiddenStress) * 2f * Time.deltaTime);
            TraumaLevel  = Mathf.Clamp01(TraumaLevel  + (HiddenStress * 0.1f - 0.02f) * Time.deltaTime);

            // Broadcast for PerceptionManager
            AIManager.Instance.SharedBlackboard.PlayerInPanic = HiddenStress > 0.7f;
        }

        // ─── Damage / Heal ───────────────────────────────────────────────────

        private void TakeDamage(float amount, DamageSource source)
        {
            if (!IsAlive) return;
            Health = Mathf.Max(0f, Health - amount);
            if (Health <= 0f) Die();
        }

        private void Die()
        {
            IsAlive   = false;
            IsMoving  = false;
            _cc.enabled = false;
            Debug.Log("[Player] Player died.");
        }

        // ─── Room Helpers ────────────────────────────────────────────────────

        private string _currentRoomId;

        private string GetCurrentRoomId() => _currentRoomId;

        private bool IsInHeatedRoom()
        {
            if (_currentRoomId == null) return false;
            var room = WorldStateManager.Instance?.GetRoom(_currentRoomId);
            return room?.IsHeated ?? false;
        }

        private void OnTriggerEnter(Collider other)
        {
            var room = other.GetComponent<World.RoomNode>();
            if (room != null) _currentRoomId = room.RoomId;
        }

        // ─── Event Handlers ─────────────────────────────────────────────────

        private void OnDamaged(PlayerDamagedEvent evt)
        {
            TakeDamage(evt.Amount, evt.Source);
            TraumaLevel = Mathf.Clamp01(TraumaLevel + 0.1f);
        }

        private void OnHealed(PlayerHealedEvent evt)
        {
            Health = Mathf.Min(_maxHealth, Health + evt.Amount);
            _isBleeding = false;
            AIManager.Instance.SharedBlackboard.PlayerIsInjured = Health < _maxHealth * 0.5f;
        }

        private void OnConditionApplied(ConditionAppliedEvent evt)
        {
            switch (evt.ConditionType)
            {
                case ConditionType.Limp:
                    _isLimping = true;
                    StartCoroutine(ClearConditionAfter(() => _isLimping = false, evt.Duration));
                    break;
                case ConditionType.Bleed:
                    _isBleeding = true;
                    StartCoroutine(ClearConditionAfter(() => _isBleeding = false, evt.Duration));
                    AIManager.Instance.SharedBlackboard.PlayerIsInjured = true;
                    break;
                case ConditionType.LouderFootsteps:
                    _isLouderFootsteps = true;
                    StartCoroutine(ClearConditionAfter(() => _isLouderFootsteps = false, evt.Duration));
                    break;
            }
        }

        private IEnumerator ClearConditionAfter(System.Action clear, float delay)
        {
            yield return new WaitForSeconds(delay);
            clear?.Invoke();
        }

        private void OnRoomStateChanged(RoomStateChangedEvent evt)
        {
            // Refresh heated state when room changes
        }

        // ─── Inventory / Item Use ────────────────────────────────────────────

        public bool TryHeal(World.MedStation station)
        {
            if (station == null || station.IsDisabled || station.IsEmpty) return false;
            if (!station.TryUse(out float amount)) return false;

            EventBus.Publish(new PlayerHealedEvent { Amount = amount });
            return true;
        }

        // ─── Save/Load ───────────────────────────────────────────────────────

        public PlayerSaveData ExportState() => new PlayerSaveData
        {
            Health       = Health,
            Stamina      = Stamina,
            Battery      = Battery,
            TraumaLevel  = TraumaLevel,
            Position     = transform.position,
            Rotation     = transform.eulerAngles
        };

        public void ImportState(PlayerSaveData data)
        {
            Health      = data.Health;
            Stamina     = data.Stamina;
            Battery     = data.Battery;
            TraumaLevel = data.TraumaLevel;
            _cc.enabled = false;
            transform.position = data.Position;
            transform.eulerAngles = data.Rotation;
            _cc.enabled = true;
        }
    }

    [System.Serializable]
    public class PlayerSaveData
    {
        public float   Health;
        public float   Stamina;
        public float   Battery;
        public float   TraumaLevel;
        public Vector3 Position;
        public Vector3 Rotation;
    }
}
