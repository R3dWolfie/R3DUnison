using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>Key of the last level the room started together (null once forced to re-sync).</summary>
        public static string LastSyncedKey => _lastSyncedKey;

        /// <summary>Make the next StartMusic of this level re-run the full sync dance (death-sync restarts).</summary>
        public static void ForceResync() => _lastSyncedKey = null;

        /// <summary>Host's custom-level folder for the current sync (null for official levels).</summary>
        internal static string HostLevelDir { get; private set; }

        internal static bool IsHostingKey(string key) => _isInitiator && _phase == Phase.HostCollecting && key == _key;

        /// <summary>A level download is progressing — don't time the room start out under it.</summary>
        internal static void NotifyTransferActivity()
        {
            if (_phase == Phase.HostCollecting)
            {
                _deadline = Mathf.Max(_deadline, Time.realtimeSinceStartup + 30f);
            }
        }

        /// <summary>Peer refused the level download — stop waiting for them.</summary>
        internal static void PeerDeclined(ulong peer)
        {
            if (_isInitiator) _peersReady.Add(peer); // counts as resolved, they just sit out
        }

        private static bool _autoSpectating;
        private static bool _weSetAuto;
        private static bool _autoPrev;

        /// <summary>Watching a roommate's level play itself via the game's autoplay.</summary>
        public static bool AutoSpectating => _autoSpectating;

        /// <summary>
        /// Load the level a roommate is playing and watch it play out via autoplay — the
        /// game renders the map in motion, plays the music, and drives the camera itself
        /// (far more robust than gating a frozen, black-screened level). Exiting the level
        /// or the host starting a real round turns autoplay back off.
        /// </summary>
        public static void SpectateInto(MemberState target)
        {
            var rm = RoomManager.Instance;
            string key = target?.StatsKey;
            if (rm == null || !rm.InRoom || key == null || key.StartsWith("menu:") || Active || _autoSpectating) return;
            try
            {
                if (ADOBase.isLevelEditor)
                {
                    StatusLine = "Leave the editor first — spectating replaces the current scene.";
                    return;
                }
            }
            catch
            {
            }
            string tail = key.Substring(key.IndexOf(':') + 1);
            try
            {
                bool official = key.StartsWith(OfficialPrefix);
                string customPath = null;
                bool internalWorld = false;
                if (official)
                {
                    try { internalWorld = scrController.IsWorldAndLevelInternalLevel(tail); } catch { }
                    if (!internalWorld && !Application.CanStreamedLevelBeLoaded(tail))
                    {
                        StatusLine = $"Can't load '{tail}' — missing world/DLC?";
                        return;
                    }
                }
                else
                {
                    customPath = Game.CustomLevels.Resolve(tail, null);
                    if (customPath == null)
                    {
                        StatusLine = "You don't have that level — ask the host to start it (auto-download kicks in).";
                        return;
                    }
                }

                EnableAutoplay();
                _autoSpectating = true;
                GCS.checkpointNum = 0;
                if (official)
                {
                    GCS.internalLevelName = internalWorld ? tail : null;
                    string scene = internalWorld ? "scnGame" : tail;
                    GCS.sceneToLoad = scene;
                    ADOBase.loader.LoadScene(scene);
                }
                else
                {
                    var controller = scrController.instance;
                    if (controller != null)
                    {
                        controller.LoadCustomLevel(customPath, tail);
                    }
                    else
                    {
                        GCS.sceneToLoad = "scnGame";
                        GCS.customLevelPaths = new[] { customPath };
                        GCS.customLevelIndex = 0;
                        GCS.loadCustomFromBundle = false;
                        GCS.customLevelId = tail;
                        ADOBase.loader.LoadScene("scnGame");
                    }
                }
                StatusLine = "SPECTATING (autoplay) — exit via the pause menu";
                Main.Log($"[spectate] autoplay into {key}");
            }
            catch (System.Exception e)
            {
                StopAutoSpectate();
                StatusLine = $"Spectate failed: {e.Message}";
            }
        }

        private static void EnableAutoplay()
        {
            try
            {
                _autoPrev = RDC.auto;
                RDC.auto = true;
                _weSetAuto = true;
            }
            catch
            {
                _weSetAuto = false;
            }
        }

        /// <summary>Turn our autoplay spectate off, restoring the player's own autoplay setting.</summary>
        public static void StopAutoSpectate()
        {
            if (_weSetAuto)
            {
                try { RDC.auto = _autoPrev; } catch { }
                _weSetAuto = false;
            }
            _autoSpectating = false;
        }

        // --- host-authoritative round speed ---

        private static string _roundSpeedKey;
        private static string _expectRestartKey;
        private static float _expectRestartUntil;

        /// <summary>Speed the current synced round runs at (from the initiator's StartLevel).</summary>
        public static float RoundSpeed { get; private set; }

        private static void SetRoundSpeed(string key, float speed)
        {
            _roundSpeedKey = key;
            RoundSpeed = speed <= 0.05f ? 1f : speed;
        }

        /// <summary>A room-wide restart is incoming: gate our reload until the host's GO.</summary>
        public static void ExpectRestart(string key)
        {
            _expectRestartKey = key;
            _expectRestartUntil = Time.realtimeSinceStartup + 15f;
        }

        /// <summary>The speed that should govern right now: the round's snapshot when one is live, else the lobby setting.</summary>
        public static float SpeedForNow(string currentKey)
        {
            if (RoundSpeed > 0f && (Active || (currentKey != null && currentKey == _roundSpeedKey)))
            {
                return RoundSpeed;
            }
            var rm = RoomManager.Instance;
            return rm?.Lobby != null && rm.InRoom ? rm.Lobby.SpeedMultiplier : 1f;
        }

        /// <summary>Harmony prefix decision point. Returns true to let StartMusic run.</summary>
        public static bool OnStartMusic(scrConductor conductor, Action onComplete, Action onSongScheduled)
        {
            if (_passthrough) return true;
            if (_autoSpectating) return true; // autoplay spectate drives itself — never gate it
            var rm = RoomManager.Instance;
            if (rm == null || !rm.SteamReady || !rm.InRoom) return true;
            var presence = Game.LevelTracker.TryDetect();
            if (presence == null) return true;
            // Room speed: customs get it at load via GCS.currentSpeedTrial (kept asserted by
            // RoomManager while in a room); official levels ignore that var, so multiply the
            // song pitch here — before StartMusic schedules — for every in-room run.
            if (!presence.IsCustom) ApplyRoomSpeed(conductor, presence.Key);
            if (rm.Members.Count < 2 || !Main.Settings.SyncedStarts) return true;
            // Quick retry of the level we just synced runs free — only fresh entries sync.
            if (presence.Key == _lastSyncedKey && Time.realtimeSinceStartup - Game.LevelTracker.LastExitRealtime < RetryWindowSeconds)
            {
                return true;
            }

            // The game may call StartMusic again while we're holding — keep holding the latest.
            if ((_phase == Phase.ClientArmed || _phase == Phase.HostCollecting) && presence.Key == _key)
            {
                Defer(conductor, onComplete, onSongScheduled);
                return false;
            }

            if (_phase == Phase.ClientLoading && presence.Key == _key)
            {
                Defer(conductor, onComplete, onSongScheduled);
                _phase = Phase.ClientArmed;
                _deadline = Time.realtimeSinceStartup + 45f;
                StatusLine = "SYNCED START · waiting for host…";
                rm.SendAll(MessageType.Ready, new LevelReadyMsg { Key = _key });
                Main.Log($"[sync] armed at gate for {_key}");
                return false;
            }

            // Reload after a room-wide restart (death-sync wipe / speed change): don't race
            // the host — gate here and wait for the fresh GO.
            if (!rm.Lobby.IsOwner && _expectRestartKey != null && presence.Key == _expectRestartKey
                && Time.realtimeSinceStartup < _expectRestartUntil)
            {
                _expectRestartKey = null;
                _isInitiator = false;
                _key = presence.Key;
                Defer(conductor, onComplete, onSongScheduled);
                _phase = Phase.ClientArmed;
                _deadline = Time.realtimeSinceStartup + 30f;
                StatusLine = "SYNCED START · waiting for host…";
                rm.SendAll(MessageType.Ready, new LevelReadyMsg { Key = _key });
                Main.Log("[sync] restart-gate armed");
                return false;
            }

            if (!rm.Lobby.IsOwner) return true; // non-host solo play runs free (v1)

            string folder = null, file = null;
            HostLevelDir = null;
            if (presence.IsCustom)
            {
                try
                {
                    string levelPath = ADOBase.levelPath;
                    folder = Path.GetFileName(Path.GetDirectoryName(levelPath));
                    file = Path.GetFileName(levelPath);
                    HostLevelDir = Path.GetDirectoryName(levelPath);
                }
                catch
                {
                    return true; // can't identify the custom level — run free
                }
            }

            _isInitiator = true;
            _phase = Phase.HostCollecting;
            _key = presence.Key;
            _display = presence.Display;
            // Editor-hosted rounds run at ×1 for everyone — the editor playtest ignores
            // the room speed (it has its own playback-speed system), so applying it to
            // the pulled-in players would desync them from the host.
            bool editorRound = false;
            try
            {
                editorRound = ADOBase.isLevelEditor;
            }
            catch
            {
            }
            SetRoundSpeed(_key, editorRound ? 1f : rm.Lobby.SpeedMultiplier);
            Defer(conductor, onComplete, onSongScheduled);
            _peersReady.Clear();
            _deadline = Time.realtimeSinceStartup + 20f;
            rm.SendAll(MessageType.StartLevel, new StartLevelMsg
            {
                Key = _key,
                Display = _display,
                Checkpoint = GCS.checkpointNum,
                Folder = folder,
                File = file,
                Speed = RoundSpeed,
            });
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
            // If we're a gated client and the round's host has left the room (Steam silently
            // transfers lobby ownership to us), no GO is ever coming — release the gate so the
            // level plays and we can exit normally instead of sitting frozen.
            if (!_isInitiator && _conductor != null && rm.Lobby != null && rm.Lobby.IsOwner
                && (_phase == Phase.ClientArmed || _phase == Phase.ClientLoading || _phase == Phase.Countdown))
            {
                Main.Log("[sync] host left while gated — releasing");
                Fire();
                return;
            }
            switch (_phase)
            {
                case Phase.HostCollecting:
                {
                    var others = rm.Members.Where(m => !m.IsSelf).Select(m => m.Id).ToList();
                    int ready = others.Count(_peersReady.Contains);
                    StatusLine = $"SYNCED START · {ready}/{others.Count} players ready{LevelTransfer.HostStatusSuffix}";
                    bool everyoneReady = others.Count > 0 && ready == others.Count;
                    bool timedOut = Time.realtimeSinceStartup > _deadline && !LevelTransfer.HostBusy;
                    if (everyoneReady || timedOut)
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
                case Phase.ClientArmed:
                    // Host never said GO (started without us / vanished): don't sit gated forever.
                    if (Time.realtimeSinceStartup > _deadline) Fire();
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
            var rm = RoomManager.Instance;
            if (key != null && rm != null && rm.InRoom && rm.Members.Count >= 2)
            {
                Scoreboard.OnRoundStart(key);
            }
        }

        private static int _speedAppliedTo;

        // Multiply the song pitch once per conductor instance (fresh per scene load, so
        // it naturally covers retries without double-applying on re-deferred calls).
        private static void ApplyRoomSpeed(scrConductor conductor, string levelKey)
        {
            var rm = RoomManager.Instance;
            if (conductor == null || rm?.Lobby == null || !rm.InRoom) return;
            float speed = SpeedForNow(levelKey);
            if (Mathf.Abs(speed - 1f) < 0.005f) return;
            int id = conductor.GetInstanceID();
            if (_speedAppliedTo == id) return;
            _speedAppliedTo = id;
            try
            {
                conductor.song.pitch *= speed;
                Main.Log($"[room] chart speed ×{speed:0.0##} applied");
            }
            catch
            {
                // song not ready — skip rather than crash the gate
            }
        }

        public static void OnStartLevel(ulong from, StartLevelMsg msg)
        {
            var rm = RoomManager.Instance;
            if (rm == null || !rm.InRoom || _isInitiator || msg?.Key == null) return;
            SetRoundSpeed(msg.Key, msg.Speed); // host-authoritative for this round
            // If we were autoplay-spectating, the round pulls us in for real — turn autoplay
            // off and always reload (don't take the "already here" shortcut, which would leave
            // us watching an autoplay instead of playing).
            bool wasAutoSpectating = _autoSpectating;
            StopAutoSpectate();
            var current = Game.LevelTracker.TryDetect();
            if (!wasAutoSpectating && current != null && current.Key == msg.Key)
            {
                // Already sitting in that level (unsynced): report ready, skip the gate.
                // Must set a real deadline — otherwise Tick's ClientArmed fires immediately
                // off a stale _deadline and this client races ahead of the coordinated GO.
                _isInitiator = false;
                _key = msg.Key;
                _phase = Phase.ClientArmed;
                _deadline = Time.realtimeSinceStartup + 45f;
                rm.SendAll(MessageType.Ready, new LevelReadyMsg { Key = msg.Key });
                return;
            }

            // Never yank someone out of an editor session — unsaved maps die that way.
            try
            {
                if (ADOBase.isLevelEditor)
                {
                    StatusLine = $"Host started '{msg.Display}' — you're in the editor, join from the menu when ready.";
                    return;
                }
            }
            catch
            {
            }

            bool official = msg.Key.StartsWith(OfficialPrefix);
            string scene = null, customPath = null;
            bool internalWorld = false;
            if (official)
            {
                scene = msg.Key.Substring(OfficialPrefix.Length);
                // Bonus/DLC worlds (XI-X etc.) aren't scenes — they run inside scnGame
                // via GCS.internalLevelName, exactly how the game's own portals load them.
                try
                {
                    internalWorld = scrController.IsWorldAndLevelInternalLevel(scene);
                }
                catch
                {
                    internalWorld = false; // unknown world key on this install
                }
                if (!internalWorld && !Application.CanStreamedLevelBeLoaded(scene))
                {
                    StatusLine = $"Can't load level '{msg.Display}' — missing world/DLC?";
                    Main.Log($"[sync] scene '{scene}' not loadable and not an internal world");
                    return;
                }
            }
            else
            {
                customPath = Game.CustomLevels.Resolve(msg.Folder, msg.File);
                if (customPath == null)
                {
                    // Ask the host to send us the level; the prompt takes it from here.
                    Main.Log($"[sync] custom level '{msg.Folder}' not found locally — requesting from host");
                    LevelTransfer.BeginPeerFlow(from, msg);
                    return;
                }
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
                if (official)
                {
                    GCS.internalLevelName = internalWorld ? scene : null;
                    string sceneToLoad = internalWorld ? "scnGame" : scene;
                    GCS.sceneToLoad = sceneToLoad;
                    ADOBase.loader.LoadScene(sceneToLoad);
                    Main.Log($"[sync] pulled into {scene} (internal={internalWorld}, checkpoint {msg.Checkpoint})");
                }
                else
                {
                    string levelId = msg.Key.Substring("custom:".Length);
                    var controller = scrController.instance;
                    if (controller != null)
                    {
                        controller.LoadCustomLevel(customPath, levelId);
                    }
                    else
                    {
                        GCS.sceneToLoad = "scnGame";
                        GCS.customLevelPaths = new[] { customPath };
                        GCS.customLevelIndex = 0;
                        GCS.loadCustomFromBundle = false;
                        GCS.customLevelId = levelId;
                        ADOBase.loader.LoadScene("scnGame");
                    }
                    Main.Log($"[sync] pulled into custom '{msg.Display}' ({customPath})");
                }
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
            LevelTransfer.PeerReset(); // pending download prompt is moot now
            if (_conductor != null) Fire(); // release the gate so nobody sits in silence
            else Reset("host aborted the synced start");
        }

        public static void OnLocalLevelExited()
        {
            StopAutoSpectate(); // left the level → stop autoplay, restore the player's setting
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
            StopAutoSpectate();
            if (!_isInitiator && _conductor != null) Fire();
            else Reset(null);
            RoundSpeed = 0f;
            _roundSpeedKey = null;
            _expectRestartKey = null;
        }

        private static void Reset(string reason)
        {
            _phase = Phase.Idle;
            _isInitiator = false;
            _conductor = null;
            _onComplete = null;
            _onSongScheduled = null;
            _peersReady.Clear();
            HostLevelDir = null;
            StatusLine = null;
            LevelTransfer.HostReset();
            if (reason != null) Main.Log($"[sync] reset: {reason}");
        }
    }
}
