using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using NightOwls.Models;

namespace NightOwls.Repositories;

public class LibraryRepository
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    public LibraryRepository(string databaseFilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databaseFilePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        Initialize();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS ScanLocations(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Path TEXT NOT NULL UNIQUE COLLATE NOCASE,
                DateAddedUtc TEXT NOT NULL,
                IsUnavailable INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS Movies(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Path TEXT NOT NULL UNIQUE COLLATE NOCASE,
                FileName TEXT NOT NULL,
                Title TEXT NOT NULL,
                Year INTEGER,
                Extension TEXT NOT NULL,
                FileSizeBytes INTEGER NOT NULL,
                LastModifiedUtc TEXT NOT NULL,
                DurationMs INTEGER NOT NULL DEFAULT 0,
                LocationId INTEGER REFERENCES ScanLocations(Id) ON DELETE SET NULL,
                DateAddedUtc TEXT NOT NULL,
                ThumbnailFile TEXT);
            CREATE INDEX IF NOT EXISTS IX_Movies_Title ON Movies(Title COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS IX_Movies_LocationId ON Movies(LocationId);
            CREATE TABLE IF NOT EXISTS Favorites(
                MovieId INTEGER PRIMARY KEY REFERENCES Movies(Id) ON DELETE CASCADE,
                DateAddedUtc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS WatchRecords(
                MovieId INTEGER PRIMARY KEY REFERENCES Movies(Id) ON DELETE CASCADE,
                LastWatchedUtc TEXT NOT NULL,
                PositionMs INTEGER NOT NULL,
                LengthMs INTEGER NOT NULL,
                Completed INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS AppSettings(
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }

    public static string DefaultDatabasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NightOwls", "nightowls.db");

    public List<ScanLocation> GetLocations()
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Path, DateAddedUtc, IsUnavailable FROM ScanLocations ORDER BY DateAddedUtc";
            using var r = cmd.ExecuteReader();
            var list = new List<ScanLocation>();
            while (r.Read())
            {
                list.Add(new ScanLocation
                {
                    Id = r.GetInt32(0),
                    Path = r.GetString(1),
                    DateAddedUtc = ParseDate(r.GetString(2)),
                    IsUnavailable = r.GetInt32(3) != 0
                });
            }
            return list;
        }
    }

    public ScanLocation AddLocation(string path)
    {
        path = NormalizePath(path);
        lock (_gate)
        {
            using var conn = Open();
            using (var exists = conn.CreateCommand())
            {
                exists.CommandText = "SELECT Id FROM ScanLocations WHERE Path = $p";
                exists.Parameters.AddWithValue("$p", path);
                if (exists.ExecuteScalar() is not null)
                    throw new InvalidOperationException("This location is already in your library.");
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO ScanLocations(Path, DateAddedUtc, IsUnavailable) VALUES($p, $d, 0); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$d", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            int id = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            return new ScanLocation { Id = id, Path = path, DateAddedUtc = DateTime.UtcNow };
        }
    }

    public void RemoveLocation(int id)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();
            using (var delMovies = conn.CreateCommand())
            {
                delMovies.Transaction = tx;
                delMovies.CommandText = "DELETE FROM Movies WHERE LocationId = $id";
                delMovies.Parameters.AddWithValue("$id", id);
                delMovies.ExecuteNonQuery();
            }
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM ScanLocations WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    public void SetLocationUnavailable(int id, bool unavailable)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ScanLocations SET IsUnavailable = $u WHERE Id = $id";
            cmd.Parameters.AddWithValue("$u", unavailable ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public List<Movie> GetAllMovies(bool includeUnavailableLocationFiles = true)
    {
        const string sql =
            """
            SELECT m.Id, m.Path, m.FileName, m.Title, m.Year, m.Extension, m.FileSizeBytes,
                   m.LastModifiedUtc, m.DurationMs, m.LocationId, m.DateAddedUtc, m.ThumbnailFile,
                   CASE WHEN f.MovieId IS NULL THEN 0 ELSE 1 END,
                   w.LastWatchedUtc, w.PositionMs, w.LengthMs, w.Completed,
                   l.IsUnavailable
            FROM Movies m
            LEFT JOIN Favorites f ON f.MovieId = m.Id
            LEFT JOIN WatchRecords w ON w.MovieId = m.Id
            LEFT JOIN ScanLocations l ON l.Id = m.LocationId
            """;

        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = includeUnavailableLocationFiles
                ? sql
                : sql + " WHERE COALESCE(l.IsUnavailable, 0) = 0";
            using var r = cmd.ExecuteReader();
            var list = new List<Movie>();
            while (r.Read()) list.Add(ReadMovie(r));
            return list;
        }
    }

    public Movie? GetMovieById(int id)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT m.Id, m.Path, m.FileName, m.Title, m.Year, m.Extension, m.FileSizeBytes,
                       m.LastModifiedUtc, m.DurationMs, m.LocationId, m.DateAddedUtc, m.ThumbnailFile,
                       CASE WHEN f.MovieId IS NULL THEN 0 ELSE 1 END,
                       w.LastWatchedUtc, w.PositionMs, w.LengthMs, w.Completed,
                       0
                FROM Movies m
                LEFT JOIN Favorites f ON f.MovieId = m.Id
                LEFT JOIN WatchRecords w ON w.MovieId = m.Id
                WHERE m.Id = $id
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadMovie(r) : null;
        }
    }

    public bool MovieExistsByPath(string path)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id FROM Movies WHERE Path = $p";
            cmd.Parameters.AddWithValue("$p", NormalizePath(path));
            return cmd.ExecuteScalar() is not null;
        }
    }

    private static Movie ReadMovie(SqliteDataReader r)
    {
        var movie = new Movie
        {
            Id = r.GetInt32(0),
            Path = r.GetString(1),
            FileName = r.GetString(2),
            Title = r.GetString(3),
            Year = r.IsDBNull(4) ? null : r.GetInt32(4),
            Extension = r.GetString(5),
            FileSizeBytes = r.GetInt64(6),
            LastModifiedUtc = ParseDate(r.GetString(7)),
            DurationMs = r.GetInt64(8),
            LocationId = r.IsDBNull(9) ? null : r.GetInt32(9),
            DateAddedUtc = ParseDate(r.GetString(10)),
            ThumbnailFile = r.IsDBNull(11) ? null : r.GetString(11),
            IsFavorite = r.GetInt32(12) != 0
        };
        if (!r.IsDBNull(13))
        {
            movie.Watch = new WatchRecord
            {
                MovieId = movie.Id,
                LastWatchedUtc = ParseDate(r.GetString(13)),
                PositionMs = r.GetInt64(14),
                LengthMs = r.GetInt64(15),
                Completed = r.GetInt32(16) != 0
            };
        }
        return movie;
    }

    public void InsertMovies(IEnumerable<Movie> movies)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                INSERT INTO Movies(Path, FileName, Title, Year, Extension, FileSizeBytes, LastModifiedUtc,
                                   DurationMs, LocationId, DateAddedUtc, ThumbnailFile)
                VALUES($path, $fn, $title, $year, $ext, $size, $mod, $dur, $loc, $added, $thumb);
                SELECT last_insert_rowid();
                """;
            var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
            var pFn = cmd.Parameters.Add("$fn", SqliteType.Text);
            var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
            var pYear = cmd.Parameters.Add("$year", SqliteType.Integer);
            var pExt = cmd.Parameters.Add("$ext", SqliteType.Text);
            var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
            var pMod = cmd.Parameters.Add("$mod", SqliteType.Text);
            var pDur = cmd.Parameters.Add("$dur", SqliteType.Integer);
            var pLoc = cmd.Parameters.Add("$loc", SqliteType.Integer);
            var pAdded = cmd.Parameters.Add("$added", SqliteType.Text);
            var pThumb = cmd.Parameters.Add("$thumb", SqliteType.Text);

            foreach (var m in movies)
            {
                pPath.Value = NormalizePath(m.Path);
                pFn.Value = m.FileName;
                pTitle.Value = m.Title;
                pYear.Value = m.Year.HasValue ? m.Year.Value : DBNull.Value;
                pExt.Value = m.Extension;
                pSize.Value = m.FileSizeBytes;
                pMod.Value = m.LastModifiedUtc.ToString("o", CultureInfo.InvariantCulture);
                pDur.Value = m.DurationMs;
                pLoc.Value = m.LocationId.HasValue ? m.LocationId.Value : DBNull.Value;
                pAdded.Value = m.DateAddedUtc.ToString("o", CultureInfo.InvariantCulture);
                pThumb.Value = m.ThumbnailFile is null ? DBNull.Value : m.ThumbnailFile;

                try
                {
                    m.Id = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                }
                catch (SqliteException)
                {
                }
            }
            tx.Commit();
        }
    }

    public void UpdateMovieMetadata(Movie m)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE Movies SET FileSizeBytes=$s, LastModifiedUtc=$mod, DurationMs=$dur, ThumbnailFile=$thumb, Title=$title, Year=$year WHERE Id=$id";
            cmd.Parameters.AddWithValue("$s", m.FileSizeBytes);
            cmd.Parameters.AddWithValue("$mod", m.LastModifiedUtc.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$dur", m.DurationMs);
            cmd.Parameters.AddWithValue("$thumb", m.ThumbnailFile is null ? DBNull.Value : m.ThumbnailFile);
            cmd.Parameters.AddWithValue("$title", m.Title);
            cmd.Parameters.AddWithValue("$year", m.Year.HasValue ? m.Year.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$id", m.Id);
            cmd.ExecuteNonQuery();
        }
    }

    public void SetThumbnailFile(int movieId, string thumbnailFile)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Movies SET ThumbnailFile=$t WHERE Id=$id";
            cmd.Parameters.AddWithValue("$t", thumbnailFile);
            cmd.Parameters.AddWithValue("$id", movieId);
            cmd.ExecuteNonQuery();
        }
    }

    public void SetDurationIfEmpty(int movieId, long durationMs)
    {
        if (durationMs <= 0) return;
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Movies SET DurationMs=$d WHERE Id=$id AND DurationMs <= 0";
            cmd.Parameters.AddWithValue("$d", durationMs);
            cmd.Parameters.AddWithValue("$id", movieId);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteMoviesByPaths(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM Movies WHERE Path = $p";
            var p = cmd.Parameters.Add("$p", SqliteType.Text);
            foreach (var path in paths)
            {
                p.Value = NormalizePath(path);
                try { cmd.ExecuteNonQuery(); } catch (SqliteException) { }
            }
            tx.Commit();
        }
    }

    public void SetFavorite(int movieId, bool favorite)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            if (favorite)
            {
                cmd.CommandText = "INSERT OR IGNORE INTO Favorites(MovieId, DateAddedUtc) VALUES($id, $d)";
                cmd.Parameters.AddWithValue("$id", movieId);
                cmd.Parameters.AddWithValue("$d", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            }
            else
            {
                cmd.CommandText = "DELETE FROM Favorites WHERE MovieId = $id";
                cmd.Parameters.AddWithValue("$id", movieId);
            }
            cmd.ExecuteNonQuery();
        }
    }

    public void SaveWatchPosition(int movieId, long positionMs, long lengthMs, bool completed)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO WatchRecords(MovieId, LastWatchedUtc, PositionMs, LengthMs, Completed)
                VALUES($id, $d, $pos, $len, $done)
                ON CONFLICT(MovieId) DO UPDATE SET
                    LastWatchedUtc=excluded.LastWatchedUtc,
                    PositionMs=excluded.PositionMs,
                    LengthMs=excluded.LengthMs,
                    Completed=excluded.Completed
                """;
            cmd.Parameters.AddWithValue("$id", movieId);
            cmd.Parameters.AddWithValue("$d", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$pos", Math.Max(0, positionMs));
            cmd.Parameters.AddWithValue("$len", Math.Max(0, lengthMs));
            cmd.Parameters.AddWithValue("$done", completed ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public WatchRecord? GetWatchRecord(int movieId)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MovieId, LastWatchedUtc, PositionMs, LengthMs, Completed FROM WatchRecords WHERE MovieId=$id";
            cmd.Parameters.AddWithValue("$id", movieId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new WatchRecord
            {
                MovieId = r.GetInt32(0),
                LastWatchedUtc = ParseDate(r.GetString(1)),
                PositionMs = r.GetInt64(2),
                LengthMs = r.GetInt64(3),
                Completed = r.GetInt32(4) != 0
            };
        }
    }

    public List<WatchRecord> GetWatchedRecords()
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MovieId, LastWatchedUtc, PositionMs, LengthMs, Completed FROM WatchRecords ORDER BY LastWatchedUtc DESC";
            using var r = cmd.ExecuteReader();
            var list = new List<WatchRecord>();
            while (r.Read())
            {
                list.Add(new WatchRecord
                {
                    MovieId = r.GetInt32(0),
                    LastWatchedUtc = ParseDate(r.GetString(1)),
                    PositionMs = r.GetInt64(2),
                    LengthMs = r.GetInt64(3),
                    Completed = r.GetInt32(4) != 0
                });
            }
            return list;
        }
    }

    public int ClearRecentlyWatched()
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE WatchRecords SET PositionMs=0, Completed=1";
            return cmd.ExecuteNonQuery();
        }
    }

    public int ClearPlaybackHistory()
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM WatchRecords";
            return cmd.ExecuteNonQuery();
        }
    }

    public int ClearFavorites()
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Favorites";
            return cmd.ExecuteNonQuery();
        }
    }

    public string? GetSetting(string key)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key=$k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO AppSettings(Key, Value) VALUES($k, $v)
                ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value
                """;
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
    }

    public static string NormalizePath(string path)
    {
        try { return System.IO.Path.GetFullPath(path.TrimEnd().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)); }
        catch { return path; }
    }

    private static DateTime ParseDate(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).ToLocalTime();
}
