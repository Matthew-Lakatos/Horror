using System.Collections;
using UnityEngine;
using Eidolon.Core;
using Eidolon.Audio;

namespace Eidolon.World
{
    // ─────────────────────────────────────────────────────────────────────────
    // SKYBRIDGE CONTROLLER
    // Elevated enclosed connector. FacilityBrain can lock doors mid-crossing.
    // Emotional purpose: nowhere to hide, strong sightlines, Big E silhouette.
    // Late game: geometry mismatch, wrong destination, lights failing mid-cross.
    // ─────────────────────────────────────────────────────────────────────────

    public class SkybridgeController : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string    _bridgeId;
        [SerializeField] private string    _buildingAEntry;
        [SerializeField] private string    _buildingBEntry;

        [Header("Doors")]
        [SerializeField] private DoorController _doorA;
        [SerializeField] private DoorController _doorB;

        [Header("Lighting")]
        [SerializeField] private Light[]   _bridgeLights;
        [SerializeField] private float     _lightFailChance = 0.3f;

        [Header("Late Game Misdirection")]
        [SerializeField] private Transform _normalExitA;
        [SerializeField] private Transform _normalExitB;
        [SerializeField] private Transform _wrongExitA; // leads to unexpected structure
        [SerializeField] private Transform _wrongExitB;
        [SerializeField] private bool      _wrongExitActive = false;

        [Header("Big E Silhouette Points")]
        [SerializeField] private Transform[] _silhouettePositions;

        [Header("Audio")]
        [SerializeField] private AudioClip _bridgeCreakClip;
        [SerializeField] private AudioClip _lightFlickerClip;
        [SerializeField] private AudioSource _audioSource;

        private bool _isLocked;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void OnEnable()  => EventBus.Subscribe<FacilityBrainActionEvent>(OnFacilityAction);
        private void OnDisable() => EventBus.Unsubscribe<FacilityBrainActionEvent>(OnFacilityAction);

        // ─── FacilityBrain Actions ───────────────────────────────────────────

        private void OnFacilityAction(FacilityBrainActionEvent evt)
        {
            if (evt.TargetId != _bridgeId) return;

            switch (evt.ActionType)
            {
                case FacilityActionType.CloseSkybridge:
                    StartCoroutine(LockBridgeMidCrossing());
                    break;
                case FacilityActionType.DimLights:
                    StartCoroutine(FailLights());
                    break;
            }
        }

        // ─── Lock Mid-Crossing ───────────────────────────────────────────────

        private IEnumerator LockBridgeMidCrossing()
        {
            // Wait for player to be on bridge
            while (!IsPlayerOnBridge()) yield return new WaitForSeconds(0.5f);

            // Lock far door — player must find another route
            _doorB?.SetLocked(true);
            _isLocked = true;
            Debug.Log($"[Skybridge] {_bridgeId} locked mid-crossing");

            // Unlock after a tense period (player must backtrack or wait)
            yield return new WaitForSeconds(20f);
            _doorB?.SetLocked(false);
            _isLocked = false;
        }

        // ─── Light Failure ───────────────────────────────────────────────────

        private IEnumerator FailLights()
        {
            PlayClip(_lightFlickerClip);

            foreach (var light in _bridgeLights)
            {
                if (light == null) continue;
                if (Random.value < _lightFailChance)
                {
                    yield return new WaitForSeconds(Random.Range(0.05f, 0.2f));
                    light.enabled = false;
                }
            }
        }

        // ─── Wrong Destination (Late Game) ───────────────────────────────────

        /// <summary>Activates wrong-exit misdirection for geometry horror.</summary>
        public void ActivateWrongExit()
        {
            if (GameDirector.Instance?.CurrentPhase != GamePhase.LateGame) return;
            _wrongExitActive = true;

            // Swap exit positions visually — the bridge appears to lead somewhere else
            if (_wrongExitA && _normalExitA) _normalExitA.position = _wrongExitA.position;
            if (_wrongExitB && _normalExitB) _normalExitB.position = _wrongExitB.position;

            Debug.Log($"[Skybridge] {_bridgeId} wrong-exit geometry active");
        }

