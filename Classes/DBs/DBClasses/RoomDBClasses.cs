using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Mocha2023.Classes.DBs.Postgres;

namespace Mocha2023.Classes.DBs.DBClasses
{
    public partial class RoomDBClasses
    {
        public const string RoomsCollectionName = "Rooms";
        public const string SubRoomDataSavesCollectionName = "SubRoomDataSaves";
        public const string RoomBansCollectionName = "RoomBans";

        public class Room
        {
            public long RoomId { get; set; }

            public bool IsDorm { get; set; }

            public int MaxPlayerCalculationMode { get; set; }

            public int MaxPlayers { get; set; }

            public bool CloningAllowed { get; set; }

            public bool DisableMicAutoMute { get; set; }

            public bool DisableRoomComments { get; set; }

            public bool EncryptVoiceChat { get; set; }

            public bool ToxmodEnabled { get; set; }

            public bool LoadScreenLocked { get; set; }

            public int PersistenceVersion { get; set; }

            public bool AutoLocalizeRoom { get; set; }

            public bool IsDeveloperOwned { get; set; }

            public string Name { get; set; } = string.Empty;

            public string? Description { get; set; }

            public string ImageName { get; set; } = string.Empty;

            public WarningMaskType WarningMask { get; set; }

            public string? CustomWarning { get; set; }

            public long CreatorAccountId { get; set; }

            public RoomState? State { get; set; }

            public RoomAccessibility Accessibility { get; set; }

            public bool SupportsLevelVoting { get; set; }

            public bool IsRRO { get; set; }

            public bool IsBaseRoom { get; set; }

            [JsonIgnore]
            public bool CreativeToolsBetaEnabled { get; set; }

            [PostgresIgnore]
            [JsonPropertyName("CreativeToolsBetaEnabled")]
            public bool CreativeToolsBetaEnabledForClient
            {
                get => CreativeToolsBetaEnabled;
                set => CreativeToolsBetaEnabled = value;
            }

            [PostgresIgnore]
            [JsonPropertyName("SupportsBetaContent")]
            public bool SupportsBetaContentForClient
            {
                get => CreativeToolsBetaEnabled;
                set => CreativeToolsBetaEnabled = value;
            }

            [PostgresIgnore]
            [JsonPropertyName("IsBeta")]
            public bool IsBetaForClient
            {
                get => CreativeToolsBetaEnabled;
                set => CreativeToolsBetaEnabled = value;
            }

            [PostgresIgnore]
            [JsonPropertyName("BetaContentEnabled")]
            public bool BetaContentEnabledForClient
            {
                get => CreativeToolsBetaEnabled;
                set => CreativeToolsBetaEnabled = value;
            }

            public bool SupportsScreens { get; set; }

            public bool SupportsWalkVR { get; set; }

            public bool SupportsTeleportVR { get; set; }

            public bool SupportsVRLow { get; set; }

            public bool SupportsQuest2 { get; set; }

            public bool SupportsMobile { get; set; }

            public bool SupportsJuniors { get; set; }

            public int MinLevel { get; set; }

            public DateTime CreatedAt { get; set; }

            public Stats Stats { get; set; } = new();

            public string? RankedEntityId { get; set; }

            public string? RankingContext { get; set; }

            public List<SubRooms> SubRooms { get; set; } = new();

            public List<Roles> Roles { get; set; } = new();

            public string? DataBlob { get; set; }

            [JsonIgnore]
            public int UgcVersion { get; set; }

            [PostgresIgnore]
            [JsonPropertyName("Version")]
            public int VersionForClient
            {
                get => Math.Max(1, UgcVersion);
                set => UgcVersion = Math.Max(1, value);
            }

            public List<Tags> Tags { get; set; } = new();

            public List<string> PromoImages { get; set; } = new();

            public List<PromoExternalContent> PromoExternalContent { get; set; }
                = new();

            public List<LoadScreens> LoadScreens { get; set; } = new();

            public List<RoomLeaderboard> Leaderboards { get; set; } = new();
        }

        public class SubRooms
        {
            public long SubRoomId { get; set; }

            public long RoomId { get; set; }

            public string Name { get; set; } = string.Empty;

            public string? DataBlob { get; set; }

            [JsonIgnore]
            public long SubRoomDataSaveId { get; set; }

