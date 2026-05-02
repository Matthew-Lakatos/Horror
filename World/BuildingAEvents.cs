using System.Collections;
using UnityEngine;
using Eidolon.Core;
using Eidolon.AI;
using Eidolon.Actors;

namespace Eidolon.World
{
    /// <summary>
    /// Building A Scripted Moment Director.
    /// 
    /// Manages all 7 planned memorable moments using existing systems.
    /// Each moment fires once, tracked via flags.
    /// Moments are triggered by objective phase, player location, and room visits.
    /// 
    /// Moments:
    ///   1. Big E Through Glass        — player enters Core Hub, looks up
    ///   2. Safe Room Knock            — while saving, knocks outside door
    ///   3. Wrong Room Label           — Med Room briefly labelled STORAGE
    ///   4. CCTV Betrayal              — feed shows empty, reality differs
    ///   5. Power Return Consequence   — lights on, new route + new threat path
    ///   6. Favourite Shortcut Closed  — FacilityBrain welds most-used route
    ///   7. Floor Lurch                — telegraphed structural buckle in East Wing
    /// </summary>
    public class BuildingAEventDirector : MonoBehaviour
    {
        public static BuildingAEventDirector Instance { get; private set; }

        // ─── Room References ─────────────────────────────────────────────────

        [Header("Room Trigger Zones")]
        [SerializeField] private Collider _coreHubZone;
        [SerializeField] private Collider _safeRoomZone;
        [SerializeField] private Collider _medRoomZone;
        [SerializeField] private Collider _eastWingZone;

        // ─── Big E Through Glass ─────────────────────────────────────────────

        [Header("1. Big E Through Glass")]
        [SerializeField] private Transform   _glassWalkwayBigEPosition;
        [SerializeField] private BigEActor   _bigE;
        [SerializeField] private float       _bigEViewDuration = 4f;

        // ─── Safe Room Knock ─────────────────────────────────────────────────

        [Header("2. Safe Room Knock")]
        [SerializeField] private AudioSource _safeRoomDoor;
        [SerializeField] private AudioClip   _knockClip;
        [SerializeField] private int         _knockCount      = 3;
        [SerializeField] private float       _knockInterval   = 1.2f;
        [SerializeField] private float       _knockDelay      = 8f; // seconds after entering

        // ─── Wrong Room Label ────────────────────────────────────────────────

        [Header("3. Wrong Room Label")]
        [SerializeField] private string      _medRoomId       = "med_room";
        [SerializeField] private string      _falseLabel      = "STORAGE — 4B";
        [SerializeField] private float       _labelCorruptDuration = 12f;

        // ─── CCTV Betrayal ───────────────────────────────────────────────────

        [Header("4. CCTV Betrayal")]
        [SerializeField] private CCTVMonitor _targetMonitor;
        [SerializeField] private string      _betrayalRoomId  = "utility_corridor_ring";

        // ─── Power Return ────────────────────────────────────────────────────

        [Header("5. Power Return Consequence")]
        [SerializeField] private Light[]     _powerReturnLights;
        [SerializeField] private DoorController _newThreatDoor;  // door that opens, exposing threat
        [SerializeField] private AudioClip   _electricalHumClip;
        [SerializeField] private AudioSource _powerAudioSource;

        // ─── Shortcut Closure ────────────────────────────────────────────────

        [Header("6. Favourite Shortcut Closed")]
        [SerializeField] private DoorController _shortcutDoor;
        [SerializeField] private GameObject     _weldedVisual;    // welding sparks / welded plate mesh
        [SerializeField] private AudioClip      _weldingClip;
        [SerializeField] private int            _shortcutVisitThreshold = 5;
        [SerializeField] private string         _shortcutRoomId  = "utility_corridor_ring";

        // ─── Floor Lurch ─────────────────────────────────────────────────────

        [Header("7. Floor Lurch (East Wing)")]
        [SerializeField] private StructuralFloor _eastWingStructuralFloor;

