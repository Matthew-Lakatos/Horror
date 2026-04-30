using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Actors
{
    using Core;

    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour, IEntity, IMovementProvider
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Movement")]
        [SerializeField] private float _walkSpeed   = 3.5f;
        [SerializeField] private float _crouchSpeed = 1.8f;
        [SerializeField] private float _sprintSpeed = 6.0f;
        [SerializeField] private float _gravity     = -9.81f;

        [Header("Camera")]
        [SerializeField] private Transform _cameraRoot;
        [SerializeField] private float     _mouseSensitivity = 2f;
        [SerializeField] private float     _crouchCameraY    = 0.9f;
        [SerializeField] private float     _standCameraY     = 1.7f;

        [Header("Stamina")]
        [SerializeField] private float _maxStamina          = 100f;
        [SerializeField] private float _staminaRegenRate    = 12f;
        [SerializeField] private float _sprintStaminaCost   = 18f;
        [SerializeField] private float _sprintMinStamina    = 15f;

        [Header("Health")]
        [SerializeField] private float _maxHealth = 100f;

        [Header("Flashlight")]
        [SerializeField] private Light  _flashlight;
        [SerializeField] private float  _maxBattery      = 300f;  // seconds
        [SerializeField] private float  _batteryDrainRate = 1f;

        [Header("Noise")]
        [SerializeField] private float _walkNoiseRadius   = 4f;
        [SerializeField] private float _sprintNoiseRadius = 9f;
        [SerializeField] private float _crouchNoiseRadius = 1.5f;
        [SerializeField] private float _noiseEmitInterval = 0.5f;

        // ── IEntity ────────────────────────────────────────────────────────────
        public string    EntityId  => "Player";
        public Transform Transform => transform;
        public bool      IsAlive   { get; private set; } = true;

        // ── IMovementProvider ─────────────────────────────────────────────────
        public float Speed       { get; private set; }
        public bool  IsGrounded  => _cc.isGrounded;

        // ── Public state ───────────────────────────────────────────────────────
        public float  Health          { get; private set; }
        public float  Stamina         { get; private set; }
        public float  Battery         { get; private set; }
        public bool   IsCrouching     { get; private set; }
        public bool   IsSprinting     { get; private set; }
        public bool   FlashlightOn    { get; private set; }
        public string CurrentRoomId   { get; set; }

        // ── Inventory ──────────────────────────────────────────────────────────
        private readonly List<string> _inventory = new();
        public  IReadOnlyList<string> Inventory  => _inventory;

        // ── Private ────────────────────────────────────────────────────────────
        private CharacterController _cc;
        private Vector3 _velocity;
        private float   _cameraPitch;
        private float   _noiseTimer;

        // Speed modifier system
        private readonly Dictionary<string, float> _speedModifiers = new();

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            _cc      = GetComponent<CharacterController>();
            Health   = _maxHealth;
            Stamina  = _maxStamina;
            Battery  = _maxBattery;
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            SetFlashlight(true);
        }

        private void Update()
        {
            if (!IsAlive) return;

            HandleLook();
            HandleMovement();
            HandleStamina();
            HandleBattery();
            HandleInteract();
            HandleFlashlight();
            EmitNoise();
        }

        // ── Look ───────────────────────────────────────────────────────────────
        private void HandleLook()
        {
            float mx = Input.GetAxis("Mouse X") * _mouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * _mouseSensitivity;

            transform.Rotate(0, mx, 0);
            _cameraPitch = Mathf.Clamp(_cameraPitch - my, -85f, 85f);
            _cameraRoot.localEulerAngles = new Vector3(_cameraPitch, 0, 0);
        }

        // ── Movement ───────────────────────────────────────────────────────────
        private void HandleMovement()
        {
            // Crouch toggle
            if (Input.GetKeyDown(KeyCode.LeftControl))
                SetCrouch(!IsCrouching);

            bool wantSprint = Input.GetKey(KeyCode.LeftShift) && Stamina > _sprintMinStamina
                              && !IsCrouching;

            IsSprinting = wantSprint;

            float targetSpeed = IsCrouching  ? _crouchSpeed
                              : IsSprinting  ? _sprintSpeed
                              : _walkSpeed;

            // Apply external modifiers (conditions, injury, etc.)
            float modifier = 1f;
            foreach (var m in _speedModifiers.Values) modifier *= m;
            targetSpeed *= modifier;

            Speed = targetSpeed;

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 dir = transform.right * h + transform.forward * v;

            if (_cc.isGrounded && _velocity.y < 0) _velocity.y = -2f;

            _velocity.y += _gravity * Time.deltaTime;
            _cc.Move((dir * targetSpeed + Vector3.up * _velocity.y) * Time.deltaTime);
        }

        // ── Stamina ────────────────────────────────────────────────────────────
        private void HandleStamina()
        {
            if (IsSprinting)
                DrainStamina(_sprintStaminaCost * Time.deltaTime);
            else
                Stamina = Mathf.Min(_maxStamina, Stamina + _staminaRegenRate * Time.deltaTime);
        }

        public void DrainStamina(float amount)
        {
            Stamina = Mathf.Max(0, Stamina - amount);
            if (Stamina <= 0 && IsSprinting) IsSprinting = false;
        }

        // ── Battery ────────────────────────────────────────────────────────────
        private void HandleBattery()
        {
            if (!FlashlightOn) return;
            Battery = Mathf.Max(0, Battery - _batteryDrainRate * Time.deltaTime);
            if (Battery <= 0) SetFlashlight(false);
        }

        private void HandleFlashlight()
        {
            if (Input.GetKeyDown(KeyCode.F))
                SetFlashlight(!FlashlightOn);
        }

        public void SetFlashlight(bool on)
        {
            FlashlightOn = on;
            if (_flashlight) _flashlight.enabled = on && Battery > 0;
        }

        public void AddBattery(float amount) => Battery = Mathf.Min(_maxBattery, Battery + amount);

        // ── Crouch ─────────────────────────────────────────────────────────────
        private void SetCrouch(bool crouch)
        {
            IsCrouching = crouch;
            if (_cameraRoot)
            {
                var lp = _cameraRoot.localPosition;
                lp.y = crouch ? _crouchCameraY : _standCameraY;
                _cameraRoot.localPosition = lp;
            }
        }

        // ── Interact ───────────────────────────────────────────────────────────
        private void HandleInteract()
        {
            if (!Input.GetKeyDown(KeyCode.E)) return;
            if (Physics.Raycast(_cameraRoot.position, _cameraRoot.forward,
                out RaycastHit hit, 2.5f))
            {
                var interactive = hit.collider.GetComponentInParent<IFacilityInteractive>();
                interactive?.Interact(this);
            }
        }

        // ── Noise emission ─────────────────────────────────────────────────────
        private void EmitNoise()
        {
            if (_cc.velocity.magnitude < 0.1f) return;
            _noiseTimer -= Time.deltaTime;
            if (_noiseTimer > 0) return;
            _noiseTimer = _noiseEmitInterval;

            // Apply loud-footstep condition
            bool louder = ConditionManager.Instance != null
                       && ConditionManager.Instance.Has(ConditionType.LoudFootsteps);

            float radius = IsCrouching  ? _crouchNoiseRadius
                         : IsSprinting  ? _sprintNoiseRadius
                         : _walkNoiseRadius;

            if (louder) radius *= 1.6f;

            NoiseLevel level = IsSprinting ? NoiseLevel.Loud
                             : IsCrouching ? NoiseLevel.Quiet
                             : NoiseLevel.Moderate;

            EventBus.Publish(new EvPlayerNoise(transform.position, level));
        }

        // ── Health ─────────────────────────────────────────────────────────────
        public void TakeDamage(float amount, string sourceId)
        {
            if (!IsAlive) return;
            Health = Mathf.Max(0, Health - amount);
            EventBus.Publish(new EvPlayerDamaged(amount, sourceId));
            if (Health <= 0) Die();
        }

        public void Heal(float amount) => Health = Mathf.Min(_maxHealth, Health + amount);

        private void Die()
        {
            IsAlive = false;
            EventBus.Publish(new EvPlayerDied());
        }

        public void OnDeath() { /* handled above */ }

        // ── Inventory ──────────────────────────────────────────────────────────
        public bool HasItem(string id)  => _inventory.Contains(id);
        public void AddItem(string id)  { if (!_inventory.Contains(id)) _inventory.Add(id); }
        public void UseItem(string id)  => _inventory.Remove(id);

        // ── Speed modifier API ─────────────────────────────────────────────────
        public void ApplySpeedModifier(float mult, string id) => _speedModifiers[id] = mult;
        public void RemoveSpeedModifier(string id)            => _speedModifiers.Remove(id);

        // ── Noise radius query ─────────────────────────────────────────────────
        public float CurrentNoiseRadius =>
            IsCrouching ? _crouchNoiseRadius :
            IsSprinting ? _sprintNoiseRadius : _walkNoiseRadius;

        // ── Normalised vitals ──────────────────────────────────────────────────
        public float HealthNormalised  => Health  / _maxHealth;
        public float StaminaNormalised => Stamina / _maxStamina;
        public float BatteryNormalised => Battery / _maxBattery;
    }
}using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Actors
{
    using Core;

    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour, IEntity, IMovementProvider
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Movement")]
        [SerializeField] private float _walkSpeed   = 3.5f;
        [SerializeField] private float _crouchSpeed = 1.8f;
        [SerializeField] private float _sprintSpeed = 6.0f;
        [SerializeField] private float _gravity     = -9.81f;

        [Header("Camera")]
        [SerializeField] private Transform _cameraRoot;
        [SerializeField] private float     _mouseSensitivity = 2f;
        [SerializeField] private float     _crouchCameraY    = 0.9f;
        [SerializeField] private float     _standCameraY     = 1.7f;

        [Header("Stamina")]
        [SerializeField] private float _maxStamina          = 100f;
        [SerializeField] private float _staminaRegenRate    = 12f;
        [SerializeField] private float _sprintStaminaCost   = 18f;
        [SerializeField] private float _sprintMinStamina    = 15f;

        [Header("Health")]
        [SerializeField] private float _maxHealth = 100f;

        [Header("Flashlight")]
        [SerializeField] private Light  _flashlight;
        [SerializeField] private float  _maxBattery      = 300f;  // seconds
        [SerializeField] private float  _batteryDrainRate = 1f;

        [Header("Noise")]
        [SerializeField] private float _walkNoiseRadius   = 4f;
        [SerializeField] private float _sprintNoiseRadius = 9f;
        [SerializeField] private float _crouchNoiseRadius = 1.5f;
        [SerializeField] private float _noiseEmitInterval = 0.5f;

        // ── IEntity ────────────────────────────────────────────────────────────
        public string    EntityId  => "Player";
        public Transform Transform => transform;
        public bool      IsAlive   { get; private set; } = true;

        // ── IMovementProvider ─────────────────────────────────────────────────
        public float Speed       { get; private set; }
        public bool  IsGrounded  => _cc.isGrounded;

        // ── Public state ───────────────────────────────────────────────────────
        public float  Health          { get; private set; }
        public float  Stamina         { get; private set; }
        public float  Battery         { get; private set; }
        public bool   IsCrouching     { get; private set; }
        public bool   IsSprinting     { get; private set; }
        public bool   FlashlightOn    { get; private set; }
        public string CurrentRoomId   { get; set; }

        // ── Inventory ──────────────────────────────────────────────────────────
        private readonly List<string> _inventory = new();
        public  IReadOnlyList<string> Inventory  => _inventory;

        // ── Private ────────────────────────────────────────────────────────────
        private CharacterController _cc;
        private Vector3 _velocity;
        private float   _cameraPitch;
        private float   _noiseTimer;

        // Speed modifier system
        private readonly Dictionary<string, float> _speedModifiers = new();

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            _cc      = GetComponent<CharacterController>();
            Health   = _maxHealth;
            Stamina  = _maxStamina;
            Battery  = _maxBattery;
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            SetFlashlight(true);
        }

        private void Update()
        {
            if (!IsAlive) return;

            HandleLook();
            HandleMovement();
            HandleStamina();
            HandleBattery();
            HandleInteract();
            HandleFlashlight();
            EmitNoise();
        }

        // ── Look ───────────────────────────────────────────────────────────────
        private void HandleLook()
        {
            float mx = Input.GetAxis("Mouse X") * _mouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * _mouseSensitivity;

            transform.Rotate(0, mx, 0);
            _cameraPitch = Mathf.Clamp(_cameraPitch - my, -85f, 85f);
            _cameraRoot.localEulerAngles = new Vector3(_cameraPitch, 0, 0);
        }

        // ── Movement ───────────────────────────────────────────────────────────
        private void HandleMovement()
        {
            // Crouch toggle
            if (Input.GetKeyDown(KeyCode.LeftControl))
                SetCrouch(!IsCrouching);

            bool wantSprint = Input.GetKey(KeyCode.LeftShift) && Stamina > _sprintMinStamina
                              && !IsCrouching;

            IsSprinting = wantSprint;

            float targetSpeed = IsCrouching  ? _crouchSpeed
                              : IsSprinting  ? _sprintSpeed
                              : _walkSpeed;

            // Apply external modifiers (conditions, injury, etc.)
            float modifier = 1f;
            foreach (var m in _speedModifiers.Values) modifier *= m;
            targetSpeed *= modifier;

            Speed = targetSpeed;

            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 dir = transform.right * h + transform.forward * v;

            if (_cc.isGrounded && _velocity.y < 0) _velocity.y = -2f;

            _velocity.y += _gravity * Time.deltaTime;
            _cc.Move((dir * targetSpeed + Vector3.up * _velocity.y) * Time.deltaTime);
        }

        // ── Stamina ────────────────────────────────────────────────────────────
        private void HandleStamina()
        {
            if (IsSprinting)
                DrainStamina(_sprintStaminaCost * Time.deltaTime);
            else
                Stamina = Mathf.Min(_maxStamina, Stamina + _staminaRegenRate * Time.deltaTime);
        }

        public void DrainStamina(float amount)
        {
            Stamina = Mathf.Max(0, Stamina - amount);
            if (Stamina <= 0 && IsSprinting) IsSprinting = false;
        }

        // ── Battery ────────────────────────────────────────────────────────────
        private void HandleBattery()
        {
            if (!FlashlightOn) return;
            Battery = Mathf.Max(0, Battery - _batteryDrainRate * Time.deltaTime);
            if (Battery <= 0) SetFlashlight(false);
        }

        private void HandleFlashlight()
        {
            if (Input.GetKeyDown(KeyCode.F))
                SetFlashlight(!FlashlightOn);
        }

        public void SetFlashlight(bool on)
        {
            FlashlightOn = on;
            if (_flashlight) _flashlight.enabled = on && Battery > 0;
        }

        public void AddBattery(float amount) => Battery = Mathf.Min(_maxBattery, Battery + amount);

        // ── Crouch ─────────────────────────────────────────────────────────────
        private void SetCrouch(bool crouch)
        {
            IsCrouching = crouch;
            if (_cameraRoot)
            {
                var lp = _cameraRoot.localPosition;
                lp.y = crouch ? _crouchCameraY : _standCameraY;
                _cameraRoot.localPosition = lp;
            }
        }

        // ── Interact ───────────────────────────────────────────────────────────
        private void HandleInteract()
        {
            if (!Input.GetKeyDown(KeyCode.E)) return;
            if (Physics.Raycast(_cameraRoot.position, _cameraRoot.forward,
                out RaycastHit hit, 2.5f))
            {
                var interactive = hit.collider.GetComponentInParent<IFacilityInteractive>();
                interactive?.Interact(this);
            }
        }

        // ── Noise emission ─────────────────────────────────────────────────────
        private void EmitNoise()
        {
            if (_cc.velocity.magnitude < 0.1f) return;
            _noiseTimer -= Time.deltaTime;
            if (_noiseTimer > 0) return;
            _noiseTimer = _noiseEmitInterval;

            // Apply loud-footstep condition
            bool louder = ConditionManager.Instance != null
                       && ConditionManager.Instance.Has(ConditionType.LoudFootsteps);

            float radius = IsCrouching  ? _crouchNoiseRadius
                         : IsSprinting  ? _sprintNoiseRadius
                         : _walkNoiseRadius;

            if (louder) radius *= 1.6f;

            NoiseLevel level = IsSprinting ? NoiseLevel.Loud
                             : IsCrouching ? NoiseLevel.Quiet
                             : NoiseLevel.Moderate;

            EventBus.Publish(new EvPlayerNoise(transform.position, level));
        }

        // ── Health ─────────────────────────────────────────────────────────────
        public void TakeDamage(float amount, string sourceId)
        {
            if (!IsAlive) return;
            Health = Mathf.Max(0, Health - amount);
            EventBus.Publish(new EvPlayerDamaged(amount, sourceId));
            if (Health <= 0) Die();
        }

        public void Heal(float amount) => Health = Mathf.Min(_maxHealth, Health + amount);

        private void Die()
        {
            IsAlive = false;
            EventBus.Publish(new EvPlayerDied());
        }

        public void OnDeath() { /* handled above */ }

        // ── Inventory ──────────────────────────────────────────────────────────
        public bool HasItem(string id)  => _inventory.Contains(id);
        public void AddItem(string id)  { if (!_inventory.Contains(id)) _inventory.Add(id); }
        public void UseItem(string id)  => _inventory.Remove(id);

        // ── Speed modifier API ─────────────────────────────────────────────────
        public void ApplySpeedModifier(float mult, string id) => _speedModifiers[id] = mult;
        public void RemoveSpeedModifier(string id)            => _speedModifiers.Remove(id);

        // ── Noise radius query ─────────────────────────────────────────────────
        public float CurrentNoiseRadius =>
            IsCrouching ? _crouchNoiseRadius :
            IsSprinting ? _sprintNoiseRadius : _walkNoiseRadius;

        // ── Normalised vitals ──────────────────────────────────────────────────
        public float HealthNormalised  => Health  / _maxHealth;
        public float StaminaNormalised => Stamina / _maxStamina;
        public float BatteryNormalised => Battery / _maxBattery;
    }
}