            [PostgresIgnore]
            [JsonPropertyName("CurrentSave")]
            public SubRoomDataSave? SubRoomDataSave { get; set; }

            [PostgresIgnore]
            [JsonPropertyName("DataSavedAt")]
            public DateTime? DataSavedAtForClient =>
                SubRoomDataSave?.CreatedAt;

            public bool IsSandbox { get; set; }

            public int MaxPlayers { get; set; }

            public RoomAccessibility Accessibility { get; set; }

            public string UnitySceneId { get; set; } = string.Empty;

            public List<SubRoomPermission> Permissions { get; set; } = new();

            [JsonIgnore]
            public long SavedByAccountId { get; set; }
        }

        public class SubRoomPermission
        {
            public bool Override { get; set; }

            public string Permission { get; set; } = string.Empty;

            public int Role { get; set; }

            public int Type { get; set; }

            public string Value { get; set; } = "True";
        }

        public class SubRoomDataSave
        {
            public sealed class SubRoomSaveResponse
            {
                [JsonPropertyName("Success")]
                public bool Success { get; set; } = true;

                [JsonPropertyName("RoomId")]
                public long RoomId { get; set; }

                [JsonPropertyName("SubRoomId")]
                public long SubRoomId { get; set; }

                [JsonPropertyName("SubRoomDataSaveId")]
                public long SubRoomDataSaveId { get; set; }

                [JsonPropertyName("SavedByAccountId")]
                public long SavedByAccountId { get; set; }

                [JsonPropertyName("DataBlob")]
                public string DataBlob { get; set; } = string.Empty;

                [JsonPropertyName("SubRoomDataFilename")]
                public string SubRoomDataFilename { get; set; } = string.Empty;

                [JsonPropertyName("DataBlobHash")]
                public string? DataBlobHash { get; set; }

                [JsonPropertyName("SubRoomDataHash")]
                public string? SubRoomDataHash { get; set; }

                [JsonPropertyName("UnityAssetId")]
                public string? UnityAssetId { get; set; }

                [JsonPropertyName("ReferencedUnityAssetIds")]
                public List<string> ReferencedUnityAssetIds { get; set; } = new();

                [JsonPropertyName("BakedUnityAssets")]
                public List<BakedUnityAsset> BakedUnityAssets { get; set; } = new();

                [JsonPropertyName("PersistenceVersion")]
                public int PersistenceVersion { get; set; }

                [JsonPropertyName("SavedOnPlatform")]
                public int? SavedOnPlatform { get; set; }

                [JsonPropertyName("SavedOnDeviceClass")]
                public int? SavedOnDeviceClass { get; set; }

                [JsonPropertyName("Description")]
                public string? Description { get; set; }

                [JsonPropertyName("CreatedAt")]
                public DateTime CreatedAt { get; set; }

                [JsonPropertyName("DataSavedAt")]
                public DateTime DataSavedAt { get; set; }

                [JsonPropertyName("CurrentSave")]
                public SubRoomDataSave CurrentSave { get; set; } = new();
            }
            [JsonPropertyName("SubRoomDataSaveId")]
            public long SubRoomDataSaveId { get; set; }

            [JsonIgnore]
            public long RoomId { get; set; }

            [JsonPropertyName("SubRoomId")]
            public long SubRoomId { get; set; }

            [JsonPropertyName("SavedByAccountId")]
            public long SavedByAccountId { get; set; }

            [JsonPropertyName("DataBlob")]
            public string DataBlob { get; set; } = string.Empty;

            [PostgresIgnore]
            [JsonPropertyName("SubRoomDataFilename")]
            public string SubRoomDataFilename => DataBlob;

            [JsonIgnore]
            public string? RoomDataBlob { get; set; }

            [JsonPropertyName("DataBlobHash")]
            public string? DataBlobHash { get; set; }

            [PostgresIgnore]
            [JsonPropertyName("SubRoomDataHash")]
            public string? SubRoomDataHash => DataBlobHash;

            [JsonPropertyName("PersistenceVersion")]
            public int? PersistenceVersion { get; set; }

            [JsonPropertyName("SavedOnPlatform")]
            public int? SavedOnPlatform { get; set; }

            [JsonPropertyName("SavedOnDeviceClass")]
            public int? SavedOnDeviceClass { get; set; }

