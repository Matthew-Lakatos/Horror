using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Actors
{
    using Core;
    using AI;

    /// <summary>
    /// Big E – The Apex Watcher.
    /// Registers all Big E actions into the shared UtilityBrain.
    /// Internal variables: Attention, Confidence, Hunger, Familiarity, PresenceCooldown.
    /// Philosophy: 70% observe / 20% manipulate / 10% attack.
    /// </summary>
    [RequireComponent(typeof(EnemyActor))]
    public class BigEController : MonoBehaviour, IPacingObserver
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Big E Variables")]
        [SerializeField] private float _hungerIncreaseRate      = 0.01f; // per second uninteracted
        [SerializeField] private float _hungerDecreaseOnSighting = 0.15f;
        [SerializeField] private float _familiarityGainRate     = 0.005f;
        [SerializeField] private float _attentionDecayRate      = 0.02f;
        [SerializeField] private float _withdrawCooldown        = 90f;
        [SerializeField] private float _chanceOfChasePerTick    = 0.05f; // when hunger high

        // ── Internal variables (all 0–1) ───────────────────────────────────────
        public float Attention    { get; private set; } = 0f;
        public float Confidence   { get; private set; } = 0f;
        public float Hunger       { get; private set; } = 0f;
        public float Familiarity  { get; private set; } = 0f;

        private float _withdrawEndTime = 0f;
        private bool  _isWithdrawn     = false;

        private EnemyActor _actor;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake() => _actor = GetComponent<EnemyActor>();

        private void Start()
        {
            GameDirector.Instance?.AddObserver(this);
        }

        private void OnDestroy()
        {
            GameDirector.Instance?.RemoveObserver(this);
        }

        // Called by EnemyActor.Start via SendMessage
        private void OnRegisterActions()
        {
            _actor.RegisterAction(new BigE_ObserveAction(this, _actor));
            _actor.RegisterAction(new BigE_ShadowAction(this, _actor));
            _actor.RegisterAction(new BigE_InterceptAction(this, _actor));
            _actor.RegisterAction(new BigE_InspectRoomAction(this, _actor));
            _actor.RegisterAction(new BigE_MotionlessStandAction(this, _actor));
            _actor.RegisterAction(new BigE_ChaseAction(this, _actor));
            _actor.RegisterAction(new BigE_WithdrawAction(this, _actor));
            _actor.RegisterAction(new BigE_PatrolAction(this, _actor));
        }

        private void Update()
        {
            if (!_actor.IsAlive) return;

            UpdateVariables();
            PushVariablesToBlackboard();
            UpdateWithdrawState();
        }

        // ── Variable evolution ─────────────────────────────────────────────────
        private void UpdateVariables()
        {
            float dt = Time.deltaTime;

            // Attention rises with sensor confidence, decays otherwise
            float targetAttention = _actor.Sensor.ConfidenceScore;
            Attention = Mathf.MoveTowards(Attention, targetAttention, _attentionDecayRate * dt * 10f);

            // Confidence mirrors sensor
            Confidence = _actor.Sensor.ConfidenceScore;

            // Hunger rises passively, drops on sightings (Big E "fed" by interaction)
            if (_actor.Sensor.HasDetected)
                Hunger = Mathf.Max(0, Hunger - _hungerDecreaseOnSighting * dt * 5f);
            else
                Hunger = Mathf.Min(1f, Hunger + _hungerIncreaseRate * dt);

            // Familiarity grows when player is in known rooms
            if (_actor.Sensor.HasDetected)
                Familiarity = Mathf.Min(1f, Familiarity + _familiarityGainRate * dt);

            // Hunger effects: bold intercepts when hungry
            if (Hunger > 0.7f && !_isWithdrawn)
            {
                float phase = (GameDirector.Instance?.GetTensionFloat() ?? 0.5f);
                if (UnityEngine.Random.value < _chanceOfChasePerTick * Hunger * phase * dt)
                    TriggerLethalAttempt();
            }
        }

        private void PushVariablesToBlackboard()
        {
            var bb = _actor.BB;
            bb.Set(Blackboard.Keys.AttentionLevel,   Attention);
            bb.Set(Blackboard.Keys.HungerLevel,      Hunger);
            bb.Set(Blackboard.Keys.FamiliarityLevel, Familiarity);
            bb.Set(Blackboard.Keys.PlayerConfidence, Confidence);
        }

        private void UpdateWithdrawState()
        {
            if (_isWithdrawn && Time.time >= _withdrawEndTime)
            {
                _isWithdrawn = false;
                _actor.SetState(EnemyState.Patrolling);
            }
        }

        // ── External events ────────────────────────────────────────────────────
        public void OnMajorEncounterCompleted()
        {
            // Withdraw after a chase or near-kill
            _isWithdrawn    = true;
            _withdrawEndTime = Time.time + _withdrawCooldown;
            Hunger           = Mathf.Max(0f, Hunger - 0.5f);
            _actor.SetState(EnemyState.Retreating);
        }

        private void TriggerLethalAttempt()
        {
            var fairness = FairnessValidator.Instance;
            var player   = FindObjectOfType<PlayerController>();
            if (player == null || fairness == null) return;

            var request = new DangerousEventRequest
            {
                EventName        = "BigE_LethalAttempt",
                Severity         = EventSeverity.Lethal,
                HadWarning       = Attention > 0.5f,        // player had observation time
                RiskWasInferable = Familiarity > 0.4f,      // they know the route is watched
                CounterplayExists = true,                   // always: run, hide, decoy
                RoomId           = WorldStateManager.Instance?.PlayerCurrentRoomId,
                PlayerStressLevel = PerceptionManager.Instance?.StressLevel ?? 0f,
                SourceEntityId   = "BigE"
            };

            var report = fairness.Validate(request);
            if (!report.WasSuppressed)
            {
                _actor.Brain.ForceAction(
                    new BigE_ChaseAction(this, _actor), _actor.BB);
                GameDirector.Instance?.RegisterMajorEncounter();
            }
        }

        // ── IPacingObserver ────────────────────────────────────────────────────
        public void OnTensionChanged(TensionState prev, TensionState next)
        {
            if (next == TensionState.Recovery)
                Hunger = Mathf.Max(0f, Hunger - 0.1f);
        }

        // ── Debug ──────────────────────────────────────────────────────────────
        public bool IsWithdrawn => _isWithdrawn;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  BIG E ACTIONS
    // ─────────────────────────────────────────────────────────────────────────

    // ── Observe: stand still and watch ────────────────────────────────────────
    public class BigE_ObserveAction : IAIAction
    {
        private readonly BigEController _bigE;
        private readonly EnemyActor     _actor;
        private float    _endTime;

        public string ActionName => "BigE_Observe";
        public BigE_ObserveAction(BigEController c, EnemyActor a) { _bigE = c; _actor = a; }

        public float ScoreAction(Blackboard bb)
        {
            // Favoured when can see player but hunger is low
            float attention = bb.Get<float>(Blackboard.Keys.AttentionLevel);
            float hunger    = bb.Get<float>(Blackboard.Keys.HungerLevel);
            float score     = attention * 0.7f - hunger * 0.2f;
            return score * _actor.Profile.ObserveWeight;
        }

        public void Execute(Blackboard bb)
        {
            _actor.Movement.Stop();
            _actor.SetState(EnemyState.Watching);
            _actor.Presence.ShowPresence(8f, _actor.Profile.PresenceCooldown);
            _endTime = Time.time + Random.Range(4f, 12f);
        }

        public bool IsComplete(Blackboard bb) => Time.time >= _endTime;
        public void Interrupt() { }
    }

    // ── Shadow: follow in parallel room ────────────────────────────────────────
    public class BigE_ShadowAction : IAIAction
    {
        private readonly BigEController _bigE;
        private readonly EnemyActor     _actor;
        private float    _endTime;

        public string ActionName => "BigE_Shadow";
        public BigE_ShadowAction(BigEController c, EnemyActor a) { _bigE = c; _actor = a; }

        public float ScoreAction(Blackboard bb)
        {
            float attention = bb.Get<float>(Blackboard.Keys.AttentionLevel);
            float hunger    = bb.Get<float>(Blackboard.Keys.HungerLevel);
            // Shadow when moderately interested but not hungry
            return attention * 0.5f + hunger * 0.3f - 0.1f;
        }

        public void Execute(Blackboard bb)
        {
            _actor.SetState(EnemyState.Stalking);
            // Move to room adjacent to player room
            var playerRoom = WorldStateManager.Instance?.PlayerCurrentRoomId;
            if (playerRoom != null)
            {
                var graph = World.FacilityGraphManager.Instance?.Graph;
                var node  = graph?.GetNode(playerRoom);
                if (node != null)
                {
                    var exits = node.GetOpenExits();
                    if (exits.Count > 0)
                    {
                        string target = exits[Random.Range(0, exits.Count)];
                        _actor.Movement.MoveTo(target);
                    }
                }
            }
            _endTime = Time.time + Random.Range(15f, 30f);
        }

        public bool IsComplete(Blackboard bb) => Time.time >= _endTime;
        public void Interrupt() => _actor.Movement.Stop();
    }

    // ── Intercept: move to block likely route ──────────────────────────────────
    public class BigE_InterceptAction : IAIAction
    {
        private readonly BigEController _bigE;
        private readonly EnemyActor     _actor;
        private float    _endTime;

        public string ActionName => "BigE_Intercept";
        public BigE_InterceptAction(BigEController c, EnemyActor a) { _bigE = c; _actor = a; }

        public float ScoreAction(Blackboard bb)
        {
            float familiarity = bb.Get<float>(Blackboard.Keys.FamiliarityLevel);
            float hunger      = bb.Get<float>(Blackboard.Keys.HungerLevel);
            return familiarity * 0.6f + hunger * 0.4f;
        }

        public void Execute(Blackboard bb)
        {
            _actor.SetState(EnemyState.Stalking);

            // Find the player's most common route and intercept
            var topRoutes = WorldStateManager.Instance?.GetTopRoutes(1);
            if (topRoutes != null && topRoutes.Count > 0)
            {
                var parts = topRoutes[0].Split('>');
                if (parts.Length == 2)
                    _actor.Movement.MoveTo(parts[1]);  // move to destination of route
            }
            _endTime = Time.time + 25f;
        }

        public bool IsComplete(Blackboard bb) => Time.time >= _endTime || _actor.Movement.HasReachedDestination;
        public void Interrupt() => _actor.Movement.Stop();
    }

    // ── Inspect Room: enter and look around slowly ────────────────────────────
    public class BigE_InspectRoomAction : IAIAction
    {
        private readonly BigEController _bigE;
        private readonly EnemyActor     _actor;
        private float    _endTime;

        public string ActionName => "BigE_InspectRoom";
        public BigE_InspectRoomAction(BigEController c, EnemyActor a) { _bigE = c; _actor = a; }

        public float ScoreAction(Blackboard bb)
        {
            float attention   = bb.Get<float>(Blackboard.Keys.AttentionLevel);
            float familiarity = bb.Get<float>(Blackboard.Keys.FamiliarityLevel);
            // Mid-game escalation
            float phaseBonus = GameDirector.Instance?.CurrentPhase == GamePhase.MidGame ? 0.2f : 0f;
            return attention * 0.4f + familiarity * 0.3f + phaseBonus;
        }

        public void Execute(Blackboard bb)
        {
            _actor.SetState(EnemyState.Investigating);
            var playerRoom = bb.Get<string>(Blackboard.Keys.PlayerLastKnownRoom);
            if (playerRoom != null)
                _actor.Movement.MoveTo(playerRoom);
            _endTime = Time.time + Random.Range(10f, 20f);
        }

        public bool IsComplete(Blackboard bb) => Time.time >= _endTime;
        public void Interrupt() => _actor.Movement.Stop();
    }

    // ── Stand Motionless: maximum dread, zero action ──────────────────────────
    public class BigE_MotionlessStandAction : IAIAction
    {
        private readonly BigEController _bigE;
        private readonly EnemyActor     _actor;
        private float    _endTime;

        public string ActionName => "BigE_MotionlessStand";
        public BigE_MotionlessStandAction(BigEController c, EnemyActor a) { _bigE = c; _actor = a; }

        public float ScoreAction(Blackboard bb)
        {
            // Use when visible to player, presence cooldown ready
            float confidence = bb.Get<float>(Blackboard.Keys.PlayerConfidence);
            bool  ready      = _actor.Presence.CooldownReady;
            return ready && confidence > 0.7f ? 0.65f : 0f;
        }

        public void Execute(Blackboard bb)
        {
            _actor.Movement.Stop();
            _actor.SetState(EnemyState.Watching);
            _actor.Presence.ShowPresence(15f, _actor.Profile.PresenceCooldown);
            _endTime = Time.time + Random.Range(6f, 18f);
        }

        public bool IsComplete(Blackboard bb) => Time.time >= _endTime;
        public void Interrupt() { }
    }

    // ── Chase: rare relentless intelligent pursuit ────────────────────────────
    public class BigE_ChaseAction : IAIAction
    {
        private readonly BigEController _bigE;
        private readonly EnemyActor     _actor;
        private bool     _complete;

        public string ActionName => "BigE_Chase";
        public BigE_ChaseAction(BigEController c, EnemyActor a) { _bigE = c; _actor = a; }

        public float ScoreAction(Blackboard bb)
        {
            float hunger = bb.Get<float>(Blackboard.Keys.HungerLevel);
            return hunger > 0.85f ? 0.95f : 0f;
        }

        public void Execute(Blackboard bb)
        {
            _actor.SetState(EnemyState.Chasing);
            _actor.Movement.SetSpeed(_actor.Profile.ChaseSpeed);
            _complete = false;
            AudioManager.Instance?.PlayChaseMusic(_actor.Profile.ChaseSoundId);
        }

        public bool IsComplete(Blackboard bb)
        {
            // Chase ends if player lost for 30s or caught
            float confidence = bb.Get<float>(Blackboard.Keys.PlayerConfidence);
            if (confidence < 0.05f)
            {
                _bigE.OnMajorEncounterCompleted();
                return true;
            }
            return _complete;
        }

        public void Interrupt()
        {
            _actor.Movement.SetSpeed(_actor.Profile.BaseSpeed);
            _complete = true;
        }
    }

    // ── Withdraw: retreat after major encounter ────────────────────────────────
    public class BigE_WithdrawAction : IAIAction
    {
        private readonly BigEController _bigE;
        private readonly EnemyActor     _actor;
        private float    _endTime;

        public string ActionName => "BigE_Withdraw";
        public BigE_WithdrawAction(BigEController c, EnemyActor a) { _bigE = c; _actor = a; }

        public float ScoreAction(Blackboard bb)
            => _bigE.IsWithdrawn ? 1.5f : 0f;   // override everything when withdrawing

        public void Execute(Blackboard bb)
        {
            _actor.SetState(EnemyState.Retreating);
            _actor.Movement.SetSpeed(_actor.Profile.StalkSpeed);
            _endTime = Time.time + 30f;
        }

        public bool IsComplete(Blackboard bb) => !_bigE.IsWithdrawn;
        public void Interrupt() { }
    }

    // ── Patrol: fallback idle behaviour ───────────────────────────────────────
    public class BigE_PatrolAction : IAIAction
    {
        private readonly BigEController _bigE;
        private readonly EnemyActor     _actor;
        private float    _endTime;

        public string ActionName => "BigE_Patrol";
        public BigE_PatrolAction(BigEController c, EnemyActor a) { _bigE = c; _actor = a; }

        public float ScoreAction(Blackboard bb) => 0.1f;   // lowest priority, always fallback

        public void Execute(Blackboard bb)
        {
            _actor.SetState(EnemyState.Patrolling);
            _actor.Movement.SetSpeed(_actor.Profile.StalkSpeed);
            // Pick a known player room from familiarity to drift towards
            string room = WorldStateManager.Instance?.GetMostVisitedRoom();
            if (room != null) _actor.Movement.MoveTo(room);
            _endTime = Time.time + 20f;
        }

        public bool IsComplete(Blackboard bb) => Time.time >= _endTime;
        public void Interrupt() => _actor.Movement.Stop();
    }
}
