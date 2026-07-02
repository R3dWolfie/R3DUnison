using System.Collections.Generic;
using System.Linq;
using R3DUnison.Session;
using UnityEngine;

namespace R3DUnison.Game
{
    /// <summary>
    /// Real planet visuals for ghosts: clones the local player's planet GameObject
    /// (sprites, trails, tail particles — including whatever scale/effects the level
    /// applies), disables all behaviour scripts so it's pure visuals, tints it to the
    /// remote player's colors and drives the pair along the ghost path every frame.
    /// </summary>
    public class GhostPlanets : MonoBehaviour
    {
        /// <summary>Members currently rendered with real planets (the overlay skips its circles for these).</summary>
        public static readonly HashSet<ulong> RealPlanets = new HashSet<ulong>();

        private class Pair
        {
            public GameObject OnTile;
            public GameObject Orbiting;
        }

        private readonly Dictionary<ulong, Pair> _pairs = new Dictionary<ulong, Pair>();
        private GameObject _template;
        private string _levelKey;

        private void LateUpdate()
        {
            var rm = RoomManager.Instance;
            if (rm == null || !rm.InRoom || rm.Members.Count < 2)
            {
                DestroyAll();
                return;
            }

            // Two modes: in a level → position on the shared track; in the level-select menu
            // (a shared freeroam world) → position at each member's streamed planet coords.
            // Both render REAL cloned planets so the menu matches in-game.
            string myKey;
            bool menuMode;
            var mine = LevelTracker.Current;
            try
            {
                if (mine != null) { myKey = mine.Key; menuMode = false; }
                else if (ADOBase.isLevelSelect) { myKey = "menu:" + ADOBase.sceneName; menuMode = true; }
                else { DestroyAll(); return; }
            }
            catch
            {
                DestroyAll();
                return;
            }

            if (_levelKey != myKey)
            {
                DestroyAll(); // scene changed under us — template and pairs are stale
                _levelKey = myKey;
            }

            List<scrFloor> floors = null;
            try
            {
                if (!menuMode) floors = ADOBase.lm?.listFloors;
                if (_template == null)
                {
                    var source = scrController.instance?.chosenPlanet?.gameObject;
                    if (source != null)
                    {
                        _template = Instantiate(source);
                        foreach (var behaviour in _template.GetComponentsInChildren<Behaviour>(true))
                        {
                            behaviour.enabled = false; // visuals only — no scrPlanet logic, no animators
                        }
                        _template.SetActive(false);
                        _template.name = "R3DUnison.GhostPlanetTemplate";
                        DontDestroyOnLoad(_template);
                    }
                }
            }
            catch
            {
                return;
            }
            if (_template == null) return;
            if (!menuMode && (floors == null || floors.Count == 0)) return;

            var seen = new HashSet<ulong>();
            foreach (var member in rm.Members)
            {
                if (member.IsSelf || member.Dead || !member.HasFreshStats || member.StatsKey != myKey) continue;

                Vector3 stationary, orbit;
                bool color1OnTile;
                if (menuMode)
                {
                    // Idle planet pair orbiting the member's streamed menu position.
                    Vector3 center = new Vector3(member.PosX, member.PosY, 0f);
                    float ang = Time.time * 1.8f;
                    Vector3 off = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * 0.8f;
                    stationary = center + off;
                    orbit = center - off;
                    color1OnTile = true;
                }
                else
                {
                    if (!UI.GhostOverlay.TryGetGhostPosition(member, floors, out stationary, out orbit, out int seq)) continue;
                    // Which color sits ON the tile alternates each note (parity) — the real two
                    // planets keep their colors and take turns pivoting.
                    color1OnTile = (seq & 1) == 0;
                }

                seen.Add(member.Id);
                // `== null` also catches Unity-destroyed clones (e.g. after a same-key retry,
                // where the scene reloaded but our _levelKey didn't change) — respawn them.
                if (!_pairs.TryGetValue(member.Id, out var pair) || pair.OnTile == null || pair.Orbiting == null)
                {
                    DestroyPair(member.Id);
                    pair = new Pair
                    {
                        OnTile = SpawnGhost(member, first: true),
                        Orbiting = SpawnGhost(member, first: false),
                    };
                    _pairs[member.Id] = pair;
                    RealPlanets.Add(member.Id);
                }
                if (pair.OnTile == null || pair.Orbiting == null) continue;
                var tileGo = color1OnTile ? pair.OnTile : pair.Orbiting;
                var orbitGo = color1OnTile ? pair.Orbiting : pair.OnTile;
                // Slightly behind the local play plane so ghosts never cover your planets
                tileGo.transform.position = stationary + Vector3.forward * 2f;
                orbitGo.transform.position = orbit + Vector3.forward * 2f;
            }

            foreach (var id in _pairs.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                DestroyPair(id);
            }
        }

        private GameObject SpawnGhost(MemberState member, bool first)
        {
            var ghost = Instantiate(_template);
            ghost.name = $"R3DUnison.Ghost.{member.Name}.{(first ? "A" : "B")}";
            ghost.SetActive(true);
            Color color = member.HasColors
                ? (first ? member.Color1 : member.Color2)
                : (first ? UI.UnisonTheme.Green : Color.white);
            // The planet body + glow are recolored by the player's color; the FACE
            // (eyes/details) is its own color and must NOT be tinted, or the whole planet
            // reads as a solid blob of one color instead of a colored body with a face.
            var faceSprites = new HashSet<SpriteRenderer>();
            try
            {
                var pr = ghost.GetComponent<scrPlanet>()?.planetRenderer;
                if (pr != null)
                {
                    if (pr.faceSprite != null) faceSprites.Add(pr.faceSprite);
                    if (pr.faceDetails != null) faceSprites.Add(pr.faceDetails);
                    if (pr.samuraiSprite != null) faceSprites.Add(pr.samuraiSprite);
                }
            }
            catch
            {
                // planetRenderer not wired on the clone — fall back to tinting everything
            }
            foreach (var sprite in ghost.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (faceSprites.Contains(sprite)) continue;
                sprite.color = new Color(color.r, color.g, color.b, sprite.color.a * 0.8f);
            }
            foreach (var particles in ghost.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.startColor = new Color(color.r, color.g, color.b, 0.6f);
            }
            foreach (var trail in ghost.GetComponentsInChildren<TrailRenderer>(true))
            {
                trail.startColor = new Color(color.r, color.g, color.b, 0.6f);
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
                trail.Clear();
            }
            return ghost;
        }

        private void DestroyPair(ulong id)
        {
            if (!_pairs.TryGetValue(id, out var pair)) return;
            if (pair.OnTile != null) Destroy(pair.OnTile);
            if (pair.Orbiting != null) Destroy(pair.Orbiting);
            _pairs.Remove(id);
            RealPlanets.Remove(id);
        }

        private void DestroyAll()
        {
            foreach (var id in _pairs.Keys.ToList()) DestroyPair(id);
            if (_template != null)
            {
                Destroy(_template);
                _template = null;
            }
            _levelKey = null;
        }

        private void OnDestroy() => DestroyAll();
    }
}
