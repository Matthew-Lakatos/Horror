using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Eidolon.UI
{
    using Core;

    /// <summary>
    /// Listens for ConditionApplied / PerceptionEvent bus messages and
    /// drives camera post-processing, audio effects, and UI distortion.
    /// Decoupled from ConditionManager — pure presentation layer.
    /// </summary>
    public class ConditionVisualRouter : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Post Processing")]
        [SerializeField] private Volume _globalVolume;

        [Header("Camera")]
        [SerializeField] private Transform _cameraRoot;
        [SerializeField] private float     _tremorAmplitude  = 0.03f;
        [SerializeField] private float     _tremorFrequency  = 8f;

        [Header("UI")]
        [SerializeField] private CanvasGroup _hudGroup;
        [SerializeField] private UnityEngine.UI.Image _vignetteOverlay;
        [SerializeField] private UnityEngine.UI.Text  _intrusiveText;
        [SerializeField] private float                _intrusiveTextDuration = 3f;

        // ── Cached post-process effects ────────────────────────────────────────
        private Vignette         _vignette;
        private ChromaticAberration _chromatic;
        private DepthOfField     _dof;
        private LensDistortion   _lensDistortion;

        // ── State ──────────────────────────────────────────────────────────────
        private readonly Dictionary<ConditionType, Coroutine> _activeCoroutines = new();
        private Vector3 _cameraBaseLocalPos;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (_globalVolume != null && _globalVolume.profile != null)
            {
                _globalVolume.profile.TryGet(out _vignette);
                _globalVolume.profile.TryGet(out _chromatic);
                _globalVolume.profile.TryGet(out _dof);
                _globalVolume.profile.TryGet(out _lensDistortion);
            }

            if (_cameraRoot) _cameraBaseLocalPos = _cameraRoot.localPosition;
            if (_intrusiveText) _intrusiveText.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EvConditionApplied>(OnConditionApplied);
            EventBus.Subscribe<EvConditionRemoved>(OnConditionRemoved);
            EventBus.Subscribe<EvPerceptionEvent> (OnPerceptionEvent);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EvConditionApplied>(OnConditionApplied);
            EventBus.Unsubscribe<EvConditionRemoved>(OnConditionRemoved);
            EventBus.Unsubscribe<EvPerceptionEvent> (OnPerceptionEvent);
        }

        // ── Condition handlers ─────────────────────────────────────────────────
        private void OnConditionApplied(EvConditionApplied evt)
        {
            StopActiveCoroutine(evt.Type);

            Coroutine c = evt.Type switch
            {
                ConditionType.Blur          => StartCoroutine(ApplyBlur(evt.Magnitude)),
                ConditionType.Tremor        => StartCoroutine(ApplyTremor(evt.Magnitude)),
                ConditionType.TunnelVision  => StartCoroutine(ApplyTunnelVision(evt.Magnitude)),
                ConditionType.ChromaticShift => StartCoroutine(ApplyChromaticShift(evt.Magnitude)),
                ConditionType.PanicBreathing => StartCoroutine(ApplyPanicBreathing(evt.Magnitude)),
                ConditionType.InputLag      => StartCoroutine(ApplyInputLag(evt.Magnitude)),
                _                           => null
            };

            if (c != null) _activeCoroutines[evt.Type] = c;
        }

        private void OnConditionRemoved(EvConditionRemoved evt)
        {
            StopActiveCoroutine(evt.Type);
            RevertEffect(evt.Type);
        }

        // ── Perception event handlers ──────────────────────────────────────────
        private void OnPerceptionEvent(EvPerceptionEvent evt)
        {
            switch (evt.Type)
            {
                case PerceptionEventType.UIDistortion:
                    StartCoroutine(UIDistortionPulse(evt.Intensity));
                    break;
                case PerceptionEventType.BlurPulse:
                    StartCoroutine(BlurPulse(evt.Intensity));
                    break;
                case PerceptionEventType.ChromaticFlash:
                    StartCoroutine(ChromaticFlash(evt.Intensity));
                    break;
                case PerceptionEventType.IntrusiveText:
                    ShowIntrusiveText(evt.Intensity);
                    break;
                case PerceptionEventType.WrongRoomLabel:
                    // Handled by HUD room label component
                    break;
                case PerceptionEventType.Tremor:
                    StartCoroutine(ApplyTremor(evt.Intensity * 0.5f));
                    break;
            }
        }

        // ── Coroutine effects ─────────────────────────────────────────────────

        private IEnumerator ApplyBlur(float magnitude)
        {
            if (_dof != null)
            {
                _dof.active          = true;
                _dof.focusDistance.Override(Mathf.Lerp(5f, 0.3f, magnitude));
                _dof.aperture.Override(Mathf.Lerp(2f, 16f, magnitude));
            }
            yield break;
        }

        private IEnumerator ApplyTremor(float magnitude)
        {
            float amp  = _tremorAmplitude * magnitude;
            float freq = _tremorFrequency;

            while (true)
            {
                float time   = Time.time;
                float xShake = Mathf.Sin(time * freq * 1.1f) * amp;
                float yShake = Mathf.Sin(time * freq * 0.9f + 0.5f) * amp * 0.5f;

                if (_cameraRoot)
                    _cameraRoot.localPosition =
                        _cameraBaseLocalPos + new Vector3(xShake, yShake, 0);

                yield return null;
            }
        }

        private IEnumerator ApplyTunnelVision(float magnitude)
        {
            if (_vignette != null)
            {
                _vignette.active     = true;
                float target         = Mathf.Lerp(0.3f, 0.75f, magnitude);
                float elapsed        = 0f;
                while (elapsed < 1.5f)
                {
                    _vignette.intensity.Override(
                        Mathf.Lerp(_vignette.intensity.value, target, elapsed / 1.5f));
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                _vignette.intensity.Override(target);
            }
        }

        private IEnumerator ApplyChromaticShift(float magnitude)
        {
            if (_chromatic != null)
            {
                _chromatic.active    = true;
                _chromatic.intensity.Override(magnitude);
            }
            yield break;
        }

        private IEnumerator ApplyPanicBreathing(float magnitude)
        {
            // Drive vignette with breathing pulse
            if (_vignette == null) yield break;
            float baseIntensity = _vignette.intensity.value;
            float breathRate    = Mathf.Lerp(0.4f, 0.8f, magnitude);

            while (true)
            {
                float pulse = Mathf.Sin(Time.time * breathRate * Mathf.PI * 2f) * 0.06f * magnitude;
                _vignette.intensity.Override(baseIntensity + pulse);
                yield return null;
            }
        }

        private IEnumerator ApplyInputLag(float magnitude)
        {
            // Input lag is delivered through Time.timeScale nudge — very subtle
            // Only used for brief, rare moments. Max 0.1 reduction.
            float reduction = 0.1f * magnitude;
            Time.timeScale  = Mathf.Max(0.7f, 1f - reduction);
            yield return new WaitForSecondsRealtime(0.4f);
            Time.timeScale  = 1f;
        }

        // ── One-shot perception pulse effects ─────────────────────────────────

        private IEnumerator UIDistortionPulse(float intensity)
        {
            if (_lensDistortion == null) yield break;
            _lensDistortion.active = true;

            float peak    = intensity * 0.6f;
            float elapsed = 0f;
            float duration = 1.2f;

            while (elapsed < duration)
            {
                float t  = elapsed / duration;
                float v  = Mathf.Sin(t * Mathf.PI) * peak;
                _lensDistortion.intensity.Override(v);
                elapsed += Time.deltaTime;
                yield return null;
            }

            _lensDistortion.intensity.Override(0f);
            _lensDistortion.active = false;
        }

        private IEnumerator BlurPulse(float intensity)
        {
            if (_dof == null) yield break;
            _dof.active = true;
            _dof.focusDistance.Override(0.5f);
            _dof.aperture.Override(16f * intensity);
            yield return new WaitForSeconds(0.8f);
            _dof.active = false;
        }

        private IEnumerator ChromaticFlash(float intensity)
        {
            if (_chromatic == null) yield break;
            _chromatic.active = true;
            _chromatic.intensity.Override(intensity);
            yield return new WaitForSeconds(0.3f);
            _chromatic.intensity.Override(0f);
            _chromatic.active = false;
        }

        private void ShowIntrusiveText(float intensity)
        {
            if (_intrusiveText == null) return;
            string[] texts = {
                "OPTIMISING",
                "SUBJECT DEVIATION NOTED",
                "RECALIBRATING",
                "ROUTE ANALYSED",
                "FEAR INDEX STABLE",
                "COMPLIANCE RECOMMENDED"
            };
            _intrusiveText.text = texts[Random.Range(0, texts.Length)];
            _intrusiveText.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.3f, 0.7f, intensity));
            _intrusiveText.gameObject.SetActive(true);
            StartCoroutine(HideIntrusiveText());
        }

        private IEnumerator HideIntrusiveText()
        {
            yield return new WaitForSeconds(_intrusiveTextDuration);
            if (_intrusiveText) _intrusiveText.gameObject.SetActive(false);
        }

        // ── Revert ────────────────────────────────────────────────────────────
        private void RevertEffect(ConditionType type)
        {
            switch (type)
            {
                case ConditionType.Blur:
                    if (_dof != null) { _dof.active = false; }
                    break;
                case ConditionType.Tremor:
                    if (_cameraRoot) _cameraRoot.localPosition = _cameraBaseLocalPos;
                    break;
                case ConditionType.TunnelVision:
                    if (_vignette != null) _vignette.intensity.Override(0.2f);
                    break;
                case ConditionType.ChromaticShift:
                    if (_chromatic != null) { _chromatic.active = false; }
                    break;
                case ConditionType.PanicBreathing:
                    if (_vignette != null) _vignette.intensity.Override(0.2f);
                    break;
                case ConditionType.InputLag:
                    Time.timeScale = 1f;
                    break;
            }
        }

        private void StopActiveCoroutine(ConditionType type)
        {
            if (_activeCoroutines.TryGetValue(type, out var co) && co != null)
            {
                StopCoroutine(co);
                _activeCoroutines.Remove(type);
            }
        }
    }
}
