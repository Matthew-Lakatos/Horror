using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Audio-first horror sound system.
    /// Handles: positional 3D audio, chase music layering, occlusion simulation,
    /// ambient dread drones, silence as tool, and per-room sound profiles.
    /// Uses an internal pool of AudioSources to avoid runtime allocation.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Audio Clips")]
        [SerializeField] private AudioClipRegistry _registry;

        [Header("Music Layers")]
        [SerializeField] private AudioSource _ambienceSource;
        [SerializeField] private AudioSource _tensionSource;
        [SerializeField] private AudioSource _chaseSource;
        [SerializeField] private AudioSource _recoverySource;
        [SerializeField] private float       _musicFadeDuration = 3f;

        [Header("Occlusion")]
        [SerializeField] private LayerMask   _occlusionMask;
        [SerializeField] private float       _occlusionVolumeMult = 0.3f;
        [SerializeField] private float       _occlusionLowpassHz  = 800f;

        [Header("Pool")]
        [SerializeField] private int _poolSize = 16;
        [SerializeField] private Transform _poolRoot;

        // ── Pool ───────────────────────────────────────────────────────────────
        private readonly Queue<PooledAudioSource> _pool       = new();
        private readonly List<PooledAudioSource>  _active     = new();

        // ── State ──────────────────────────────────────────────────────────────
        private TensionState _currentTension = TensionState.Calm;
        private bool         _chaseActive;
        private Coroutine    _musicTransition;
        private Transform    _playerTransform;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            BuildPool();
            var player = FindObjectOfType<Actors.PlayerController>();
            if (player) _playerTransform = player.transform;
            StartAmbience();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EvTensionChanged>   (OnTensionChanged);
            EventBus.Subscribe<EvEnemyStateChanged>(OnEnemyStateChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EvTensionChanged>   (OnTensionChanged);
            EventBus.Unsubscribe<EvEnemyStateChanged>(OnEnemyStateChanged);
        }

        private void Update()
        {
            // Return completed pool sources
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var pas = _active[i];
                if (!pas.Source.isPlaying)
                {
                    pas.Source.gameObject.SetActive(false);
                    _pool.Enqueue(pas);
                    _active.RemoveAt(i);
                }
                else
                {
                    UpdateOcclusion(pas);
                }
            }
        }

        // ── Pool ───────────────────────────────────────────────────────────────
        private void BuildPool()
        {
            for (int i = 0; i < _poolSize; i++)
            {
                var go  = new GameObject($"PooledAudio_{i}");
                go.transform.SetParent(_poolRoot != null ? _poolRoot : transform);
                var src = go.AddComponent<AudioSource>();
                var lp  = go.AddComponent<AudioLowPassFilter>();
                lp.cutoffFrequency = 22000f;
                go.SetActive(false);

                var pas = new PooledAudioSource { Source = src, LowPass = lp };
                _pool.Enqueue(pas);
            }
        }

        private PooledAudioSource GetFromPool()
        {
            PooledAudioSource pas;
            if (_pool.Count > 0)
            {
                pas = _pool.Dequeue();
            }
            else
            {
                // Steal the oldest active source
                pas = _active[0];
                _active.RemoveAt(0);
                pas.Source.Stop();
            }
            pas.Source.gameObject.SetActive(true);
            return pas;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Play a sound at a world position with full 3D spatialisation.</summary>
        public void PlayAtPosition(string clipId, Vector3 position, float volume = 1f)
        {
            var clip = _registry?.Get(clipId);
            if (clip == null) return;

            var pas = GetFromPool();
            pas.Source.clip        = clip;
            pas.Source.volume      = volume;
            pas.Source.spatialBlend = 1f;
            pas.Source.rolloffMode  = AudioRolloffMode.Linear;
            pas.Source.maxDistance  = 35f;
            pas.Source.transform.position = position;
            pas.Source.Play();

            _active.Add(pas);
        }

        /// <summary>Play a 2D UI / non-diegetic sound.</summary>
        public void PlayUI(string clipId, float volume = 1f)
        {
            var clip = _registry?.Get(clipId);
            if (clip == null) return;

            var pas = GetFromPool();
            pas.Source.clip         = clip;
            pas.Source.volume       = volume;
            pas.Source.spatialBlend = 0f;
            pas.Source.Play();
            _active.Add(pas);
        }

        /// <summary>Play a chase music layer by enemy ID.</summary>
        public void PlayChaseMusic(string chaseLayerId)
        {
            if (_chaseSource == null) return;
            var clip = _registry?.Get(chaseLayerId ?? "audio_chase_default");
            if (clip != null) _chaseSource.clip = clip;

            if (_musicTransition != null) StopCoroutine(_musicTransition);
            _musicTransition = StartCoroutine(FadeIn(_chaseSource, 1f, _musicFadeDuration));
            _chaseActive = true;
        }

        public void StopChaseMusic()
        {
            if (_musicTransition != null) StopCoroutine(_musicTransition);
            _musicTransition = StartCoroutine(FadeOut(_chaseSource, _musicFadeDuration));
            _chaseActive = false;
        }

        /// <summary>Play an ambient sound profile for the entered room.</summary>
        public void SetRoomAmbience(string profileId)
        {
            var clip = _registry?.Get(profileId);
            if (clip == null || _ambienceSource == null) return;
            if (_ambienceSource.clip == clip) return;

            StartCoroutine(CrossfadeAmbience(clip));
        }

        // ── Music state machine ────────────────────────────────────────────────
        private void OnTensionChanged(EvTensionChanged evt)
        {
            _currentTension = evt.Next;
            if (_chaseActive) return;   // chase overrides

            switch (evt.Next)
            {
                case TensionState.Calm:
                    TransitionMusic(targetTension: 0f, targetDread: 0.1f);
                    break;
                case TensionState.Suspicion:
                    TransitionMusic(targetTension: 0.3f, targetDread: 0.3f);
                    break;
                case TensionState.Pressure:
                    TransitionMusic(targetTension: 0.6f, targetDread: 0.5f);
                    break;
                case TensionState.Panic:
                    TransitionMusic(targetTension: 0.9f, targetDread: 0.8f);
                    break;
                case TensionState.Recovery:
                    TransitionMusic(targetTension: 0.1f, targetDread: 0.2f);
                    break;
            }
        }

        private void OnEnemyStateChanged(EvEnemyStateChanged evt)
        {
            if (evt.Next == EnemyState.Chasing) return;    // PlayChaseMusic called by monster
            if (evt.Prev == EnemyState.Chasing && !IsAnyChaseActive())
                StopChaseMusic();
        }

        private bool IsAnyChaseActive()
        {
            return AIManager.Instance?.IsAnyEnemyChasing() ?? false;
        }

        private void TransitionMusic(float targetTension, float targetDread)
        {
            if (_musicTransition != null) StopCoroutine(_musicTransition);
            _musicTransition = StartCoroutine(FadeMusicLayers(targetTension, targetDread));
        }

        // ── Occlusion ─────────────────────────────────────────────────────────
        private void UpdateOcclusion(PooledAudioSource pas)
        {
            if (_playerTransform == null || pas.Source.spatialBlend < 0.5f) return;

            Vector3 dir  = _playerTransform.position - pas.Source.transform.position;
            bool    occ  = Physics.Raycast(pas.Source.transform.position, dir.normalized,
                                           dir.magnitude, _occlusionMask);

            pas.Source.volume              = occ ? _occlusionVolumeMult : 1f;
            pas.LowPass.cutoffFrequency    = occ ? _occlusionLowpassHz  : 22000f;
        }

        // ── Ambience startup ──────────────────────────────────────────────────
        private void StartAmbience()
        {
            if (_ambienceSource == null) return;
            var clip = _registry?.Get("audio_ambient_industrial");
            if (clip) _ambienceSource.clip = clip;
            _ambienceSource.loop   = true;
            _ambienceSource.volume = 0.4f;
            _ambienceSource.Play();

            if (_tensionSource) { _tensionSource.loop = true; _tensionSource.volume = 0f; _tensionSource.Play(); }
            if (_chaseSource)   { _chaseSource.loop   = true; _chaseSource.volume   = 0f; _chaseSource.Play(); }
            if (_recoverySource){ _recoverySource.loop = true; _recoverySource.volume = 0f; _recoverySource.Play(); }
        }

        // ── Coroutines ─────────────────────────────────────────────────────────
        private IEnumerator FadeMusicLayers(float targetTension, float targetDread)
        {
            float elapsed  = 0f;
            float startT   = _tensionSource?.volume ?? 0f;
            float startD   = _ambienceSource?.volume ?? 0.4f;

            while (elapsed < _musicFadeDuration)
            {
                float t = elapsed / _musicFadeDuration;
                if (_tensionSource)  _tensionSource.volume  = Mathf.Lerp(startT, targetTension, t);
                if (_ambienceSource) _ambienceSource.volume = Mathf.Lerp(startD, targetDread,   t);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator FadeIn(AudioSource src, float targetVolume, float duration)
        {
            if (src == null) yield break;
            float elapsed = 0f;
            float start   = src.volume;
            while (elapsed < duration)
            {
                src.volume = Mathf.Lerp(start, targetVolume, elapsed / duration);
                elapsed   += Time.deltaTime;
                yield return null;
            }
            src.volume = targetVolume;
        }

        private IEnumerator FadeOut(AudioSource src, float duration)
        {
            if (src == null) yield break;
            float elapsed = 0f;
            float start   = src.volume;
            while (elapsed < duration)
            {
                src.volume = Mathf.Lerp(start, 0f, elapsed / duration);
                elapsed   += Time.deltaTime;
                yield return null;
            }
            src.volume = 0f;
        }

        private IEnumerator CrossfadeAmbience(AudioClip newClip)
        {
            if (_ambienceSource == null) yield break;
            float startVol = _ambienceSource.volume;
            float elapsed  = 0f;
            float half     = _musicFadeDuration * 0.5f;

            while (elapsed < half)
            {
                _ambienceSource.volume = Mathf.Lerp(startVol, 0f, elapsed / half);
                elapsed += Time.deltaTime;
                yield return null;
            }

            _ambienceSource.clip = newClip;
            _ambienceSource.Play();
            elapsed = 0f;

            while (elapsed < half)
            {
                _ambienceSource.volume = Mathf.Lerp(0f, startVol, elapsed / half);
                elapsed += Time.deltaTime;
                yield return null;
            }

            _ambienceSource.volume = startVol;
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private class PooledAudioSource
        {
            public AudioSource      Source;
            public AudioLowPassFilter LowPass;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  AUDIO CLIP REGISTRY  (ScriptableObject)
    // ─────────────────────────────────────────────────────────────────────────
    [CreateAssetMenu(menuName = "Eidolon/Audio Clip Registry", fileName = "AudioClipRegistry")]
    public class AudioClipRegistry : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string    Id;
            public AudioClip Clip;
        }

        [SerializeField] private Entry[] _entries;

        private Dictionary<string, AudioClip> _map;

        private void OnEnable()
        {
            _map = new Dictionary<string, AudioClip>();
            if (_entries == null) return;
            foreach (var e in _entries)
                if (!string.IsNullOrEmpty(e.Id) && e.Clip != null)
                    _map[e.Id] = e.Clip;
        }

        public AudioClip Get(string id)
        {
            if (_map == null) OnEnable();
            return _map != null && _map.TryGetValue(id, out var c) ? c : null;
        }
    }
}
