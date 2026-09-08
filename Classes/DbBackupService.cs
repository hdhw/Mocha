using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Mocha2023.Classes
{
    // Periodic snapshot of Data/DBs into Data/DBBackups.
    //
    // The tick is cheap and does nothing most of the time: it fingerprints the
    // database files and returns immediately unless something moved. LiteDB writes
    // every change through the -log.db companion before it is checkpointed into the
    // .db, so a new account, a room save, a cheer - anything that touches a
    // collection - shows up as a length or write-time change on one of those files.
    // That is the trigger; there is no per-feature bookkeeping to keep in sync.
    public static class DbBackupService
    {
        // How often the fingerprint is taken. A tick that finds no change costs a
        // directory listing, so this can be frequent without being expensive.
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

        // Snapshots kept before the oldest is pruned. A busy server can produce one
        // every interval, so this is the real disk ceiling: roughly
        // MaxSnapshots * (size of Data/DBs).
        private const int MaxSnapshots = 48;

        private static readonly object Sync = new();

        private static Timer? _timer;
        private static string? _lastFingerprint;

        public static string BackupRoot =>
            Path.Combine(Program.dataDir, "DBBackups");

        private static string SourceRoot =>
            Path.Combine(Program.dataDir, "DBs");

        public static void Start()
        {
            lock (Sync)
            {
                if (_timer != null)
                    return;

                // The state on disk at startup is already safe on disk - it is what a
                // backup would copy. Recording it now means an idle server that is
                // started and left alone writes nothing.
                _lastFingerprint = TryFingerprint();

                _timer = new Timer(
                    _ => Tick(),
                    null,
                    Interval,
                    Interval);
            }

            ServerLog.Info(
                $"DB backups armed: every {Interval.TotalMinutes:0} min when changed, " +
                $"keeping {MaxSnapshots} in Data/DBBackups.");
        }

        public static void Stop()
        {
            lock (Sync)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        // Run a backup regardless of whether anything changed. Used by the shutdown
        // path and the admin panel's manual button.
        public static string? BackupNow(string reason)
        {
            lock (Sync)
            {
                string? destination = CopySnapshot(reason);
                if (destination != null)
                    _lastFingerprint = TryFingerprint();

                return destination;
            }
        }

        public sealed class SnapshotInfo
        {
            public string Name { get; set; } = string.Empty;
            public DateTime CreatedUtc { get; set; }
            public int Files { get; set; }
            public long Bytes { get; set; }
        }

        public static int IntervalMinutes => (int)Interval.TotalMinutes;

        public static int RetainedSnapshots => MaxSnapshots;

        // Newest first. Used by the admin panel; the sizes are read off disk rather
        // than remembered, so a snapshot deleted by hand simply stops being listed.
        public static List<SnapshotInfo> ListSnapshots()
        {
            var results = new List<SnapshotInfo>();

            try
            {
                if (!Directory.Exists(BackupRoot))
                    return results;

                foreach (string path in Directory
                             .EnumerateDirectories(BackupRoot)
                             .Where(value => IsSnapshotName(Path.GetFileName(value)))
                             .OrderByDescending(value => Path.GetFileName(value), StringComparer.Ordinal))
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);

                    results.Add(new SnapshotInfo
                    {
                        Name = Path.GetFileName(path),
                        CreatedUtc = Directory.GetCreationTimeUtc(path),
                        Files = files.Length,
                        Bytes = files.Sum(file => new FileInfo(file).Length)
                    });
                }
            }
            catch (Exception ex)
            {
                ServerLog.Error($"DB backup list: {ex.Message}");
            }

            return results;
        }

        private static void Tick()
        {
            try
            {
                lock (Sync)
                {
                    string? fingerprint = TryFingerprint();

                    // Unreadable directory, or genuinely nothing new since the last
                    // snapshot. Either way there is nothing worth writing.
                    if (fingerprint == null || fingerprint == _lastFingerprint)
                        return;

                    if (CopySnapshot("scheduled") != null)
                        _lastFingerprint = fingerprint;
                }
            }
            catch (Exception ex)
            {
                ServerLog.Error($"DB backup tick: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Name, length and last-write time of every file under Data/DBs. Length and
        // write time together are what change on a LiteDB write; hashing the files
        // would be the same answer for several megabytes more work each tick.
        private static string? TryFingerprint()
        {
            try
            {
                if (!Directory.Exists(SourceRoot))
                    return null;

                var builder = new StringBuilder();

                foreach (string path in Directory
                             .EnumerateFiles(SourceRoot, "*", SearchOption.AllDirectories)
                             .OrderBy(value => value, StringComparer.Ordinal))
                {
                    var info = new FileInfo(path);
                    builder.Append(Path.GetRelativePath(SourceRoot, path))
                        .Append('|')
                        .Append(info.Length)
                        .Append('|')
                        .Append(info.LastWriteTimeUtc.Ticks)
                        .Append('\n');
                }

                return builder.ToString();
            }
            catch (Exception ex)
            {
                ServerLog.Error($"DB backup fingerprint: {ex.Message}");
                return null;
            }
        }

        private static string? CopySnapshot(string reason)
        {
            try
            {
                if (!Directory.Exists(SourceRoot))
                    return null;

                string stamp = DateTime.UtcNow.ToString(
                    "yyyy-MM-dd_HH-mm-ss",
                    CultureInfo.InvariantCulture);
                string destination = Path.Combine(BackupRoot, stamp);

                // A second snapshot inside the same second would land on the same
                // directory; make the name unique rather than merging the two.
                int suffix = 1;
                while (Directory.Exists(destination))
                    destination = Path.Combine(BackupRoot, $"{stamp}-{suffix++}");

                Directory.CreateDirectory(destination);

                int files = 0;
                long bytes = 0;

                foreach (string path in Directory.EnumerateFiles(
                             SourceRoot,
                             "*",
                             SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(SourceRoot, path);
                    string target = Path.Combine(destination, relative);

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                    // The databases are open and being written to, so the copy has to
                    // tolerate the writer's own handle. Both the .db and its -log.db
                    // are copied, which is the pair LiteDB needs to recover a snapshot
                    // taken between checkpoints.
                    using (FileStream source = new(
                               path,
                               FileMode.Open,
                               FileAccess.Read,
                               FileShare.ReadWrite | FileShare.Delete))
                    using (FileStream sink = new(
                               target,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None))
                    {
                        source.CopyTo(sink);
                        bytes += sink.Length;
                    }

                    files++;
                }

                Prune();

                ServerLog.Success(
                    $"DB backup ({reason}): {files} file(s), " +
                    $"{bytes / 1024 / 1024.0:0.0} MB -> Data/DBBackups/{Path.GetFileName(destination)}");

                return destination;
            }
            catch (Exception ex)
            {
                ServerLog.Error($"DB backup failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // Keep the newest MaxSnapshots directories this service created. Directories
        // that do not match the stamp format are left alone - Data/DBBackups is the
        // service's own, but an operator's hand-made folder should not be deleted by
        // a retention pass.
        private static void Prune()
        {
            try
            {
                if (!Directory.Exists(BackupRoot))
                    return;

                List<string> mine = Directory
                    .EnumerateDirectories(BackupRoot)
                    .Where(path => IsSnapshotName(Path.GetFileName(path)))
                    .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                    .ToList();

                foreach (string stale in mine.Skip(MaxSnapshots))
                {
                    Directory.Delete(stale, recursive: true);
                    ServerLog.Info($"DB backup pruned: {Path.GetFileName(stale)}");
                }
            }
            catch (Exception ex)
            {
                ServerLog.Error($"DB backup prune: {ex.Message}");
            }
        }

        // yyyy-MM-dd_HH-mm-ss, optionally with the -N collision suffix.
        private static bool IsSnapshotName(string name)
        {
            string stamp = name;
            int dash = name.LastIndexOf('-');

            if (dash == 19 && int.TryParse(name[(dash + 1)..], out _))
                stamp = name[..dash];

            return DateTime.TryParseExact(
                stamp,
                "yyyy-MM-dd_HH-mm-ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);
        }
    }
}
