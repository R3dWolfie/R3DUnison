using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace R3DUnison.Game
{
    /// <summary>
    /// Finds the local copy of a custom level from the folder/file hint a peer sends.
    /// Levels installed via CLS/TUF live under Documents/A Dance of Fire and Ice/
    /// (Worlds, Featured); Steam Workshop subscriptions live under
    /// steamapps/workshop/content/977950/. Folder names are stable across installs
    /// for TUF/Workshop content, which is what makes this hint-based match work.
    /// </summary>
    public static class CustomLevels
    {
        public static string Resolve(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            foreach (var root in Roots().Distinct())
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    string dir = Path.Combine(root, folder);
                    if (!Directory.Exists(dir))
                    {
                        dir = Directory.GetDirectories(root).FirstOrDefault(d =>
                            string.Equals(Path.GetFileName(d), folder, StringComparison.OrdinalIgnoreCase));
                    }
                    if (dir == null) continue;

                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string direct = Path.Combine(dir, fileName);
                        if (File.Exists(direct)) return direct;
                    }
                    string found = AdoPackageInstaller.FindLevelFile(dir).Value;
                    if (!string.IsNullOrEmpty(found) && File.Exists(found)) return found;
                    string any = Directory.GetFiles(dir, "*.adofai").FirstOrDefault();
                    if (any != null) return any;
                }
                catch
                {
                    // unreadable root — try the next one
                }
            }
            return null;
        }

        private static IEnumerable<string> Roots()
        {
            string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "A Dance of Fire and Ice");
            yield return Path.Combine(docs, "Worlds");
            yield return Path.Combine(docs, "Featured");
            if (!string.IsNullOrEmpty(scnCLS.localWorldsPath)) yield return scnCLS.localWorldsPath;

            string workshop = null;
            try
            {
                // <steamapps>/common/<game> → <steamapps>/workshop/content/977950
                string gameDir = Path.GetDirectoryName(UnityEngine.Application.dataPath);
                string steamapps = Path.GetDirectoryName(Path.GetDirectoryName(gameDir));
                workshop = Path.Combine(steamapps, "workshop", "content", "977950");
            }
            catch
            {
                // non-Steam layout
            }
            if (workshop != null) yield return workshop;
        }
    }
}