        // ─── Flags ───────────────────────────────────────────────────────────

        private bool _bigEGlassDone;
        private bool _safeRoomKnockDone;
        private bool _wrongLabelDone;
        private bool _cctvBetrayalDone;
        private bool _powerReturnDone;
        private bool _shortcutClosedDone;
        private bool _floorLurchArmed = true;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<RoomStateChangedEvent>(OnRoomVisited);
            EventBus.Subscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<RoomStateChangedEvent>(OnRoomVisited);
            EventBus.Unsubscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
        }

        private void Update()
        {
            CheckShortcutClosure();
        }

        // ─── Trigger Entry Callbacks ─────────────────────────────────────────
        // Called from per-zone trigger scripts (BuildingAZoneTrigger)

        public void OnEnterCoreHub()
        {
            if (!_bigEGlassDone) StartCoroutine(BigEThroughGlass());
        }

        public void OnEnterSafeRoom()
        {
            if (!_safeRoomKnockDone) StartCoroutine(SafeRoomKnock());
        }

        public void OnEnterMedRoom()
        {
            if (!_wrongLabelDone && GameDirector.Instance?.CurrentPhase >= GamePhase.MidGame)
                StartCoroutine(WrongRoomLabel());
        }

        // ─── 1. Big E Through Glass ──────────────────────────────────────────

        private IEnumerator BigEThroughGlass()
        {
            _bigEGlassDone = true;

            // Wait for player to settle into Core Hub
            yield return new WaitForSeconds(3f);

            if (_bigE == null || _glassWalkwayBigEPosition == null) yield break;
            if (!FairnessValidator.Instance.QuickValidate(
                    FairnessThreatType.GeometryShift, hadWarning: true, hasCounterplay: true))
                yield break;

            // Move Big E to walkway position silently
            _bigE.transform.position  = _glassWalkwayBigEPosition.position;
            _bigE.PresenceModule?.Show();

            // Face downward toward the Core Hub (player is below)
            var downward = Vector3.down + (_bigE.transform.position - Camera.main.transform.position).normalized * 0.3f;
            _bigE.transform.rotation = Quaternion.LookRotation(downward);

            // Hold — motionless for maximum dread
            yield return new WaitForSeconds(_bigEViewDuration);

            // Big E withdraws if player approaches stairs (i.e. position check)
            var player = PlayerController.Instance;
            if (player != null)
            {
                float dist = Vector3.Distance(player.transform.position, _glassWalkwayBigEPosition.position);
                if (dist < 8f)
                {
                    _bigE.PresenceModule?.Hide();
                    _bigE.SetPresenceCooldown();
                }
            }

            Debug.Log("[BuildingAEvents] Big E Through Glass — complete");
        }

        // ─── 2. Safe Room Knock ──────────────────────────────────────────────

        private IEnumerator SafeRoomKnock()
        {
            _safeRoomKnockDone = true;

            // Wait for player to settle — they need to feel safe first
            yield return new WaitForSeconds(_knockDelay);

            if (_safeRoomDoor == null || _knockClip == null) yield break;

            // FairnessValidator: warning = the sound itself is the warning
            // counterplay = player can just stay put (door never opens)
            for (int i = 0; i < _knockCount; i++)
            {
                _safeRoomDoor.PlayOneShot(_knockClip);
                yield return new WaitForSeconds(_knockInterval);
            }

            // Tension bump — suspicion only, never panic
            GameDirector.Instance?.TransitionToTension(TensionState.Suspicion);

            // Bump Big E attention — it was watching
            // (Big E's OnPlayerNoise handler will catch this ambient cue)
            EventBus.Publish(new PlayerNoiseEvent
            {
                Origin = _safeRoomDoor.transform.position,
                Radius = 5f,
                Type   = NoiseType.Collision
            });

            Debug.Log("[BuildingAEvents] Safe Room Knock — complete");
        }

