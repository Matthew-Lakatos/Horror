using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Eidolon.Core;

namespace Eidolon.Audio
{
    /// <summary>
    /// Audio-first horror sound system.
    /// Manages: positional audio, machine ambience, silence as a tool,
    /// enemy signature sounds, distance occlusion, fake cues, chase layers,
    /// tinnitus/muffled conditions, and dynamic dread tone mixing.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        // ─── Channel Sources ─────────────────────────────────────────────────

        [Header("Master Sources")]
        [SerializeField] private AudioSource _ambienceSource;
        [SerializeField] private AudioSource _dreadToneSource;
        [SerializeField] private AudioSource _machinerySource;
        [SerializeField] private AudioSource _chaseLayerSource;
        [SerializeField] private AudioSource _uiSoundSource;
        [SerializeField] private AudioSource _tinnitusSource;

        [Header("Ambience Clips")]
        [SerializeField] private AudioClip _buildingAmbience;
        [SerializeField] private AudioClip _outdoorYardAmbience;
        [SerializeField] private AudioClip _tunnelAmbience;
        [SerializeField] private AudioClip _skybridgeAmbience;

        [Header("Dread Tone Clips")]
        [SerializeField] private AudioClip[] _lowDreadTones;
        [SerializeField] private AudioClip   _silenceTone;   // very faint hum for "silent" moments
        [SerializeField] private AudioClip[] _metalStressSounds;

        [Header("Chase Layers")]
        [SerializeField] private AudioClip _chaseLayer_BigE;
        [SerializeField] private AudioClip _chaseLayer_Hound;
        [SerializeField] private AudioClip _chaseLayer_Generic;

        [Header("Tinnitus")]
        [SerializeField] private AudioClip _tinnitusClip;
        [SerializeField] private float     _tinnitusMaxVolume = 0.7f;

        [Header("Occlusion")]
        [SerializeField] private LayerMask _occlusionMask;
        [SerializeField] private float     _occlusionVolumeReduction = 0.4f;

        [Header("Mixer Volumes")]
        [SerializeField] [Range(0f, 1f)] private float _masterVolume   = 1f;
        [SerializeField] [Range(0f, 1f)] private float _sfxVolume      = 1f;
        [SerializeField] [Range(0f, 1f)] private float _ambienceVolume = 0.6f;
        [SerializeField] [Range(0f, 1f)] private float _musicVolume    = 0.5f;

        // ─── Runtime State ───────────────────────────────────────────────────

        private TensionState   _lastTension     = TensionState.Calm;
        private float          _currentDreadVolume;
        private float          _targetDreadVolume;
        private bool           _chaseActive;
        private EnemyType      _chaseSource;
        private bool           _tinnitusActive;
        private float          _tinnitusExpiry;
        private bool           _muffledActive;
        private float          _muffledExpiry;

