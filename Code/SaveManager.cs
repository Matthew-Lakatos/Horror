using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace Eidolon.Save
{
    using Core;

    /// <summary>
    /// Coordinates save and load across all ISaveable systems.
    /// Uses binary serialisation to a persistent data path.
    /// Each ISaveable system registers itself; SaveManager queries CaptureState()
    /// and RestoreState() in a predictable order.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Save Settings")]
        [SerializeField] private string _saveFileName     = "eidolon_save.bin";
        [SerializeField] private bool   _autoSaveEnabled  = true;
        [SerializeField] private float  _autoSaveInterval = 120f;

        // ── State ──────────────────────────────────────────────────────────────
        private readonly List<ISaveable> _saveables = new();
        private float                    _autoSaveTimer;
        private string                   SavePath => Path.Combine(Application.persistentDataPath, _saveFileName);

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EvSaveRequested>(_ => Save());
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EvSaveRequested>(_ => Save());
        }

        private void Update()
        {
            if (!_autoSaveEnabled) return;
            _autoSaveTimer += Time.deltaTime;
            if (_autoSaveTimer >= _autoSaveInterval)
            {
                _autoSaveTimer = 0f;
                Save();
            }
        }

        // ── Registration ───────────────────────────────────────────────────────
        public void Register(ISaveable saveable)
        {
            if (!_saveables.Contains(saveable))
                _saveables.Add(saveable);
        }

        public void Unregister(ISaveable saveable)
            => _saveables.Remove(saveable);

        // ── Save ───────────────────────────────────────────────────────────────
        public void Save()
        {
            var bundle = new SaveBundle();
            bundle.Timestamp = DateTime.UtcNow.ToString("o");

            foreach (var s in _saveables)
            {
                try
                {
                    var state = s.CaptureState();
                    bundle.States[s.SaveKey] = state;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SaveManager] Failed to capture {s.SaveKey}: {ex}");
                }
            }

            try
            {
                var formatter = new BinaryFormatter();
#pragma warning disable SYSLIB0011
                using var stream = File.Open(SavePath, FileMode.Create);
                formatter.Serialize(stream, bundle);
#pragma warning restore SYSLIB0011
                Debug.Log($"[SaveManager] Saved to {SavePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Serialisation failed: {ex}");
            }
        }

        // ── Load ───────────────────────────────────────────────────────────────
        public bool Load()
        {
            if (!File.Exists(SavePath))
            {
                Debug.Log("[SaveManager] No save file found.");
                return false;
            }

            SaveBundle bundle;
            try
            {
                var formatter = new BinaryFormatter();
#pragma warning disable SYSLIB0011
                using var stream = File.Open(SavePath, FileMode.Open);
                bundle = (SaveBundle)formatter.Deserialize(stream);
#pragma warning restore SYSLIB0011
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Deserialisation failed: {ex}");
                return false;
            }

            foreach (var s in _saveables)
            {
                if (bundle.States.TryGetValue(s.SaveKey, out var state))
                {
                    try   { s.RestoreState(state); }
                    catch (Exception ex)
                    { Debug.LogError($"[SaveManager] Failed to restore {s.SaveKey}: {ex}"); }
                }
            }

            EventBus.Publish(new EvLoadCompleted());
            Debug.Log($"[SaveManager] Loaded save from {bundle.Timestamp}");
            return true;
        }

        // ── Delete ─────────────────────────────────────────────────────────────
        public void DeleteSave()
        {
            if (File.Exists(SavePath))
                File.Delete(SavePath);
        }

        public bool SaveExists() => File.Exists(SavePath);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SAVE BUNDLE
    // ─────────────────────────────────────────────────────────────────────────
    [Serializable]
    public class SaveBundle
    {
        public string                     Timestamp;
        public Dictionary<string, object> States = new();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  AUTO-REGISTER HELPER
    //  Attach to any MonoBehaviour that implements ISaveable to auto-register.
    // ─────────────────────────────────────────────────────────────────────────
    public class AutoRegisterSaveable : MonoBehaviour
    {
        private ISaveable _saveable;

        private void Awake()
        {
            _saveable = GetComponent<ISaveable>();
        }

        private void Start()
        {
            if (_saveable != null)
                SaveManager.Instance?.Register(_saveable);
        }

        private void OnDestroy()
        {
            if (_saveable != null)
                SaveManager.Instance?.Unregister(_saveable);
        }
    }
}
