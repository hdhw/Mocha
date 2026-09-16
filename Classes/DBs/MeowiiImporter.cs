using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mocha2023.Classes.DBs;

public static partial class MeowiiImporter
{
    private const string ApiBase = "https://live.meowii.app";
    private const string CdnBase = "https://cdn.meowii.app";
    private const string ClientUserAgent = "BestHTTP/2.4.5";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public sealed class RoomSummary
    {
        public long RoomId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public int Accessibility { get; init; }
        public long CreatorAccountId { get; init; }
        public int SubRoomCount { get; init; }
        public int PlayableSubRoomCount { get; init; }
    }

    public static async Task<List<RoomSummary>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Enter a room name to search for.", nameof(query));

        string url = $"{ApiBase}/rooms/bulk?name={Uri.EscapeDataString(query.Trim())}&excludePrivateRooms=false";
        using HttpResponseMessage response = await Client.GetAsync(url, cancellationToken);

        List<MeowiiRoom>? rooms = null;
        if (response.IsSuccessStatusCode)
        {
            try
            {
                rooms = await response.Content.ReadFromJsonAsync<List<MeowiiRoom>>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"The meowii API returned invalid JSON for '{query}': {ex.Message}");
            }
        }
        else if ((int)response.StatusCode == 404)
        {
            return new List<RoomSummary>();
        }
        else
        {
            throw new InvalidDataException(
                $"The meowii API returned {(int)response.StatusCode} for the search '{query}'.");
        }

        return rooms?
            .Where(room => room.RoomId > 0 || !string.IsNullOrWhiteSpace(room.Name))
            .Select(room => new RoomSummary
            {
                RoomId = room.RoomId,
                Name = room.Name ?? string.Empty,
                Description = room.Description,
                Accessibility = room.Accessibility,
                CreatorAccountId = room.CreatorAccountId,
                SubRoomCount = room.SubRooms?.Count ?? 0,
                PlayableSubRoomCount = (room.SubRooms ?? new List<MeowiiSubRoom>())
                    .Count(subRoom => !string.IsNullOrWhiteSpace(subRoom.DataBlob))
            })
            .ToList() ?? new List<RoomSummary>();
    }

    public static async Task<ShowdownImporter.ImportResult> ImportRoomAsync(
        string roomIdOrName,
        long creatorAccountId = 1,
        bool replaceExisting = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roomIdOrName))
            throw new ArgumentException("Enter a meowii room ID or name to import.", nameof(roomIdOrName));

        MeowiiRoom? room = null;
        if (long.TryParse(roomIdOrName.Trim(), out long requestedRoomId) && requestedRoomId > 0)
        {
            room = await FetchByIdAsync(requestedRoomId, cancellationToken);
        }

        if (room == null)
        {
            List<RoomSummary> matches = await SearchAsync(roomIdOrName, cancellationToken);
            if (matches.Count == 0)
            {
                throw new InvalidDataException(
                    $"No meowii rooms matched \"{roomIdOrName}\".");
            }

            RoomSummary? exact = matches.FirstOrDefault(match =>
                string.Equals(match.Name, roomIdOrName.Trim(), StringComparison.OrdinalIgnoreCase));
            RoomSummary? choice = exact ?? matches.First();

            if (exact == null)
            {
                Console.WriteLine(
                    $"[MEOWII IMPORT] No exact name match for \"{roomIdOrName}\"; " +
                    $"importing \"{choice.Name}\" instead.");
            }

            room = await FetchByIdAsync(choice.RoomId, cancellationToken);
        }

        if (room == null || room.RoomId <= 0)
            throw new InvalidDataException("The meowii API returned an unusable room.");

        string stagingRoot = Path.Combine(
            Program.dataDir,
            "Temp",
            "Meowii",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        try
        {
            await StageRoom(room, stagingRoot, cancellationToken);
            return ShowdownImporter.Import(stagingRoot, creatorAccountId, replaceExisting);
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                    Directory.Delete(stagingRoot, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MEOWII IMPORT] Could not clean up staging folder: {ex.Message}");
            }
        }
    }

    private static async Task StageRoom(
        MeowiiRoom room,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var exportSubRooms = new List<object>();
        int playableSubRooms = 0;
        int blobsCopied = 0;
        long firstSubRoomId = 0;

        long generatedSubRoomId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach ((MeowiiSubRoom subRoom, int index) in (room.SubRooms ?? new List<MeowiiSubRoom>())
                     .Select((value, index) => (value, index)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? blobName = Path.GetFileName(subRoom.DataBlob ?? string.Empty);
            if (string.IsNullOrWhiteSpace(blobName))
            {
                Console.WriteLine(
                    $"[MEOWII IMPORT] Subroom {subRoom.SubRoomId}:{subRoom.Name} has no DataBlob; skipped.");
                continue;
            }

            long effectiveSubRoomId = subRoom.SubRoomId > 0
                ? subRoom.SubRoomId
                : generatedSubRoomId + index;

            string subRoomDirectory = Path.Combine(stagingRoot, effectiveSubRoomId.ToString());
            string saveDirectory = Path.Combine(subRoomDirectory, "Saves", effectiveSubRoomId.ToString());
            Directory.CreateDirectory(saveDirectory);

            string blobPath = Path.Combine(saveDirectory, blobName);
            await DownloadToOneAsync(
                $"{CdnBase}/room/{Uri.EscapeDataString(blobName)}",
                blobPath,
                "room blob",
                cancellationToken);

            if (!File.Exists(blobPath) || new FileInfo(blobPath).Length == 0)
            {
                Console.WriteLine(
                    $"[MEOWII IMPORT] Empty room blob for {subRoom.Name}; skipped.");
                continue;
            }
            blobsCopied++;

            int persistenceVersion = ParsePersistenceVersion(blobPath);
            var exportSave = new ExportSave
            {
                SubRoomDataSaveId = effectiveSubRoomId,
                SubRoomId = effectiveSubRoomId,
                DataBlob = blobName,
                DataBlobHash = ComputeSha256Base64(blobPath),
                PersistenceVersion = persistenceVersion,
                CreatedAt = NormalizeDate(subRoom.DataSavedAt)
            };
            WriteJson(Path.Combine(saveDirectory, effectiveSubRoomId + ".json"), exportSave);

            var exportSubRoom = new ExportSubRoom
            {
                RoomId = room.RoomId,
                SubRoomId = effectiveSubRoomId,
                Name = string.IsNullOrWhiteSpace(subRoom.Name) ? "Home" : subRoom.Name,
                UnitySceneId = subRoom.UnitySceneId ?? string.Empty,
                IsSandbox = subRoom.IsSandbox,
                MaxPlayers = subRoom.MaxPlayers,
                Accessibility = (int)RoomAccessibility.Public,
                CurrentSave = new ExportSave { SubRoomDataSaveId = effectiveSubRoomId }
            };
            WriteJson(Path.Combine(subRoomDirectory, "subRoom.json"), exportSubRoom);

            firstSubRoomId = firstSubRoomId == 0 ? effectiveSubRoomId : firstSubRoomId;
            exportSubRooms.Add(new
            {
                exportSubRoom.RoomId,
                exportSubRoom.SubRoomId,
                exportSubRoom.Name,
                exportSubRoom.UnitySceneId,
                exportSubRoom.IsSandbox,
                exportSubRoom.MaxPlayers,
                exportSubRoom.Accessibility,
                CurrentSave = new ExportSave { SubRoomDataSaveId = effectiveSubRoomId }
            });
            playableSubRooms++;
        }

        if (playableSubRooms == 0)
        {
            throw new InvalidDataException(
                "This meowii room has no subrooms with a downloadable room blob to import.");
        }

        string stagedImageName = string.Empty;
        if (!string.IsNullOrWhiteSpace(room.ImageName))
        {
            string imageFileName = Path.GetFileName(room.ImageName);
            string imagePath = Path.Combine(stagingRoot, imageFileName);
            try
            {
                await DownloadToOneAsync(
                    $"{CdnBase}/{Uri.EscapeDataString(imageFileName)}",
                    imagePath,
                    "room image",
                    cancellationToken);
                stagedImageName = Path.GetFileName(imagePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[MEOWII IMPORT] Could not fetch thumbnail {room.ImageName}: {ex.Message}");
            }
        }

        int roomAccessibility = (int)RoomAccessibility.Public;

        var exportRoom = new
        {
            RoomId = room.RoomId,
            Name = room.Name ?? string.Empty,
            Description = string.IsNullOrWhiteSpace(room.Description)
                ? "Imported from meow.net."
                : room.Description,
            ImageName = stagedImageName,
            MaxPlayers = room.MaxPlayers,
            Accessibility = roomAccessibility,
            PersistenceVersion = room.PersistenceVersion,
            SubRoomId = firstSubRoomId,
            SubRooms = exportSubRooms
        };
        WriteJson(Path.Combine(stagingRoot, "room.json"), exportRoom);

        Console.WriteLine(
            $"[MEOWII IMPORT] Staged \"{room.Name}\" room={room.RoomId} " +
            $"subrooms={playableSubRooms} blobs={blobsCopied} " +
            $"persistence={room.PersistenceVersion} image={stagedImageName}");
    }

    private static async Task<MeowiiRoom?> FetchByIdAsync(long roomId, CancellationToken cancellationToken)
    {
        string url = $"{ApiBase}/rooms/{roomId}";
        using HttpResponseMessage response = await Client.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Console.WriteLine($"[MEOWII IMPORT] No room with ID {roomId}.");
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidDataException(
                $"The meowii API returned {(int)response.StatusCode} for room {roomId}.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<MeowiiRoom>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The meowii API returned invalid JSON for room {roomId}: {ex.Message}");
        }
    }

    private static async Task DownloadToOneAsync(
        string url,
        string destinationPath,
        string description,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidDataException(
                $"Could not download {description} from {url} ({(int)response.StatusCode}).");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await input.CopyToAsync(output, cancellationToken);
    }

    private static int ParsePersistenceVersion(string blobPath)
    {
        try
        {
            if (File.Exists(blobPath) && new FileInfo(blobPath).Length >= 3)
            {
                using var stream = File.OpenRead(blobPath);
                int first = stream.ReadByte();
                int second = stream.ReadByte();
                int third = stream.ReadByte();
                stream.Close();

                if (first == 8 && (second & 0x80) == 0)
                    return second;
                if (first == 8 && (second & 0x80) != 0 && (third & 0x80) == 0)
                    return ((second & 0x7f) << 7) | third;
            }
        }
        catch (Exception)
        {
            return 0;
        }

        return 0;
    }

    private static DateTime NormalizeDate(string? raw)
    {
        if (DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTime parsed))
        {
            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }
        return DateTime.UtcNow;
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ComputeSha256Base64(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20) };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ClientUserAgent);
        return client;
    }

    private sealed class ExportSave
    {
        public long SubRoomDataSaveId { get; set; }
        public long SubRoomId { get; set; }
        public string? UnityAssetId { get; set; }
        public string DataBlob { get; set; } = string.Empty;
        public string? DataBlobHash { get; set; }
        public int PersistenceVersion { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class ExportSubRoom
    {
        public long RoomId { get; set; }
        public long SubRoomId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? UnitySceneId { get; set; }
        public ExportSave? CurrentSave { get; set; }
        public bool IsSandbox { get; set; }
        public int MaxPlayers { get; set; }
        public int Accessibility { get; set; }
    }

    private sealed class MeowiiRoom
    {
        public long RoomId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public long CreatorAccountId { get; set; }
        public int Accessibility { get; set; }
        public int MaxPlayers { get; set; }
        public int PersistenceVersion { get; set; }
        public List<MeowiiSubRoom>? SubRooms { get; set; }
    }

    private sealed class MeowiiSubRoom
    {
        public long SubRoomId { get; set; }
        public long RoomId { get; set; }
        public int Accessibility { get; set; }
        public string? DataBlob { get; set; }
        public string? DataSavedAt { get; set; }
        public bool IsSandbox { get; set; }
        public int MaxPlayers { get; set; }
        public string? Name { get; set; }
        public string? UnitySceneId { get; set; }
    }
}