        // Pooled positional audio sources
        private readonly Queue<AudioSource> _positionalPool = new Queue<AudioSource>();
        private const int PositionalPoolSize = 8;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            BuildPositionalPool();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<TensionStateChangedEvent>(OnTensionChanged);
            EventBus.Subscribe<EnemyDetectedPlayerEvent>(OnEnemyDetected);
            EventBus.Subscribe<EnemyLostPlayerEvent>(OnEnemyLost);
            EventBus.Subscribe<ConditionAppliedEvent>(OnConditionApplied);
            EventBus.Subscribe<FacilityBrainActionEvent>(OnFacilityAction);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<TensionStateChangedEvent>(OnTensionChanged);
            EventBus.Unsubscribe<EnemyDetectedPlayerEvent>(OnEnemyDetected);
            EventBus.Unsubscribe<EnemyLostPlayerEvent>(OnEnemyLost);
            EventBus.Unsubscribe<ConditionAppliedEvent>(OnConditionApplied);
            EventBus.Unsubscribe<FacilityBrainActionEvent>(OnFacilityAction);
        }

        private void Start()
        {
            StartAmbience(BuildingAmbienceType.Interior);
            StartCoroutine(MetalStressLoop());
        }

        private void Update()
        {
            TickDreadVolume();
            TickTinnitus();
            TickMuffled();
        }

        // ─── Ambience ────────────────────────────────────────────────────────

        public void StartAmbience(BuildingAmbienceType type)
        {
            if (_ambienceSource == null) return;

            var clip = type switch
            {
                BuildingAmbienceType.Interior  => _buildingAmbience,
                BuildingAmbienceType.Yard       => _outdoorYardAmbience,
                BuildingAmbienceType.Tunnel     => _tunnelAmbience,
                BuildingAmbienceType.Skybridge  => _skybridgeAmbience,
                _ => _buildingAmbience
            };

            if (clip == null || _ambienceSource.clip == clip) return;

            StartCoroutine(CrossfadeAmbience(clip, 2.5f));
        }

        private IEnumerator CrossfadeAmbience(AudioClip newClip, float duration)
        {
            float startVol = _ambienceSource.volume;
            float t = 0f;

            // Fade out
            while (t < duration * 0.5f)
            {
                _ambienceSource.volume = Mathf.Lerp(startVol, 0f, t / (duration * 0.5f));
                t += Time.deltaTime;
                yield return null;
            }

            _ambienceSource.clip = newClip;
            _ambienceSource.loop = true;
            _ambienceSource.Play();

            t = 0f;
            // Fade in
            while (t < duration * 0.5f)
            {
                _ambienceSource.volume = Mathf.Lerp(0f, _ambienceVolume * _masterVolume, t / (duration * 0.5f));
                t += Time.deltaTime;
                yield return null;
            }

            _ambienceSource.volume = _ambienceVolume * _masterVolume;
        }

        // ─── Dread Tones ─────────────────────────────────────────────────────

        private void TickDreadVolume()
        {
            _currentDreadVolume = Mathf.Lerp(_currentDreadVolume, _targetDreadVolume, Time.deltaTime * 0.8f);
            if (_dreadToneSource != null)
                _dreadToneSource.volume = _currentDreadVolume * _musicVolume * _masterVolume;
        }

        private void SetDreadIntensity(float intensity)
        {
            _targetDreadVolume = Mathf.Clamp01(intensity);

            if (_dreadToneSource != null && !_dreadToneSource.isPlaying && _lowDreadTones.Length > 0)
            {
                _dreadToneSource.clip = _lowDreadTones[Random.Range(0, _lowDreadTones.Length)];
                _dreadToneSource.loop = true;
                _dreadToneSource.Play();
            }
        }

        // ─── Chase Layers ─────────────────────────────────────────────────────

        private void StartChaseLayer(EnemyType type)
        {
            if (_chaseLayerSource == null) return;
            _chaseActive = true;
            _chaseSource = type;

            var clip = type switch
            {
                EnemyType.BigE  => _chaseLayer_BigE,
                EnemyType.Hound => _chaseLayer_Hound,
                _ => _chaseLayer_Generic
            };

            if (clip == null) return;

            _chaseLayerSource.clip   = clip;
            _chaseLayerSource.loop   = true;
            _chaseLayerSource.volume = 0f;
            _chaseLayerSource.Play();
            StartCoroutine(FadeSource(_chaseLayerSource, 0f, _musicVolume * _masterVolume, 1.5f));
        }

        private void StopChaseLayer()
        {
            if (!_chaseActive) return;
            _chaseActive = false;
            StartCoroutine(FadeSourceThenStop(_chaseLayerSource, 3f));
        }

        // ─── Positional Audio Pool ────────────────────────────────────────────

        private void BuildPositionalPool()
        {
            for (int i = 0; i < PositionalPoolSize; i++)
            {
                var go = new GameObject($"PositionalAudio_{i}");
                go.transform.SetParent(transform);
                var src = go.AddComponent<AudioSource>();
                src.spatialBlend = 1f;
                src.rolloffMode  = AudioRolloffMode.Linear;
                src.maxDistance  = 40f;
                _positionalPool.Enqueue(src);
            }
        }

        /// <summary>
        /// Plays a positional clip at world position with optional occlusion check.
        /// </summary>
        public void PlayPositional(AudioClip clip, Vector3 worldPos, float volume = 1f, bool checkOcclusion = true)
        {
            if (clip == null || _positionalPool.Count == 0) return;

            var src = _positionalPool.Dequeue();
            src.transform.position = worldPos;

            float finalVolume = volume * _sfxVolume * _masterVolume;

            // Occlusion: reduce volume if wall between listener and source
            if (checkOcclusion && Camera.main != null)
            {
                var dir = Camera.main.transform.position - worldPos;
                if (Physics.Raycast(worldPos, dir.normalized, dir.magnitude, _occlusionMask))
                    finalVolume *= _occlusionVolumeReduction;
            }

            src.volume = finalVolume;
            src.PlayOneShot(clip);
            StartCoroutine(ReturnToPool(src, clip.length + 0.1f));
        }

        private IEnumerator ReturnToPool(AudioSource src, float delay)
        {
            yield return new WaitForSeconds(delay);
            _positionalPool.Enqueue(src);
        }

        // ─── Metal Stress Sounds ─────────────────────────────────────────────

        private IEnumerator MetalStressLoop()
        {
            while (true)
            {
                float interval = Random.Range(15f, 45f);
                yield return new WaitForSeconds(interval);

                if (_metalStressSounds == null || _metalStressSounds.Length == 0) continue;
                if (GameDirector.Instance?.CurrentTension == TensionState.Recovery) continue;

                var clip = _metalStressSounds[Random.Range(0, _metalStressSounds.Length)];
                // Play from a random room-ish position
                var offset = Random.insideUnitSphere * 20f;
                offset.y = 0f;
                var pos = (Camera.main != null ? Camera.main.transform.position : Vector3.zero) + offset;
                PlayPositional(clip, pos, 0.5f, true);
            }
        }

        // ─── Silence as Tool ─────────────────────────────────────────────────

        /// <summary>
        /// Strategically cuts all ambient sound for dramatic silence effect.
        /// </summary>
        public void StrategicSilence(float duration)
        {
            StartCoroutine(SilenceRoutine(duration));
        }

        private IEnumerator SilenceRoutine(float duration)
        {
            float savedAmbience = _ambienceSource != null ? _ambienceSource.volume : 0f;
            float savedDread    = _currentDreadVolume;

            if (_ambienceSource) _ambienceSource.volume = 0f;
            _targetDreadVolume = 0f;

            // Barely audible hum during silence
            if (_silenceTone != null && _uiSoundSource != null)
                _uiSoundSource.PlayOneShot(_silenceTone, 0.05f);

            yield return new WaitForSeconds(duration);

            if (_ambienceSource) _ambienceSource.volume = savedAmbience;
            _targetDreadVolume = savedDread;
        }

        // ─── Tinnitus / Muffled ──────────────────────────────────────────────

        private void TickTinnitus()
        {
            if (!_tinnitusActive) return;
            if (Time.time >= _tinnitusExpiry)
            {
                _tinnitusActive = false;
                StartCoroutine(FadeSource(_tinnitusSource, _tinnitusSource?.volume ?? 0f, 0f, 2f));
            }
        }

        private void TickMuffled()
        {
            // Muffled hearing: reduce SFX and ambience high-frequency via low-pass
            // In production, this would drive an AudioMixer low-pass filter parameter
            if (!_muffledActive) return;
            if (Time.time >= _muffledExpiry)
                _muffledActive = false;
        }

        public void ActivateTinnitus(float duration, float magnitude)
        {
            if (_tinnitusSource == null || _tinnitusClip == null) return;
            _tinnitusActive = true;
            _tinnitusExpiry = Time.time + duration;

            if (!_tinnitusSource.isPlaying)
            {
                _tinnitusSource.clip   = _tinnitusClip;
                _tinnitusSource.loop   = true;
                _tinnitusSource.volume = 0f;
                _tinnitusSource.Play();
            }
            StartCoroutine(FadeSource(_tinnitusSource, _tinnitusSource.volume, _tinnitusMaxVolume * magnitude, 0.5f));
        }

        private void ActivateMuffled(float duration)
        {
            _muffledActive = true;
            _muffledExpiry = Time.time + duration;
            // Drive AudioMixer parameter in production
        }

        // ─── Machinery Sound ────────────────────────────────────────────────

        public void TriggerMachineryRoar()
        {
            if (_machinerySource == null) return;
            if (!_machinerySource.isPlaying)
            {
                _machinerySource.volume = 0.7f * _sfxVolume * _masterVolume;
                _machinerySource.Play();
                StartCoroutine(FadeSourceThenStop(_machinerySource, 5f));
            }
        }

        // ─── Facility Siren ─────────────────────────────────────────────────

        public void TriggerSiren(bool on)
        {
            // Siren clip handled by a dedicated source on the siren object
            // Notified through FacilityBrainActionEvent → world objects respond
        }

        // ─── Utility Coroutines ─────────────────────────────────────────────

        private IEnumerator FadeSource(AudioSource src, float from, float to, float duration)
        {
            if (src == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                src.volume = Mathf.Lerp(from, to, t / duration);
                t += Time.deltaTime;
                yield return null;
            }
            src.volume = to;
        }

        private IEnumerator FadeSourceThenStop(AudioSource src, float fadeDuration)
        {
            if (src == null) yield break;
            float startVol = src.volume;
            yield return FadeSource(src, startVol, 0f, fadeDuration);
            src.Stop();
        }

        // ─── Event Handlers ──────────────────────────────────────────────────

        private void OnTensionChanged(TensionStateChangedEvent evt)
        {
            float dreadTarget = evt.Current switch
            {
                TensionState.Calm      => 0.1f,
                TensionState.Suspicion => 0.3f,
                TensionState.Pressure  => 0.55f,
                TensionState.Panic     => 0.85f,
                TensionState.Recovery  => 0.15f,
                _ => 0.1f
            };
            SetDreadIntensity(dreadTarget);

            // Strategic silence on recovery
            if (evt.Current == TensionState.Recovery && evt.Previous == TensionState.Panic)
                StrategicSilence(3f);
        }

        private void OnEnemyDetected(EnemyDetectedPlayerEvent evt)
        {
            StartChaseLayer(evt.Type);
        }

        private void OnEnemyLost(EnemyLostPlayerEvent evt)
        {
            if (_chaseSource == evt.Type)
                StopChaseLayer();
        }

        private void OnConditionApplied(ConditionAppliedEvent evt)
        {
            switch (evt.ConditionType)
            {
                case ConditionType.Tinnitus:
                    ActivateTinnitus(evt.Duration, evt.Magnitude);
                    break;
                case ConditionType.MuffledHearing:
                    ActivateMuffled(evt.Duration);
                    break;
            }
        }

        private void OnFacilityAction(FacilityBrainActionEvent evt)
        {
            if (evt.ActionType == FacilityActionType.TriggerMachinery)
                TriggerMachineryRoar();

            if (evt.ActionType == FacilityActionType.ActivateSiren)
                TriggerSiren(evt.TargetId == "siren_on");
        }

        // ─── Volume Control ─────────────────────────────────────────────────

        public void SetMasterVolume(float v)
        {
            _masterVolume = Mathf.Clamp01(v);
            if (_ambienceSource)  _ambienceSource.volume  = _ambienceVolume * _masterVolume;
            if (_dreadToneSource) _dreadToneSource.volume = _currentDreadVolume * _musicVolume * _masterVolume;
        }
    }

    public enum BuildingAmbienceType { Interior, Yard, Tunnel, Skybridge }
}
