using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace R3DUnison.Core
{
    /// <summary>
    /// In-mod auto-update. UMM's in-game "Available update" arrow is only a
    /// notification (its UI draws the icon as a plain Box; installing is left to the
    /// Windows-only installer app), so we do the whole loop ourselves: check
    /// Repository.json on launch, offer an UPDATE button, download the release zip and
    /// swap our own files. The loaded DLL is renamed to .old first (allowed even while
    /// memory-mapped on Windows) and leftovers are cleaned on next start.
    /// </summary>
    public static class SelfUpdater
    {
        private const string RepoJsonUrl = "https://raw.githubusercontent.com/R3dWolfie/R3DUnison/main/Repository.json";
        private const string FallbackZipUrl = "https://github.com/R3dWolfie/R3DUnison/releases/latest/download/R3DUnison.zip";

        /// <summary>Non-null when a newer version is available and not yet applied.</summary>
        public static string AvailableVersion { get; private set; }
        public static string State { get; private set; }
        public static bool Busy { get; private set; }
        public static bool Applied { get; private set; }

        private static string _downloadUrl = FallbackZipUrl;

#pragma warning disable 649 // populated by Newtonsoft via reflection
        private class RepoFile
        {
            public Release[] Releases;
        }

        private class Release
        {
            public string Id;
            public string Version;
            public string DownloadUrl;
        }
#pragma warning restore 649

        private static string ModDir => Main.Mod.Path;

        public static void OnStartup()
        {
            CleanupOldFiles();
            MainThreadDispatcher.Run(CheckRoutine());
        }

        private static void CleanupOldFiles()
        {
            try
            {
                foreach (var file in Directory.GetFiles(ModDir, "*.old"))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // still locked or gone — next launch gets another shot
            }
        }

        private static IEnumerator CheckRoutine()
        {
            using (var request = UnityWebRequest.Get(RepoJsonUrl))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Main.Log($"[update] check failed: {request.error}");
                    yield break;
                }
                try
                {
                    var repo = JsonConvert.DeserializeObject<RepoFile>(request.downloadHandler.text);
                    var release = repo?.Releases?.FirstOrDefault(r => r.Id == Main.Mod.Info.Id);
                    if (release?.Version != null && new Version(release.Version) > new Version(Main.Mod.Info.Version))
                    {
                        AvailableVersion = release.Version;
                        if (!string.IsNullOrEmpty(release.DownloadUrl)) _downloadUrl = release.DownloadUrl;
                        Main.Log($"[update] v{release.Version} available (installed v{Main.Mod.Info.Version})");
                    }
                }
                catch (Exception e)
                {
                    Main.Log($"[update] repository parse failed: {e.Message}");
                }
            }
        }

        public static void Apply()
        {
            if (Busy || Applied || AvailableVersion == null) return;
            MainThreadDispatcher.Run(ApplyRoutine());
        }

        private static IEnumerator ApplyRoutine()
        {
            Busy = true;
            State = $"Downloading v{AvailableVersion}…";
            using (var request = UnityWebRequest.Get(_downloadUrl))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    State = $"Download failed: {request.error}";
                    Main.LogError($"[update] download failed: {request.error}");
                }
                else
                {
                    try
                    {
                        Install(request.downloadHandler.data);
                        Applied = true;
                        State = $"Updated to v{AvailableVersion} — restart the game to finish.";
                        Main.Log($"[update] v{AvailableVersion} installed, restart pending");
                    }
                    catch (Exception e)
                    {
                        State = $"Update failed: {e.Message}";
                        Main.LogError($"[update] install failed: {e}");
                    }
                }
            }
            Busy = false;
        }

        private static void Install(byte[] zipBytes)
        {
            using (var stream = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Length == 0) continue; // directory entries
                    // zip layout: R3DUnison/<file> — flatten into our mod folder
                    string target = Path.Combine(ModDir, Path.GetFileName(entry.FullName));
                    if (File.Exists(target))
                    {
                        string old = target + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(target, old);
                    }
                    using (var source = entry.Open())
                    using (var output = File.Create(target))
                    {
                        source.CopyTo(output);
                    }
                }
            }
        }
    }
}