        // ─── 3. Wrong Room Label ─────────────────────────────────────────────

        private IEnumerator WrongRoomLabel()
        {
            _wrongLabelDone = true;

            var room = WorldStateManager.Instance?.GetRoom(_medRoomId);
            if (room == null) yield break;

            room.SetCorruptedLabel(_falseLabel);
            EventBus.Publish(new RoomStateChangedEvent
            {
                RoomId      = _medRoomId,
                ChangedFlag = RoomStateFlag.LabelCorrupted
            });

            yield return new WaitForSeconds(_labelCorruptDuration);

            WorldStateManager.Instance?.SetLabelCorrupted(_medRoomId, false);
            Debug.Log("[BuildingAEvents] Wrong Room Label — restored");
        }

        // ─── 4. CCTV Betrayal ────────────────────────────────────────────────

        public void TriggerCCTVBetrayalIfReady()
        {
            if (_cctvBetrayalDone) return;
            if (GameDirector.Instance?.CurrentPhase < GamePhase.MidGame) return;
            StartCoroutine(CCTVBetrayalSequence());
        }

        private IEnumerator CCTVBetrayalSequence()
        {
            _cctvBetrayalDone = true;
            if (_targetMonitor == null) yield break;

            // Show empty room on CCTV
            _targetMonitor.ShowFeed(CCTVFeedState.Clear);

            yield return new WaitForSeconds(3f);

            // Big E is actually in that room — place it there quietly
            if (_bigE != null)
            {
                var room = WorldStateManager.Instance?.GetRoom(_betrayalRoomId);
                if (room != null)
                {
                    _bigE.transform.position = room.transform.position + Vector3.forward * 2f;
                    _bigE.PresenceModule?.Show();
                }
            }

            // CCTV feed stays "clear" — it doesn't show Big E
            yield return new WaitForSeconds(5f);

            // Static burst — feed corrupts
            _targetMonitor.ShowFeed(CCTVFeedState.Static);

            Debug.Log("[BuildingAEvents] CCTV Betrayal — complete");
        }

        // ─── 5. Power Return Consequence ─────────────────────────────────────

        public void TriggerPowerReturn()
        {
            if (_powerReturnDone) return;
            StartCoroutine(PowerReturnSequence());
        }

        private IEnumerator PowerReturnSequence()
        {
            _powerReturnDone = true;

            // Lights flicker on
            if (_powerAudioSource && _electricalHumClip)
                _powerAudioSource.PlayOneShot(_electricalHumClip);

            foreach (var light in _powerReturnLights)
            {
                if (light == null) continue;
                light.enabled = false;
                yield return new WaitForSeconds(Random.Range(0.05f, 0.3f));
                light.enabled = true;
            }

            yield return new WaitForSeconds(1.5f);

            // New route opens — but so does threat path
            if (_newThreatDoor != null)
                _newThreatDoor.SetLocked(false);

            // FacilityBrain hostility jumps — power restored = systems online
            FacilityBrain.Instance?.ImportHostility(
                Mathf.Min(1f, (FacilityBrain.Instance?.HostilityLevel ?? 0f) + 0.2f));

            // Push tension to Pressure
            GameDirector.Instance?.TransitionToTension(TensionState.Pressure);

            Debug.Log("[BuildingAEvents] Power Return Consequence — complete");
        }

        // ─── 6. Favourite Shortcut Closure ───────────────────────────────────

        private void CheckShortcutClosure()
        {
            if (_shortcutClosedDone) return;
            if (GameDirector.Instance?.CurrentPhase < GamePhase.MidGame) return;

            int visits = WorldStateManager.Instance?.GetVisitCount(_shortcutRoomId) ?? 0;
            if (visits >= _shortcutVisitThreshold)
                StartCoroutine(WeldShortcut());
        }

