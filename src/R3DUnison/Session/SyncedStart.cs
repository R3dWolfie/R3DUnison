using System;
using System.Collections.Generic;
using System.Linq;
using R3DUnison.Protocol;
using UnityEngine;

namespace R3DUnison.Session
{
    /// <summary>
    /// Synced level starts ("lobby mode"). The room owner launching a level pulls every
    /// member into it: each client's scrConductor.StartMusic is deferred by a Harmony
    /// prefix, readiness is collected, then a GO releases the deferred StartMusic on
    /// everyone within the same instant — the game's own "Get Ready" countdown does the
    /// rest, so all runs begin on the same beat.
    ///
    /// V1 scope: official levels only, owner-initiated. GO skew equals one-way network
    /// latency (tens of ms between friends); proper clock offset estimation can come
    /// later if it's ever audible.
    /// </summary>
    public static class SyncedStart
    {
        private enum Phase { Idle, HostCollecting, ClientLoading, ClientArmed, Countdown }

        private const string OfficialPrefix = "official:";
        private const float RetryWindowSeconds = 5f;

        private static Phase _phase = Phase.Idle;
        private static bool _isInitiator;
        private static string _key, _display;
        private static scrConductor _conductor;
        private static Action _onComplete, _onSongScheduled;
        private static bool _passthrough;
        private static readonly HashSet<ulong> _peersReady = new HashSet<ulong>();
        private static float _deadline, _fireAt;
        private static string _lastSyncedKey;

        public static bool Active => _phase != Phase.Idle;
        public static string StatusLine { get; private set; }

        /// <summary>Harmony prefix decision point. Returns true to let StartMusic run.</summary>
        public static bool OnStartMusic(scrConductor conductor, Action onComplete, Action onSongScheduled)
        {
            if (_passthrough) return true;
            var rm = RoomManager.Instance;
            if (rm == null || !rm.SteamReady || !rm.InRoom || rm.Members.Count < 2 || !Main.Settings.SyncedStarts) return true;
            var presence = Game.LevelTracker.TryDetect();
            if (presence == null || presence.IsCustom) return true; // custom-level sync: later milestone
            // Quick retry of the level we just synced runs free — only fresh entries sync.
            if (presence.Key == _lastSyncedKey && Time.realtimeSinceStartup - Game.LevelTracker.LastExitRealtime < RetryWindowSeconds)
            {
                return true;
            }

            if (_phase == Phase.ClientLoading && presence.Key == _key)
            {
                Defer(conductor, onComplete, onSongScheduled);
                _phase = Phase.ClientArmed;
                StatusLine = "SYNCED START · waiting for host…";
                rm.SendAll(MessageType.Ready, new LevelReadyMsg { Key = _key });
                Main.Log($"[sync] armed at gate for {_key}");
                return false;
            }

            if (!rm.Lobby.IsOwner) return true; // non-host solo play runs free (v1)

            _isInitiator = true;
            _phase = Phase.HostCollecting;
            _key = presence.Key;
            _display = presence.Display;
            Defer(conductor, onComplete, onSongScheduled);
            _peersReady.Clear();
            _deadline = Time.realtimeSinceStartup + 20f;
            rm.SendAll(MessageType.StartLevel, new StartLevelMsg { Key = _key, Display = _display, Checkpoint = GCS.checkpointNum });
            StatusLine = "SYNCED START · waiting for players…";
            Main.Log($"[sync] host gating {_key}, StartLevel broadcast");
            return false;
        }

        private static void Defer(scrConductor conductor, Action onComplete, Action onSongScheduled)
        {
            _conductor = conductor;
            _onComplete = onComplete;
            _onSongScheduled = onSongScheduled;
        }

        public static void Tick()
        {
            var rm = RoomManager.Instance;
            if (rm == null) return;
            switch (_phase)
            {
                case Phase.HostCollecting:
                {
                    var others = rm.Members.Where(m => !m.IsSelf).Select(m => m.Id).ToList();
                    int ready = others.Count(_peersReady.Contains);
                    StatusLine = $"SYNCED START · {ready}/{others.Count} players ready";
                    if ((others.Count > 0 && ready == others.Count) || Time.realtimeSinceStartup > _deadline)
                    {
                        rm.SendAll(MessageType.CountdownStart, new CountdownMsg { DelayMs = 3000 });
                        _fireAt = Time.realtimeSinceStartup + 3f;
                        _phase = Phase.Countdown;
                        Main.Log($"[sync] GO sent ({ready}/{others.Count} ready)");
                    }
                    break;
                }
                case Phase.ClientLoading:
                    if (Time.realtimeSinceStartup > _deadline) Reset("level load timed out");
                    break;
                case Phase.Countdown:
                {
                    float left = _fireAt - Time.realtimeSinceStartup;
                    StatusLine = $"SYNCED START · {Mathf.Max(0f, left):0.0}";
                    if (left <= 0f) Fire();
                    break;
                }
            }
        }

