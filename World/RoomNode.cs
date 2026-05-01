using System.Collections.Generic;
using UnityEngine;
using Eidolon.Core;

namespace Eidolon.World
{
    /// <summary>
    /// Represents a single room node in the facility graph.
    /// Tracks all runtime state flags used by FacilityBrain, AI, and the player.
    /// </summary>
    public class RoomNode : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string _roomId;
        [SerializeField] private string _displayLabel;
        [SerializeField] private RoomType _roomType;
        [SerializeField] private BuildingType _building;

        [Header("Connectivity")]
        [SerializeField] private List<string> _exits = new List<string>();

        [Header("Initial State")]
        [SerializeField] private bool _initiallyLit    = true;
        [SerializeField] private bool _initiallyFogged = false;
        [SerializeField] private bool _initiallyHeated = false;
        [SerializeField] private bool _initiallyLocked = false;
        [SerializeField] private bool _isSafeRoom      = false;

        [Header("Geometry")]
        [SerializeField] private List<GameObject> _geometryVariants = new List<GameObject>();
        [SerializeField] private List<GameObject> _hazards          = new List<GameObject>();

        [Header("Audio")]
        [SerializeField] private AudioProfile _soundProfile;

        // ─── Runtime State ──────────────────────────────────────────────────

        public string     RoomId   { get => _roomId;   private set => _roomId   = value; }
        public RoomType   RoomType { get => _roomType; private set => _roomType = value; }
        public BuildingType Building => _building;
        public List<string> Exits  => _exits;

        // Mutable flags
        public bool IsLocked        { get; set; }
        public bool IsLit           { get; set; }
        public bool IsFogged        { get; set; }
        public bool IsHeated        { get; set; }
        public bool IsSafeRoom      => _isSafeRoom;
        public bool IsLabelCorrupted{ get; set; }
        public bool HasGeometryVariants => _geometryVariants != null && _geometryVariants.Count > 1;

        private int _currentGeometryVariant;
        private string _corruptedLabel;
        public string DisplayLabel => IsLabelCorrupted ? _corruptedLabel : _displayLabel;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            IsLocked         = _initiallyLocked;
            IsLit            = _initiallyLit;
            IsFogged         = _initiallyFogged;
            IsHeated         = _initiallyHeated;
            IsLabelCorrupted = false;
            ApplyGeometryVariant(0);
        }

        // ─── Geometry Variants ───────────────────────────────────────────────

        public void CycleGeometryVariant()
        {
            if (!HasGeometryVariants) return;
            _currentGeometryVariant = (_currentGeometryVariant + 1) % _geometryVariants.Count;
            ApplyGeometryVariant(_currentGeometryVariant);
        }

        private void ApplyGeometryVariant(int index)
        {
            for (int i = 0; i < _geometryVariants.Count; i++)
                if (_geometryVariants[i] != null)
                    _geometryVariants[i].SetActive(i == index);
        }

        // ─── Label Corruption ────────────────────────────────────────────────

        /// <summary>
        /// Assigns a plausible but wrong label (e.g. nearby room name) for dread effect.
        /// </summary>
        public void SetCorruptedLabel(string falseLabel)
        {
            _corruptedLabel  = falseLabel;
            IsLabelCorrupted = true;
        }

        // ─── Hazard Activation ───────────────────────────────────────────────

        public void ActivateHazards(bool active)
        {
            foreach (var h in _hazards)
                if (h != null) h.SetActive(active);
        }

        // ─── Save/Load ───────────────────────────────────────────────────────

        public RoomSaveData ExportState() => new RoomSaveData
        {
            RoomId           = _roomId,
            IsLocked         = IsLocked,
            IsLit            = IsLit,
            IsFogged         = IsFogged,
            IsHeated         = IsHeated,
            IsLabelCorrupted = IsLabelCorrupted,
            CorruptedLabel   = _corruptedLabel,
            GeometryVariant  = _currentGeometryVariant
        };

        public void ImportState(RoomSaveData data)
        {
            IsLocked         = data.IsLocked;
            IsLit            = data.IsLit;
            IsFogged         = data.IsFogged;
            IsHeated         = data.IsHeated;
            IsLabelCorrupted = data.IsLabelCorrupted;
            _corruptedLabel  = data.CorruptedLabel;
            ApplyGeometryVariant(data.GeometryVariant);
            _currentGeometryVariant = data.GeometryVariant;
        }
    }

    // ─── Enumerations & Data ─────────────────────────────────────────────────

    public enum RoomType
    {
        Corridor, ProcessingFloor, ControlRoom, StorageVault, ResearchLab,
        ObservationDeck, MedBay, GeneratorRoom, MaintenanceTunnel, Skybridge,
        IndustrialYard, Archive, AdminOffice, BreachPoint, SafeRoom
    }

    [System.Serializable]
    public class RoomSaveData
    {
        public string RoomId;
        public bool   IsLocked;
        public bool   IsLit;
        public bool   IsFogged;
        public bool   IsHeated;
        public bool   IsLabelCorrupted;
        public string CorruptedLabel;
        public int    GeometryVariant;
    }
}