        // ─── Big E Silhouette ────────────────────────────────────────────────

        /// <summary>Called by BigEActor when it wants to appear as a silhouette at the far end.</summary>
        public Transform GetSilhouettePoint()
        {
            if (_silhouettePositions == null || _silhouettePositions.Length == 0) return null;
            return _silhouettePositions[Random.Range(0, _silhouettePositions.Length)];
        }

        // ─── Footstep Detection ──────────────────────────────────────────────

        /// <summary>Bridge plays creak sounds as player crosses — unseen footstep atmosphere.</summary>
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                AudioManager.Instance?.StartAmbience(BuildingAmbienceType.Skybridge);
                PlayClip(_bridgeCreakClip);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
                AudioManager.Instance?.StartAmbience(BuildingAmbienceType.Interior);
        }

        private bool IsPlayerOnBridge()
        {
            var player = Actors.PlayerController.Instance;
            if (player == null) return false;
            return GetComponent<Collider>()?.bounds.Contains(player.transform.position) ?? false;
        }

        private void PlayClip(AudioClip clip)
        {
            if (_audioSource && clip) _audioSource.PlayOneShot(clip);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INDUSTRIAL YARD ZONE
    // Outdoor traversal. Contrast from interiors. Exposed traversal fear.
    // Player always sees fences/walls/blocked exits. Never feels free.
    // ─────────────────────────────────────────────────────────────────────────

    public class YardZone : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string _yardId;

        [Header("Floodlights")]
        [SerializeField] private Light[]  _floodlights;
        [SerializeField] private float    _floodlightRestoreDuration = 30f;

        [Header("Steam Vents")]
        [SerializeField] private ParticleSystem[] _steamVents;

        [Header("Security Gates")]
        [SerializeField] private DoorController[] _securityGates;

        [Header("Fog")]
        [SerializeField] private ParticleSystem _fogParticles;
        [SerializeField] private float          _fogDuration = 25f;

        [Header("Siren")]
        [SerializeField] private AudioSource _sirenSource;
        [SerializeField] private AudioClip   _sirenClip;

        [Header("Transformer Hum")]
        [SerializeField] private AudioSource _transformerHum;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (_transformerHum != null) _transformerHum.Play();
        }

        private void OnEnable()  => EventBus.Subscribe<FacilityBrainActionEvent>(OnFacilityAction);
        private void OnDisable() => EventBus.Unsubscribe<FacilityBrainActionEvent>(OnFacilityAction);

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
                AudioManager.Instance?.StartAmbience(BuildingAmbienceType.Yard);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
                AudioManager.Instance?.StartAmbience(BuildingAmbienceType.Interior);
        }

        // ─── FacilityBrain Actions ───────────────────────────────────────────

        private void OnFacilityAction(FacilityBrainActionEvent evt)
        {
            if (evt.TargetId != _yardId && evt.TargetId != "yard_global") return;

            switch (evt.ActionType)
            {
                case FacilityActionType.DisableFloodlights:
                    StartCoroutine(DisableFloodlights());
                    break;
                case FacilityActionType.FloodSteam:
                    StartCoroutine(FloodSteam());
                    break;
                case FacilityActionType.ActivateSiren:
                    ActivateSiren(evt.TargetId == "siren_on");
                    break;
            }
        }

        // ─── Floodlights ────────────────────────────────────────────────────

        private IEnumerator DisableFloodlights()
        {
            foreach (var light in _floodlights)
                if (light != null) light.enabled = false;

            Debug.Log($"[YardZone] {_yardId} floodlights disabled");
            yield return new WaitForSeconds(_floodlightRestoreDuration);

            foreach (var light in _floodlights)
                if (light != null) light.enabled = true;
        }

        // ─── Steam Flood ─────────────────────────────────────────────────────

        private IEnumerator FloodSteam()
        {
            foreach (var vent in _steamVents)
                if (vent != null) vent.Play();

            WorldStateManager.Instance?.SetRoomFogged(_yardId, true);
            yield return new WaitForSeconds(_fogDuration);

            foreach (var vent in _steamVents)
                if (vent != null) vent.Stop();

            WorldStateManager.Instance?.SetRoomFogged(_yardId, false);
        }

        // ─── Siren ───────────────────────────────────────────────────────────

        private void ActivateSiren(bool on)
        {
            if (_sirenSource == null) return;
            if (on)
            {
                _sirenSource.clip = _sirenClip;
                _sirenSource.loop = true;
                _sirenSource.Play();
            }
            else _sirenSource.Stop();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MAINTENANCE TUNNEL ZONE
    // Underground service routes. Claustrophobic acoustics.
    // Stealth routes, shortcut systems, close-range threat spaces.
    // ─────────────────────────────────────────────────────────────────────────

    public class MaintenanceTunnelZone : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string _tunnelId;
        [SerializeField] private string _connectsFrom;
        [SerializeField] private string _connectsTo;

        [Header("Lighting")]
        [SerializeField] private Light[] _emergencyLights;
        [SerializeField] private Color   _emergencyLightColor = new Color(1f, 0.4f, 0f);

        [Header("Hazards")]
        [SerializeField] private RustyNailHazard[] _nailHazards;
        [SerializeField] private float             _wetConcreteSlowFactor = 0.85f;

        [Header("Acoustics")]
        [SerializeField] private AudioSource _tunnelAmbience;
        [SerializeField] private float       _reverbStrength = 0.8f;

        [Header("Valves / Pipes Visual")]
        [SerializeField] private GameObject[] _pipingProps;
        [SerializeField] private GameObject[] _cableTrayProps;

        [Header("FacilityBrain Blocking")]
        [SerializeField] private DoorController _tunnelBlockDoor;

        private bool _playerInTunnel;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            SetEmergencyLighting();
        }

        private void OnEnable()  => EventBus.Subscribe<FacilityBrainActionEvent>(OnFacilityAction);
        private void OnDisable() => EventBus.Unsubscribe<FacilityBrainActionEvent>(OnFacilityAction);

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInTunnel = true;
            AudioManager.Instance?.StartAmbience(BuildingAmbienceType.Tunnel);

            // Apply wet concrete slow via condition
            EventBus.Publish(new ConditionAppliedEvent
            {
                ConditionType = ConditionType.StaminaDrain,
                Duration      = 0f,   // persistent while inside
                Magnitude     = 1f - _wetConcreteSlowFactor
            });
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInTunnel = false;
            AudioManager.Instance?.StartAmbience(BuildingAmbienceType.Interior);
        }

        // ─── Emergency Lighting ──────────────────────────────────────────────

        private void SetEmergencyLighting()
        {
            foreach (var light in _emergencyLights)
                if (light != null) light.color = _emergencyLightColor;
        }

        // ─── FacilityBrain ───────────────────────────────────────────────────

        private void OnFacilityAction(FacilityBrainActionEvent evt)
        {
            if (evt.TargetId != _tunnelId) return;

            if (evt.ActionType == FacilityActionType.BlockTunnel)
                StartCoroutine(BlockTunnel());
        }

        private IEnumerator BlockTunnel()
        {
            if (_tunnelBlockDoor == null) yield break;

            // Only block if it won't disconnect the map
            _tunnelBlockDoor.SetLocked(true);
            Debug.Log($"[MaintenanceTunnel] {_tunnelId} blocked by FacilityBrain");

            yield return new WaitForSeconds(30f);
            _tunnelBlockDoor.SetLocked(false);
        }
    }
}
