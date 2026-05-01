using System.Collections;
using UnityEngine;
using Eidolon.Core;

namespace Eidolon.World
{
    // ─────────────────────────────────────────────────────────────────────────
    // DOOR CONTROLLER
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manages a physical door: locked/unlocked state, FacilityBrain override,
    /// and wrench-forced entry. Publishes DoorStateChangedEvent on any change.
    /// </summary>
    public class DoorController : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string _doorId;
        [SerializeField] private string _roomA;
        [SerializeField] private string _roomB;
        [SerializeField] private bool   _initiallyLocked = false;

        [Header("Audio")]
        [SerializeField] private AudioClip _openClip;
        [SerializeField] private AudioClip _lockClip;
        [SerializeField] private AudioClip _wrenchClip;
        [SerializeField] private float _wrenchNoiseRadius = 12f;

        [Header("Animation")]
        [SerializeField] private Animator _animator;
        private static readonly int _animOpen   = Animator.StringToHash("Open");
        private static readonly int _animLocked = Animator.StringToHash("Locked");

        public string DoorId => _doorId;
        public string RoomA  => _roomA;
        public string RoomB  => _roomB;
        public bool   IsLocked { get; private set; }

        private AudioSource _audioSource;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            IsLocked = _initiallyLocked;
            RefreshAnimation();
        }

        // ─── State Changes ───────────────────────────────────────────────────

        public void SetLocked(bool locked)
        {
            if (IsLocked == locked) return;
            IsLocked = locked;
            RefreshAnimation();
            PlayClip(locked ? _lockClip : _openClip);
            EventBus.Publish(new DoorStateChangedEvent { DoorId = _doorId, IsLocked = locked });
        }

        /// <summary>Player-triggered wrench forced entry. Generates loud noise.</summary>
        public void ForceOpen()
        {
            if (!IsLocked) return;
            PlayClip(_wrenchClip);
            EventBus.Publish(new PlayerNoiseEvent
            {
                Origin = transform.position,
                Radius = _wrenchNoiseRadius,
                Type   = NoiseType.Wrench
            });
            SetLocked(false);
        }

        private void RefreshAnimation()
        {
            if (_animator == null) return;
            _animator.SetBool(_animLocked, IsLocked);
            if (!IsLocked) _animator.SetTrigger(_animOpen);
        }

        private void PlayClip(AudioClip clip)
        {
            if (_audioSource != null && clip != null)
                _audioSource.PlayOneShot(clip);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LIFT CONTROLLER
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Controls an elevator/freight lift. FacilityBrain can apply slow debuffs.
    /// </summary>
    public class LiftController : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private string _liftId;
        [SerializeField] private float  _normalSpeed = 2f;
        [SerializeField] private float  _slowMultiplier = 0.3f;

        [Header("Floors")]
        [SerializeField] private Transform[] _floorPositions;

        private bool  _isSlowed;
        private float _slowExpiry;
        private int   _targetFloor;
        private bool  _isMoving;

        public string LiftId => _liftId;
        public float  CurrentSpeed => _isSlowed ? _normalSpeed * _slowMultiplier : _normalSpeed;

        private void Update()
        {
            if (_isSlowed && Time.time >= _slowExpiry)
                _isSlowed = false;

            if (_isMoving)
                MoveTick();
        }

        public void ApplySlow(float duration)
        {
            _isSlowed  = true;
            _slowExpiry = Time.time + duration;
        }

        public void CallToFloor(int floorIndex)
        {
            if (floorIndex < 0 || floorIndex >= _floorPositions.Length) return;
            _targetFloor = floorIndex;
            _isMoving    = true;
        }

        private void MoveTick()
        {
            if (_floorPositions == null || _targetFloor >= _floorPositions.Length) return;
            var target = _floorPositions[_targetFloor].position;
            transform.position = Vector3.MoveTowards(transform.position, target, CurrentSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, target) < 0.02f)
            {
                transform.position = target;
                _isMoving = false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MED STATION
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Healing station. FacilityBrain and Nurse can disable it temporarily.
    /// Player interacts to consume healing.
    /// </summary>
    public class MedStation : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string _stationId;

        [Header("Healing")]
        [SerializeField] private float _healAmount     = 40f;
        [SerializeField] private int   _charges        = 3;
        [SerializeField] private float _useTime        = 2.5f;

        [Header("Audio")]
        [SerializeField] private AudioClip _useClip;
        [SerializeField] private AudioClip _emptyClip;
        [SerializeField] private AudioClip _disabledClip;

        public string StationId  => _stationId;
        public bool   IsDisabled { get; private set; }
        public bool   IsEmpty    => _charges <= 0;

        private Coroutine _disableCoroutine;
        private AudioSource _audioSource;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        }

        public bool TryUse(out float healGiven)
        {
            healGiven = 0f;
            if (IsDisabled || IsEmpty) { PlayClip(IsDisabled ? _disabledClip : _emptyClip); return false; }
            _charges--;
            healGiven = _healAmount;
            PlayClip(_useClip);
            return true;
        }

        public void Disable(float duration)
        {
            if (_disableCoroutine != null) StopCoroutine(_disableCoroutine);
            _disableCoroutine = StartCoroutine(DisableRoutine(duration));
        }

        private IEnumerator DisableRoutine(float duration)
        {
            IsDisabled = true;
            Debug.Log($"[MedStation] {_stationId} disabled for {duration}s");
            yield return new WaitForSeconds(duration);
            IsDisabled = false;
        }

        public float UseTime => _useTime;

        private void PlayClip(AudioClip clip)
        {
            if (_audioSource != null && clip != null)
                _audioSource.PlayOneShot(clip);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STRUCTURAL FAILURE FLOORING
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Visually telegraphed weak floor sections. Buckles under player weight,
    /// generates noise, and causes stumble. No hidden cheap deaths.
    /// </summary>
    public class StructuralFloor : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _damageAmount   = 10f;
        [SerializeField] private float _noiseBurstRadius = 8f;
        [SerializeField] private bool  _alreadyTriggered = false;

        [Header("References")]
        [SerializeField] private GameObject _intactMesh;
        [SerializeField] private GameObject _brokenMesh;
        [SerializeField] private AudioClip  _crackClip;
        [SerializeField] private AudioClip  _collapseClip;

        private bool _isCracking;
        private AudioSource _audioSource;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            SetVisual(broken: _alreadyTriggered);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_alreadyTriggered) return;
            if (!other.CompareTag("Player")) return;

            if (!_isCracking)
                StartCoroutine(CrackAndCollapse(other));
        }

        private IEnumerator CrackAndCollapse(Collider playerCollider)
        {
            _isCracking = true;
            PlayClip(_crackClip);
            yield return new WaitForSeconds(0.8f); // telegraphed delay

            PlayClip(_collapseClip);
            _alreadyTriggered = true;
            SetVisual(broken: true);

            // Noise burst
            EventBus.Publish(new PlayerNoiseEvent
            {
                Origin = transform.position,
                Radius = _noiseBurstRadius,
                Type   = NoiseType.Collision
            });

            // Apply damage + stumble
            EventBus.Publish(new PlayerDamagedEvent
            {
                Amount   = _damageAmount,
                Source   = DamageSource.StructuralFail,
                HitPoint = transform.position
            });
        }

        private void SetVisual(bool broken)
        {
            if (_intactMesh) _intactMesh.SetActive(!broken);
            if (_brokenMesh) _brokenMesh.SetActive(broken);
        }

        private void PlayClip(AudioClip clip)
        {
            if (_audioSource != null && clip != null)
                _audioSource.PlayOneShot(clip);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RUSTY NAIL HAZARD
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rare environmental hazard in neglected sectors. Causes damage, bleed,
    /// and optional limp. Always visible — never a hidden attrition trap.
    /// </summary>
    public class RustyNailHazard : MonoBehaviour
    {
        [SerializeField] private float _damageAmount  = 8f;
        [SerializeField] private float _bleedChance   = 0.5f;
        [SerializeField] private float _limpChance    = 0.3f;
        [SerializeField] private float _triggerCooldown = 5f;

        private float _lastTrigger = -99f;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            if (Time.time - _lastTrigger < _triggerCooldown) return;
            _lastTrigger = Time.time;

            EventBus.Publish(new PlayerDamagedEvent
            {
                Amount   = _damageAmount,
                Source   = DamageSource.RustyNail,
                HitPoint = transform.position
            });

            if (Random.value < _bleedChance)
                EventBus.Publish(new ConditionAppliedEvent
                {
                    ConditionType = ConditionType.Bleed,
                    Duration      = 15f,
                    Magnitude     = 0.5f
                });

            if (Random.value < _limpChance)
                EventBus.Publish(new ConditionAppliedEvent
                {
                    ConditionType = ConditionType.Limp,
                    Duration      = 20f,
                    Magnitude     = 0.4f
                });
        }
    }
}