            [JsonPropertyName("Description")]
            public string? Description { get; set; }

            [JsonPropertyName("UnityAssetId")]
            public string? UnityAssetId { get; set; }

            [JsonPropertyName("ReferencedUnityAssetIds")]
            public List<string> ReferencedUnityAssetIds { get; set; } = new();

            [JsonPropertyName("OMVersion")]
            public int OMVersion { get; set; }

            [JsonPropertyName("UgcSubVersion")]
            public int UgcSubVersion { get; set; }

            [JsonPropertyName("ModerationState")]
            public int ModerationState { get; set; }

            [JsonPropertyName("Tags")]
            public List<string> Tags { get; set; } = new();

            [JsonPropertyName("BakedUnityAssets")]
            public List<BakedUnityAsset> BakedUnityAssets { get; set; } = new();

            [JsonPropertyName("CreatedAt")]
            public DateTime CreatedAt { get; set; }

            [JsonIgnore]
            public bool IsPublished { get; set; }
        }

        public class BakedUnityAsset
        {
            [JsonPropertyName("UnityAssetId")]
            public string UnityAssetId { get; set; } = string.Empty;

            [JsonPropertyName("Target")]
            public int Target { get; set; }

            [JsonPropertyName("Version")]
            public int Version { get; set; }

            [JsonPropertyName("Filename")]
            public string Filename { get; set; } = string.Empty;

            [JsonPropertyName("Hash")]
            public string? Hash { get; set; }

            [JsonPropertyName("UnityVersion")]
            public string? UnityVersion { get; set; }

            [JsonPropertyName("IsAvailable")]
            public bool IsAvailable { get; set; }
        }

        public static SubRoomDataSave? FindNewestSubRoomSave(
            Mocha2023.Classes.DBs.Postgres.PostgresCollection<SubRoomDataSave> saves,
            long roomId,
            long subRoomId)
        {
            ArgumentNullException.ThrowIfNull(saves);

            return saves
                .Find(save => save.RoomId == roomId && save.SubRoomId == subRoomId)
                .OrderByDescending(save => save.CreatedAt)
                .ThenByDescending(save => save.SubRoomDataSaveId)
                .FirstOrDefault();
        }

        public static void ApplyCurrentSave(
            SubRooms subRoom,
            SubRoomDataSave currentSave)
        {
            ArgumentNullException.ThrowIfNull(subRoom);
            ArgumentNullException.ThrowIfNull(currentSave);

            subRoom.SubRoomDataSaveId =
                currentSave.SubRoomDataSaveId;

            subRoom.SubRoomDataSave =
                currentSave;

            subRoom.DataBlob =
                currentSave.DataBlob;

            subRoom.SavedByAccountId =
                currentSave.SavedByAccountId;
        }

        public class Tags
        {
            public string Tag { get; set; } = string.Empty;

            public TagType Type { get; set; }
        }

        public class PromoExternalContent
        {
            public PromoExternalContentType Type { get; set; }

            public string Reference { get; set; } = string.Empty;
        }

        public class RoomLeaderboard
        {
            public long RoomId { get; set; }

            public int LeaderboardId { get; set; }

            public string LeaderboardTitle { get; set; } = string.Empty;

            public int StatFormat { get; set; }

            public bool SortAscending { get; set; }
        }

        public class LoadScreens
        {
            public string ImageName { get; set; } = string.Empty;

            public string Title { get; set; } = string.Empty;

            public string Subtitle { get; set; } = string.Empty;
        }

        public class Stats
        {
            public int CheerCount { get; set; }

            public int FavoriteCount { get; set; }

            public int VisitorCount { get; set; }

            public int VisitCount { get; set; }
        }

        public class Roles
        {
            public long AccountId { get; set; }

            public Role Role { get; set; }

            public Role InvitedRole { get; set; }
        }

        public class RoomEditPermission
        {
            public bool CanEditRoom { get; set; }

            public string Error { get; set; } = string.Empty;
        }

        public class RoomBan
        {
            public long RoomBanId { get; set; }

            public long RoomId { get; set; }

            public long AccountId { get; set; }

            public long BannedByAccountId { get; set; }

            public string? Reason { get; set; }

            public DateTime BannedAt { get; set; }
        }
    }
}
