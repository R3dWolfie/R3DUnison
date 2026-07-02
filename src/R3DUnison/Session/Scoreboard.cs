using System.Collections.Generic;
using System.Linq;
using R3DUnison.Protocol;
using UnityEngine;

namespace R3DUnison.Session
{
    /// <summary>
    /// Round tracking + session win tally. A round opens on every synced GO; entries
    /// accumulate from the live stats everyone already streams. Free/death-sync rounds
    /// close 12s after the first finisher; elimination rounds close when every entrant
    /// has either finished or died once (die once = OUT, later attempts don't count).
    /// Ranking: finishers by accuracy, then survivors by how long they lasted.
    /// </summary>
    public static class Scoreboard
    {
        public class Entry
        {
            public ulong Id;
            public string Name;
            public bool Won;
            public float Acc;
            public float Progress;
            public int DeathOrder; // >0 = order of first death this round
            public bool Eliminated;
        }

        public static readonly Dictionary<ulong, int> Wins = new Dictionary<ulong, int>();
        public static List<Entry> LastRound { get; private set; }
        public static string LastRoundWinner { get; private set; }
        public static float LastRoundAt { get; private set; } = -1000f;
        public static string ActiveRoundKey { get; private set; }
        public static bool ElimRound { get; private set; }

        private static readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();
        private static float _closeAt = -1f;
        private static int _deaths;

        public static void OnRoundStart(string key)
        {
            var rm = RoomManager.Instance;
            ActiveRoundKey = key;
            ElimRound = rm?.Lobby != null && rm.Lobby.Mode == Transport.RoomMode.Elimination;
            _entries.Clear();
            _deaths = 0;
            _closeAt = -1f;
        }

        public static Entry GetEntry(ulong id) => _entries.TryGetValue(id, out var entry) ? entry : null;

        public static void NoteStats(MemberState member, string key)
        {
            if (ActiveRoundKey == null || key != ActiveRoundKey) return;
            if (!_entries.TryGetValue(member.Id, out var entry))
            {
                _entries[member.Id] = entry = new Entry { Id = member.Id, Name = member.Name };
            }
            if (entry.Won || entry.Eliminated) return;
            entry.Acc = member.Accuracy;
            if (member.Progress > entry.Progress) entry.Progress = member.Progress;
            if (member.Dead && entry.DeathOrder == 0)
            {
                entry.DeathOrder = ++_deaths;
                if (ElimRound) entry.Eliminated = true;
            }
        }

        public static void NoteWon(ulong id, string name, RunResultMsg result)
        {
            if (ActiveRoundKey == null || result?.Key != ActiveRoundKey) return;
            if (!_entries.TryGetValue(id, out var entry))
            {
                _entries[id] = entry = new Entry { Id = id, Name = name };
            }
            entry.Won = true;
            entry.Acc = result.Acc;
            entry.Progress = 1f;
            if (_closeAt < 0f) _closeAt = Time.realtimeSinceStartup + 12f;
        }

        public static void Tick()
        {
            if (ActiveRoundKey == null) return;
            bool elimDone = ElimRound && _entries.Count >= 2 && _entries.Values.All(e => e.Won || e.Eliminated);
            bool timerDone = _closeAt > 0f && Time.realtimeSinceStartup > _closeAt;
            if (elimDone || timerDone) CloseRound();
        }

        private static void CloseRound()
        {
            var ranked = _entries.Values
                .OrderByDescending(e => e.Won)
                .ThenByDescending(e => e.Won ? e.Acc : 0f)
                .ThenByDescending(e => ElimRound ? e.DeathOrder : 0) // died later = outlasted
                .ThenByDescending(e => e.Progress)
                .ThenByDescending(e => e.Acc)
                .ToList();
            if (ranked.Count >= 2)
            {
                var winner = ranked[0];
                Wins[winner.Id] = Wins.TryGetValue(winner.Id, out var count) ? count + 1 : 1;
                LastRoundWinner = winner.Name;
                LastRound = ranked;
                LastRoundAt = Time.realtimeSinceStartup;
                Main.Log($"[round] winner: {winner.Name} ({ranked.Count} players)");
            }
            ActiveRoundKey = null;
        }

        /// <summary>Round restarted/abandoned before anyone finished — no winner awarded.</summary>
        public static void AbandonRound() => ActiveRoundKey = null;

        public static void ResetSession()
        {
            Wins.Clear();
            LastRound = null;
            LastRoundWinner = null;
            ActiveRoundKey = null;
            LastRoundAt = -1000f;
        }
    }
}
