using System;
using System.IO;
using System.Reflection;
using _Scripts.Helpers;
using _Scripts.Managers;
using BranteAccess.Module.Screens;
using UnityEngine;

namespace BranteAccess.Module.Game
{
    /// <summary>
    /// Player-made backups of the current save slot. The game has no manual save: it rewrites
    /// Autosave.dat on every scene and keeps only chapter-start saves, so one regretted choice
    /// can cost a whole chapter. Ctrl+S copies the live slot folder to
    /// persistentDataPath/BranteAccessBackups/&lt;slot id&gt;/&lt;timestamp&gt;. Ctrl+L on the main menu
    /// copies the newest backup back over its slot, after stashing the slot's current files
    /// under _before-restore so the restore itself can be undone by hand; the game's own
    /// Continue then loads it, because the load window re-reads every slot from disk when it
    /// opens (LoadWindow -> SaveLoadManager.GetAllSaves). Restore needs a second press within
    /// a few seconds, so a stray Ctrl+L cannot overwrite progress.
    /// </summary>
    internal static class SaveBackups
    {
        private const string RootName = "BranteAccessBackups";
        private const string BeforeRestore = "_before-restore";
        private const string InfoFile = "backup-info.txt";
        private const float ConfirmSeconds = 5f;

        private static readonly FieldInfo CurrentSaveField = typeof(SaveLoadManager)
            .GetField("_currentSaveFile", BindingFlags.NonPublic | BindingFlags.Instance);

        // Module statics reset on hot reload; losing a pending confirmation then is harmless.
        private static string _armedPath;
        private static float _armedUntil = -1f;

        private static string Root => Path.Combine(Application.persistentDataPath, RootName);
        private static string SavesDir => Path.Combine(Application.persistentDataPath, "Saves");

        /// <summary>Ctrl+S: copy the slot that is being played.</summary>
        public static void Backup()
        {
            try
            {
                // A plain reference test, not Unity's ==: the game keeps using this manager's
                // managed object after its native side is gone, so Unity's null check says null.
                object manager = SaveLoadManager.Instance;
                var helper = manager == null || CurrentSaveField == null
                    ? null
                    : CurrentSaveField.GetValue(manager) as SaveLoadHelper;
                var id = helper?.SlotInfo?.Id;
                var src = string.IsNullOrEmpty(id) ? null : Path.Combine(SavesDir, id);
                if (src == null || !Directory.Exists(src)
                    || !File.Exists(Path.Combine(src, "Autosave.dat")))
                {
                    Say(Loc.T("backup.none"));
                    return;
                }
                var dest = Path.Combine(Path.Combine(Root, id), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                CopyFiles(src, dest);
                File.WriteAllText(Path.Combine(dest, InfoFile),
                    "chapter = " + helper.Chapter + "\nyear = " + helper.Year + "\n");
                Say(Loc.T("backup.saved", new { chapter = helper.Chapter, year = helper.Year }));
            }
            catch (Exception e)
            {
                Mod.Warn("[backup] save failed: " + e.Message);
                Say(Loc.T("backup.failed"));
            }
        }

        /// <summary>Ctrl+L: on the main menu, restore the newest backup (press twice).</summary>
        public static void Restore()
        {
            try
            {
                if (ScreenManager.Current == null || ScreenManager.Current.Key != "mainmenu")
                {
                    Say(Loc.T("backup.restore.mainmenu"));
                    return;
                }
                var newest = Newest();
                if (newest == null)
                {
                    Say(Loc.T("backup.restore.none"));
                    return;
                }
                ReadInfo(newest, out var chapter, out var year);
                if (_armedPath != newest || Time.unscaledTime > _armedUntil)
                {
                    _armedPath = newest;
                    _armedUntil = Time.unscaledTime + ConfirmSeconds;
                    Say(Loc.T("backup.restore.confirm", new { chapter, year }));
                    return;
                }
                _armedPath = null;
                var id = Path.GetFileName(Path.GetDirectoryName(newest));
                var slot = Path.Combine(SavesDir, id);
                if (Directory.Exists(slot))
                    CopyFiles(slot, Path.Combine(Path.Combine(Path.Combine(Root, id), BeforeRestore),
                        DateTime.Now.ToString("yyyyMMdd-HHmmss")));
                Directory.CreateDirectory(slot);
                foreach (var file in Directory.GetFiles(newest))
                    if (Path.GetFileName(file) != InfoFile)
                        File.Copy(file, Path.Combine(slot, Path.GetFileName(file)), overwrite: true);
                Say(Loc.T("backup.restored", new { chapter, year }));
            }
            catch (Exception e)
            {
                Mod.Warn("[backup] restore failed: " + e.Message);
                Say(Loc.T("backup.failed"));
            }
        }

        // The newest timestamp folder across all slots; folder names sort chronologically.
        private static string Newest()
        {
            if (!Directory.Exists(Root)) return null;
            string best = null, bestName = null;
            foreach (var slotDir in Directory.GetDirectories(Root))
                foreach (var dir in Directory.GetDirectories(slotDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name == BeforeRestore) continue;
                    if (bestName == null || string.CompareOrdinal(name, bestName) > 0)
                    {
                        best = dir;
                        bestName = name;
                    }
                }
            return best;
        }

        private static void ReadInfo(string dir, out string chapter, out string year)
        {
            chapter = "?";
            year = "?";
            var path = Path.Combine(dir, InfoFile);
            if (!File.Exists(path)) return;
            foreach (var raw in File.ReadAllLines(path))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                var key = raw.Substring(0, eq).Trim();
                var value = raw.Substring(eq + 1).Trim();
                if (key == "chapter") chapter = value;
                else if (key == "year") year = value;
            }
        }

        private static void CopyFiles(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }

        private static void Say(string text) => Mod.Speech.Output(text, interrupt: true);
    }
}