        private IEnumerator WeldShortcut()
        {
            _shortcutClosedDone = true;
            if (_shortcutDoor == null) yield break;

            // Distant welding sounds first (warning — player can hear it happening)
            if (_powerAudioSource && _weldingClip)
            {
                _powerAudioSource.PlayOneShot(_weldingClip);
                yield return new WaitForSeconds(_weldingClip.length);
            }

            _shortcutDoor.SetLocked(true);
            if (_weldedVisual) _weldedVisual.SetActive(true);

            // FacilityBrain confirms: hot route now blocked
            EventBus.Publish(new FacilityBrainActionEvent
            {
                ActionType = FacilityActionType.LockDoor,
                TargetId   = "shortcut_utility_door"
            });

            Debug.Log("[BuildingAEvents] Favourite Shortcut Closed — welded");
        }

        // ─── Event Handlers ──────────────────────────────────────────────────

        private void OnRoomVisited(RoomStateChangedEvent evt)
        {
            // CCTV betrayal armed when player accesses CCTV room
            if (evt.RoomId == "cctv_security_room")
                TriggerCCTVBetrayalIfReady();
        }

        private void OnObjectiveCompleted(ObjectiveCompletedEvent evt)
        {
            // Power return fires when generator objective completes
            if (evt.ObjectiveId == "obj_restore_power_a")
                TriggerPowerReturn();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BUILDING A ZONE TRIGGER
    // Lightweight trigger placed on each major room volume.
    // Routes enter/exit events to BuildingAEventDirector.
    // Avoids cluttering RoomNode with building-specific logic.
    // ─────────────────────────────────────────────────────────────────────────

    public class BuildingAZoneTrigger : MonoBehaviour
    {
        public enum ZoneType { CoreHub, SafeRoom, MedRoom, EastWing, Other }

        [SerializeField] private ZoneType _zone;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var dir = BuildingAEventDirector.Instance;
            if (dir == null) return;

            switch (_zone)
            {
                case ZoneType.CoreHub:   dir.OnEnterCoreHub();  break;
                case ZoneType.SafeRoom:  dir.OnEnterSafeRoom(); break;
                case ZoneType.MedRoom:   dir.OnEnterMedRoom();  break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CCTV MONITOR
    // Displays camera feed states. Used by CCTV Betrayal moment.
    // ─────────────────────────────────────────────────────────────────────────

    public class CCTVMonitor : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.RawImage _feedDisplay;
        [SerializeField] private Texture2D               _clearFeedTexture;
        [SerializeField] private Texture2D               _staticTexture;
        [SerializeField] private Texture2D               _offTexture;
        [SerializeField] private UnityEngine.UI.Text     _roomLabel;
        [SerializeField] private string                  _monitoredRoomId;

        private CCTVFeedState _currentState = CCTVFeedState.Off;

        public void ShowFeed(CCTVFeedState state)
        {
            _currentState = state;
            if (_feedDisplay == null) return;
            _feedDisplay.texture = state switch
            {
                CCTVFeedState.Clear  => _clearFeedTexture,
                CCTVFeedState.Static => _staticTexture,
                CCTVFeedState.Off    => _offTexture,
                _ => _offTexture
            };
        }

        // Remote scouting: player examines CCTV to check rooms
        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.E)) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (Physics.Raycast(cam.transform.position, cam.transform.forward,
                    out RaycastHit hit, 2.5f) && hit.collider.gameObject == gameObject)
                ActivateMonitor();
        }

        private void ActivateMonitor()
        {
            // Check if room is actually safe or has a threat
            var room = WorldStateManager.Instance?.GetRoom(_monitoredRoomId);
            if (room == null) return;

            // Deliberately do NOT check Big E's actual presence here —
            // that gap is the CCTV Betrayal. Feed always shows "clear" until corrupted.
            ShowFeed(_currentState == CCTVFeedState.Off ? CCTVFeedState.Clear : _currentState);

            if (_roomLabel)
                _roomLabel.text = room.DisplayLabel;
        }
    }

    public enum CCTVFeedState { Off, Clear, Static }
}