        private static void Fire()
        {
            var conductor = _conductor;
            var onComplete = _onComplete;
            var onSongScheduled = _onSongScheduled;
            string key = _key;
            Reset(null);
            _lastSyncedKey = key;
            if (conductor != null) // Unity-null: scene may have unloaded underneath us
            {
                try
                {
                    _passthrough = true;
                    conductor.StartMusic(onComplete, onSongScheduled);
                }
                finally
                {
                    _passthrough = false;
                }
                Main.Log("[sync] StartMusic released");
            }
        }

        public static void OnStartLevel(ulong from, StartLevelMsg msg)
        {
            var rm = RoomManager.Instance;
            if (rm == null || !rm.InRoom || _isInitiator || msg?.Key == null) return;
            if (!msg.Key.StartsWith(OfficialPrefix))
            {
                StatusLine = "Host started a custom level — sync for customs isn't supported yet.";
                return;
            }
            var current = Game.LevelTracker.TryDetect();
            if (current != null && current.Key == msg.Key)
            {
                // Already sitting in that level (unsynced): report ready, skip the gate.
                _isInitiator = false;
                _key = msg.Key;
                _phase = Phase.ClientArmed;
                rm.SendAll(MessageType.Ready, new LevelReadyMsg { Key = msg.Key });
                return;
            }
            string scene = msg.Key.Substring(OfficialPrefix.Length);
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                StatusLine = $"Can't load level '{msg.Display}'";
                Main.Log($"[sync] scene '{scene}' not loadable");
                return;
            }
            _isInitiator = false;
            _key = msg.Key;
            _display = msg.Display;
            _phase = Phase.ClientLoading;
            _deadline = Time.realtimeSinceStartup + 30f;
            StatusLine = $"SYNCED START · loading {msg.Display}…";
            try
            {
                GCS.checkpointNum = msg.Checkpoint;
                GCS.sceneToLoad = scene;
                ADOBase.loader.LoadScene(scene);
                Main.Log($"[sync] pulled into {scene} (checkpoint {msg.Checkpoint})");
            }
            catch (Exception e)
            {
                Reset("level load failed: " + e.Message);
            }
        }

        public static void OnPeerReady(ulong from, LevelReadyMsg msg)
        {
            if (_isInitiator && _phase == Phase.HostCollecting && msg?.Key == _key)
            {
                _peersReady.Add(from);
            }
        }

        public static void OnGo(ulong from, CountdownMsg msg)
        {
            if (_isInitiator || msg == null) return;
            if (_phase == Phase.ClientArmed || _phase == Phase.ClientLoading)
            {
                _fireAt = Time.realtimeSinceStartup + msg.DelayMs / 1000f;
                _phase = Phase.Countdown;
            }
        }

        public static void OnAbort(ulong from)
        {
            if (_isInitiator) return;
            if (_conductor != null) Fire(); // release the gate so nobody sits in silence
            else Reset("host aborted the synced start");
        }

        public static void OnLocalLevelExited()
        {
            if (_isInitiator && Active)
            {
                RoomManager.Instance?.SendAll(MessageType.SyncAbort, new SyncAbortMsg());
                Reset("host left the level");
            }
            else if (!_isInitiator && Active && _phase != Phase.ClientLoading)
            {
                // Our gated conductor died with the scene
                Reset(null);
            }
        }

        /// <summary>Leaving the room / mod shutdown: never leave a level gated in silence.</summary>
        public static void ResetAll()
        {
            if (!_isInitiator && _conductor != null) Fire();
            else Reset(null);
        }

        private static void Reset(string reason)
        {
            _phase = Phase.Idle;
            _isInitiator = false;
            _conductor = null;
            _onComplete = null;
            _onSongScheduled = null;
            _peersReady.Clear();
            StatusLine = null;
            if (reason != null) Main.Log($"[sync] reset: {reason}");
        }
    }
}
