using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Eidolon.Core;
using Eidolon.Actors;
using Eidolon.Objectives;
using Eidolon.Data;

namespace Eidolon.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // MAIN HUD
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives all in-game HUD elements: health/stamina bars, inventory strip,
    /// active objective display, and perception distortion effects.
    /// Perception effects driven by PerceptionManager — not directly by HUD.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("Status Bars")]
        [SerializeField] private Slider _healthBar;
        [SerializeField] private Slider _staminaBar;
        [SerializeField] private Slider _batteryBar;

        [Header("Inventory Display")]
        [SerializeField] private Text  _bandageCount;
        [SerializeField] private Text  _batteryCount;
        [SerializeField] private Text  _decoyCount;
        [SerializeField] private Image _wrenchIcon;
        [SerializeField] private Color _iconActiveColor   = Color.white;
        [SerializeField] private Color _iconInactiveColor = new Color(1f, 1f, 1f, 0.25f);

        [Header("Objective Display")]
        [SerializeField] private Text  _primaryObjectiveText;
        [SerializeField] private Text  _secondaryObjectiveText;
        [SerializeField] private float _objectiveDisplayDuration = 5f;
        [SerializeField] private CanvasGroup _objectivePanel;

        [Header("Perception Overlays")]
        [SerializeField] private CanvasGroup _distortionOverlay;
        [SerializeField] private Text        _intrusiveText;
        [SerializeField] private Image       _vignette;
        [SerializeField] private float       _vignetteBaseAlpha = 0.15f;

        [Header("Room Label")]
        [SerializeField] private Text  _roomLabel;
        [SerializeField] private float _labelFadeDelay = 2f;

        private Coroutine _objectiveFadeCoroutine;
        private Coroutine _labelFadeCoroutine;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void OnEnable()
        {
            EventBus.Subscribe<ObjectiveActivatedEvent>(OnObjectiveActivated);
            EventBus.Subscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
            EventBus.Subscribe<PerceptionEffectEvent>(OnPerceptionEffect);
            EventBus.Subscribe<RoomStateChangedEvent>(OnRoomStateChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ObjectiveActivatedEvent>(OnObjectiveActivated);
            EventBus.Unsubscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
            EventBus.Unsubscribe<PerceptionEffectEvent>(OnPerceptionEffect);
            EventBus.Unsubscribe<RoomStateChangedEvent>(OnRoomStateChanged);
        }

        private void Update()
        {
            RefreshStatusBars();
            RefreshInventory();
            RefreshVignette();
        }

        // ─── Status Bars ────────────────────────────────────────────────────

        private void RefreshStatusBars()
        {
            var player = PlayerController.Instance;
            if (player == null) return;

            if (_healthBar)  _healthBar.value  = player.Health  / 100f;
            if (_staminaBar) _staminaBar.value = player.Stamina / 100f;
            if (_batteryBar) _batteryBar.value = player.Battery / 100f;
        }

        // ─── Inventory ──────────────────────────────────────────────────────

        private void RefreshInventory()
        {
            var res = ResourceManager.Instance;
            if (res == null) return;

            if (_bandageCount) _bandageCount.text = res.Bandages.ToString();
            if (_batteryCount) _batteryCount.text = res.Batteries.ToString();
            if (_decoyCount)   _decoyCount.text   = res.Decoys.ToString();
            if (_wrenchIcon)   _wrenchIcon.color  = res.HasWrench ? _iconActiveColor : _iconInactiveColor;
        }

        // ─── Vignette (stress-driven) ────────────────────────────────────────

        private void RefreshVignette()
        {
            var player = PlayerController.Instance;
            if (player == null || _vignette == null) return;

            float stressAlpha = Mathf.Lerp(_vignetteBaseAlpha, 0.6f, player.HiddenStress);
            var c = _vignette.color;
            _vignette.color = new Color(c.r, c.g, c.b, stressAlpha);
        }

        // ─── Objective Display ───────────────────────────────────────────────

        private void ShowObjective(string text, bool isPrimary)
        {
            var targetText = isPrimary ? _primaryObjectiveText : _secondaryObjectiveText;
            if (targetText) targetText.text = text;

            if (_objectiveFadeCoroutine != null) StopCoroutine(_objectiveFadeCoroutine);
            _objectiveFadeCoroutine = StartCoroutine(ShowThenFadePanel(_objectivePanel, _objectiveDisplayDuration));
        }

        private IEnumerator ShowThenFadePanel(CanvasGroup group, float displayTime)
        {
            if (group == null) yield break;
            group.alpha = 1f;
            yield return new WaitForSeconds(displayTime);

            float t = 0f;
            float fadeDuration = 1f;
            while (t < fadeDuration)
            {
                group.alpha = Mathf.Lerp(1f, 0f, t / fadeDuration);
                t += Time.deltaTime;
                yield return null;
            }
            group.alpha = 0f;
        }

        // ─── Room Label ──────────────────────────────────────────────────────

        public void ShowRoomLabel(string label, bool isCorrupted = false)
        {
            if (_roomLabel == null) return;
            _roomLabel.text  = label;
            _roomLabel.color = isCorrupted ? Color.red : Color.white;

            if (_labelFadeCoroutine != null) StopCoroutine(_labelFadeCoroutine);
            _labelFadeCoroutine = StartCoroutine(FadeLabel(_labelFadeDelay));
        }

        private IEnumerator FadeLabel(float delay)
        {
            if (_roomLabel == null) yield break;
            yield return new WaitForSeconds(delay);

            float t = 0f;
            var startColor = _roomLabel.color;
            while (t < 1f)
            {
                _roomLabel.color = Color.Lerp(startColor, Color.clear, t);
                t += Time.deltaTime;
                yield return null;
            }
            _roomLabel.color = Color.clear;
        }

        // ─── Perception Effects ──────────────────────────────────────────────

        private void OnPerceptionEffect(PerceptionEffectEvent evt)
        {
            switch (evt.EffectType)
            {
                case PerceptionEffectType.UIDistort:
                    StartCoroutine(DistortUI(evt.Intensity, evt.Duration));
                    break;
                case PerceptionEffectType.IntrusiveText:
                    // Handled by PerceptionManager directly
                    break;
                case PerceptionEffectType.RoomLabelFlicker:
                    StartCoroutine(FlickerRoomLabel());
                    break;
            }
        }

        private IEnumerator DistortUI(float intensity, float duration)
        {
            if (_distortionOverlay == null) yield break;
            _distortionOverlay.alpha = intensity;
            yield return new WaitForSeconds(duration);

            float t = 0f;
            while (t < 0.5f)
            {
                _distortionOverlay.alpha = Mathf.Lerp(intensity, 0f, t / 0.5f);
                t += Time.deltaTime;
                yield return null;
            }
            _distortionOverlay.alpha = 0f;
        }

        private IEnumerator FlickerRoomLabel()
        {
            if (_roomLabel == null) yield break;
            var original = _roomLabel.text;
            for (int i = 0; i < 3; i++)
            {
                _roomLabel.text  = GenerateGlitchText(original);
                _roomLabel.color = Color.red;
                yield return new WaitForSeconds(0.1f);
                _roomLabel.text  = original;
                _roomLabel.color = Color.white;
                yield return new WaitForSeconds(0.15f);
            }
        }

        private string GenerateGlitchText(string input)
        {
            char[] chars = input.ToCharArray();
            for (int i = 0; i < Mathf.Min(3, chars.Length); i++)
            {
                int idx = Random.Range(0, chars.Length);
                chars[idx] = (char)Random.Range(65, 91);
            }
            return new string(chars);
        }

        // ─── Event Handlers ─────────────────────────────────────────────────

        private void OnObjectiveActivated(ObjectiveActivatedEvent evt)
        {
            var obj = ObjectiveManager.Instance?.GetObjective(evt.ObjectiveId);
            if (obj != null)
                ShowObjective($"▶ {obj.Data.DisplayName}", isPrimary: obj.Data.Tier == ObjectiveTier.Primary);
        }

        private void OnObjectiveCompleted(ObjectiveCompletedEvent evt)
        {
            var obj = ObjectiveManager.Instance?.GetObjective(evt.ObjectiveId);
            if (obj != null)
                ShowObjective($"✓ {obj.Data.DisplayName}", isPrimary: false);
        }

        private void OnRoomStateChanged(RoomStateChangedEvent evt)
        {
            if (evt.ChangedFlag == RoomStateFlag.LabelCorrupted)
            {
                var room = WorldStateManager.Instance?.GetRoom(evt.RoomId);
                if (room != null)
                    ShowRoomLabel(room.DisplayLabel, room.IsLabelCorrupted);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INTRO SEQUENCE CONTROLLER
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives the 2-4 minute intro sequence:
    /// Room → Email → Research → Laptop Close → Breath → Line → Arrival.
    /// Establishes greed, mystery, legitimacy, player motivation.
    /// </summary>
    public class IntroSequenceController : MonoBehaviour
    {
        [Header("Intro UI Panels")]
        [SerializeField] private GameObject _desktopPanel;
        [SerializeField] private GameObject _emailPanel;
        [SerializeField] private GameObject _browserPanel;
        [SerializeField] private GameObject _laptopClosePanel;
        [SerializeField] private CanvasGroup _fadeOverlay;

        [Header("Email Content")]
        [SerializeField] private Text _emailSubjectText;
        [SerializeField] private Text _emailBodyText;
        [SerializeField] private Text _payoutAmountText;
        [SerializeField] private string _emailSubject = "GOVERNMENT CONTRACT — URGENT: Eidolon Facility Inspection";
        [SerializeField] private string _payoutAmount = "$84,000";

        [Header("Browser Search Results")]
        [SerializeField] private List<SearchResultEntry> _searchResults = new List<SearchResultEntry>();
        [SerializeField] private Transform _searchResultsContainer;
        [SerializeField] private GameObject _searchResultPrefab;

        [Header("Audio")]
        [SerializeField] private AudioClip _typingClip;
        [SerializeField] private AudioClip _emailNotifClip;
        [SerializeField] private AudioClip _laptopCloseClip;
        [SerializeField] private AudioClip _breathClip;
        [SerializeField] private AudioClip _ambientRoomClip;

        [Header("Voice Line")]
        [SerializeField] private AudioClip _openingLineClip; // "...Let's check it out."
        [SerializeField] private string    _openingLineText  = "...Let's check it out.";

        [Header("Timing")]
        [SerializeField] private float _emailDelay          = 3f;
        [SerializeField] private float _readEmailDuration   = 6f;
        [SerializeField] private float _searchDuration      = 8f;
        [SerializeField] private float _breathPauseDuration = 2f;

        [Header("Scene Transition")]
        [SerializeField] private string _facilitySceneName = "FacilityMain";

        private AudioSource _audioSource;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        }

        private void Start()
        {
            StartCoroutine(RunIntroSequence());
        }

        // ─── Intro Sequence ──────────────────────────────────────────────────

        private IEnumerator RunIntroSequence()
        {
            // 1. Late-night room
            ShowOnly(_desktopPanel);
            PlayClip(_ambientRoomClip);
            yield return new WaitForSeconds(_emailDelay);

            // 2. Email notification
            PlayClip(_emailNotifClip);
            ShowOnly(_emailPanel);

            if (_emailSubjectText) _emailSubjectText.text = _emailSubject;
            if (_payoutAmountText) _payoutAmountText.text = _payoutAmount;

            // 3. Payout clearly shown — typing reveal
            if (_emailBodyText)
                yield return TypewriterReveal(_emailBodyText,
                    $"You have been selected for a government facility inspection contract.\n\n" +
                    $"Facility: Eidolon Industrial Cognitive Facility\n" +
                    $"Classification: ACTIVE — Autonomous Operation\n" +
                    $"Compensation: {_payoutAmount}\n\n" +
                    $"Report to the facility entrance at your earliest convenience.\n" +
                    $"All access credentials are enclosed.", 0.03f);

            yield return new WaitForSeconds(_readEmailDuration);

            // 4. Gongle search
            ShowOnly(_browserPanel);
            yield return RunSearchSequence();

            // 5. Laptop closes abruptly
            PlayClip(_laptopCloseClip);
            ShowOnly(_laptopClosePanel);
            yield return new WaitForSeconds(0.8f);

            // 6. Audible breath
            PlayClip(_breathClip);
            yield return new WaitForSeconds(_breathPauseDuration);

            // 7. Quiet opening line
            if (_openingLineClip) PlayClip(_openingLineClip);

            // Fade to black
            yield return FadeOut(2f);

            // 8. Transition to facility
            yield return new WaitForSeconds(0.5f);
            UnityEngine.SceneManagement.SceneManager.LoadScene(_facilitySceneName);
        }

        private IEnumerator RunSearchSequence()
        {
            // Searches accelerate — shown as sequential rapid additions
            var searches = new List<string>
            {
                "Eidolon Industrial Cognitive Facility",
                "Eidolon facility employees missing",
                "Eidolon facility sealed government",
                "Eidolon cognitive research disappearances",
                "\"Eidolon\" behavioural optimisation loop rumours",
                "Eidolon facility autonomous AI danger"
            };

            float[] delays = { 2f, 1.5f, 1.2f, 0.9f, 0.7f, 0.5f }; // accelerating

            for (int i = 0; i < searches.Count; i++)
            {
                AppendSearchResult(searches[i]);
                float d = i < delays.Length ? delays[i] : 0.4f;
                yield return new WaitForSeconds(d);
            }

            yield return new WaitForSeconds(1f);
        }

        private void AppendSearchResult(string query)
        {
            if (_searchResultsContainer == null || _searchResultPrefab == null) return;

            var go  = Instantiate(_searchResultPrefab, _searchResultsContainer);
            var txt = go.GetComponentInChildren<Text>();
            if (txt) txt.text = $"<color=blue>Gongle</color> — {query}";
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private void ShowOnly(GameObject panel)
        {
            if (_desktopPanel)    _desktopPanel.SetActive(panel == _desktopPanel);
            if (_emailPanel)      _emailPanel.SetActive(panel == _emailPanel);
            if (_browserPanel)    _browserPanel.SetActive(panel == _browserPanel);
            if (_laptopClosePanel)_laptopClosePanel.SetActive(panel == _laptopClosePanel);
        }

        private IEnumerator TypewriterReveal(Text target, string fullText, float charDelay)
        {
            target.text = "";
            foreach (char c in fullText)
            {
                target.text += c;
                if (c != ' ') PlayClip(_typingClip);
                yield return new WaitForSeconds(charDelay);
            }
        }

        private IEnumerator FadeOut(float duration)
        {
            if (_fadeOverlay == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                _fadeOverlay.alpha = Mathf.Lerp(0f, 1f, t / duration);
                t += Time.deltaTime;
                yield return null;
            }
            _fadeOverlay.alpha = 1f;
        }

        private void PlayClip(AudioClip clip)
        {
            if (_audioSource && clip) _audioSource.PlayOneShot(clip);
        }
    }

    // ─── Search Result Data ──────────────────────────────────────────────────

    [System.Serializable]
    public class SearchResultEntry
    {
        public string Query;
        public string Snippet;
        public bool   IsContradictory;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LORE LOG VIEWER
    // ─────────────────────────────────────────────────────────────────────────

    public class LoreLogViewer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup _panel;
        [SerializeField] private Text        _titleText;
        [SerializeField] private Text        _authorText;
        [SerializeField] private Text        _timestampText;
        [SerializeField] private Text        _bodyText;
        [SerializeField] private Text        _logTypeLabel;

        private bool _isOpen;

        public void Show(LoreLogData log)
        {
            if (log == null) return;
            _isOpen = true;

            if (_titleText)     _titleText.text     = log.Title;
            if (_authorText)    _authorText.text     = $"Author: {log.Author}";
            if (_timestampText) _timestampText.text  = log.Timestamp;
            if (_logTypeLabel)  _logTypeLabel.text   = log.LogType.ToString().ToUpper();
            if (_bodyText)      _bodyText.text        = log.IsRedacted ? log.RedactedVersion : log.Body;

            if (_panel) _panel.alpha = 1f;

            // Pause player input while reading (not full pause — ambient still plays)
            Time.timeScale = 0f;
        }

        public void Close()
        {
            _isOpen = false;
            if (_panel) _panel.alpha = 0f;
            Time.timeScale = 1f;
        }

        private void Update()
        {
            if (_isOpen && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab)))
                Close();
        }
    }
}
