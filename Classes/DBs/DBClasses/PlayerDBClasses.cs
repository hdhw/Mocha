using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mocha2023.Classes;
using LiteDB;

namespace Mocha2023.Classes.DBs.DBClasses
{
    public partial class PlayerDBClasses
    {
        public class FullPlayer
        {
            [BsonId]
            public long PlayerId { get; set; }
            public List<mPlatformID> PlatformIds { get; set; } = new();
            [JsonIgnore]
            public List<string>? DeviceIds { get; set; } = new();
            [JsonIgnore]
            public string? AuthToken { get; set; }
            [JsonIgnore]
            public string? Password { get; set; }
            [JsonIgnore]
            public string? TwoFactorSecret { get; set; }
            public bool TwoFactorEnabled { get; set; }
            [JsonIgnore]
            public List<string>? TwoFactorRecoveryCodeHashes { get; set; }
            [JsonIgnore]
            public string? SignupIp { get; set; }
            [JsonIgnore]
            public string? LastKnownIp { get; set; }
            [JsonIgnore]
            public DateTime? LastKnownIpAt { get; set; }
            [JsonIgnore]
            public List<AccountIpRecord>? RecentIps { get; set; }
            public bool LoginDisabled { get; set; }
            public List<PlayerRoles> PlayerRoles { get; set; } = new();
            public Player? Player { get; set; }
        }

        public class AccountIpRecord
        {
            public string Ip { get; set; } = string.Empty;
            public DateTime SeenAt { get; set; }
        }

        public class Player
        {
            public string? Username { get; set; }
            public string? DisplayName { get; set; }
            public string? Bio { get; set; }
            public int AvailableUsernameChanges { get; set; } = 3;
            public bool? IsJunior { get; set; }
            public bool IsAgeVerified { get; set; }
            public DateTime? AgeVerifiedAt { get; set; }
            public int Level { get; set; } = 1;
            public int XP { get; set; } = 0;
            public string? ProfileImage { get; set; }
            public string? BannerImage { get; set; }
            public string? Email { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastLoginAt { get; set; }
            public DateTime? Birthday { get; set; }
            public CurrentAuthSession? CurrentAuthSession { get; set; } = new CurrentAuthSession();
            public Reputation Reputation { get; set; } = new Reputation();
            public List<long> VisitedRooms { get; set; } = new();
            public List<long> CheeredRooms { get; set; } = new List<long>();
            public List<long> FavoritedRooms { get; set; } = new List<long>();
            public List<PlayerRelationship> Relationships { get; set; } = new();
            public List<PlayerCheerRecord> ReceivedCheers { get; set; } = new();
            public List<long> SubscribedAccountIds { get; set; } = new();
            public int SupportedInfluencerId { get; set; }
            public string? InfluencerCode { get; set; }
            public PlayerExtra PlayerExtra { get; set; } = new PlayerExtra();
            public string DisplayEmoji { get; set; } = "";
            public int PersonalPronouns { get; set; }

            public int IdentityFlags { get; set; }
        }

        public class PlayerRelationship
        {
            public long Id { get; set; }
            public long PlayerID { get; set; }
            public RelationshipType RelationshipType { get; set; }
            public bool Favorited { get; set; }
            public bool Muted { get; set; }
            public bool Ignored { get; set; }
        }

        public sealed class ClientRelationshipDTO
        {
            [JsonPropertyName("PlayerID")]
            public int PlayerID { get; set; }

            [JsonPropertyName("RelationshipType")]
            public int RelationshipType { get; set; }

            [JsonPropertyName("Favorited")]
            public int Favorited { get; set; }

            [JsonPropertyName("Muted")]
            public int Muted { get; set; }

            [JsonPropertyName("Ignored")]
            public int Ignored { get; set; }
        }

        public class PlayerCheerRecord
        {
            public long CheerId { get; set; }
            public long NotificationId { get; set; }
            public long GiverPlayerId { get; set; }
            public long ReceiverPlayerId { get; set; }
            public CheerCategory CheerCategory { get; set; }
            public bool Anonymous { get; set; }
            public CheerStatus Status { get; set; } = CheerStatus.Pending;
            public DateTime CreatedAt { get; set; }
            public DateTime? HandledAt { get; set; }
        }

        public class PlayerDTOBase
        {
            public long accountId { get; set; }
            public DateTime createdAt { get; set; }
            public string? displayName { get; set; }
            public bool? isJunior { get; set; }
            public int platforms { get; set; }
            public string? profileImage { get; set; }
            public string? bannerImage { get; set; }
            public string? username { get; set; }
            public int personalPronouns { get; set; }
            public int identityFlags { get; set; }
            public string? displayEmoji { get; set; }
        }

        public class PlayerDTO : PlayerDTOBase { }
        public class PlayerMeDTO : PlayerDTOBase
        {
            public int availableUsernameChanges { get; set; } = 3;
            public DateTime? birthday { get; set; }
            public string? email { get; set; }
            public string? phone { get; set; }
        }

        public class CurrentAuthSession { }

        public class PlayerExtra
        {
            public Avatar Avatar { get; set; } = new Avatar();
            public List<string> AvatarItems { get; set; } = new();
            public List<SavedOutfit> SavedAvatars { get; set; } = new();
            public ModerationBlockDetails? ModerationBlockDetails { get; set; } = new ModerationBlockDetails();
            public List<Setting> Settings { get; set; } = new();
            public Heartbeat Heartbeat { get; set; } = new Heartbeat();
            public List<PlayerCurrency> Currencies { get; set; } = new();
            public List<RoomVisit> RoomVisits { get; set; } = new();
            public List<EquipmentItem> Equipment { get; set; } = new();
            public List<GiftPackage> PendingGiftPackages { get; set; } = new();

            // Keyed by "{rewardType}|{giftContext}" - one claim per hour per
            // (type, activity) pair, per /api/gamerewards/v1/request.
            [JsonIgnore]
            public Dictionary<string, long> GameRewardClaimUnixTime { get; set; } = new();

            [JsonIgnore]
            public long FriendotronLastSentUtcDay { get; set; }

            public long LevelingRoomInstanceId { get; set; }
            public int LevelingRoomActiveSeconds { get; set; }
            public long LevelingLastHeartbeatUnixTime { get; set; }

            public long LaserTagRoomInstanceId { get; set; }
            public int LaserTagRoomActiveSeconds { get; set; }
            public long LaserTagLastHeartbeatUnixTime { get; set; }

            public long PartyId { get; set; }
            public long PartyLeaderPlayerId { get; set; }
            public List<long> PartyMemberPlayerIds { get; set; } = new();
            public List<PartyInviteRecord> PendingPartyInvites { get; set; } = new();

            public List<string> Warnings { get; set; } = new();
            public List<string> ItemWishlist { get; set; } = new();
        }

        public sealed class GiftPackage
        {
            public long GiftPackageId { get; set; }
            public int FromPlayerId { get; set; }
            public int PlayerId { get; set; }
            public string Message { get; set; } = string.Empty;
            public string AvatarItemDesc { get; set; } = string.Empty;
            public string ConsumableItemDesc { get; set; } = string.Empty;
            public int ConsumableQuantity { get; set; } = 1;
            public string EquipmentPrefabName { get; set; } = string.Empty;
            public string EquipmentModificationGuid { get; set; } = string.Empty;
            public string FriendlyName { get; set; } = string.Empty;
            public string Tooltip { get; set; } = string.Empty;
            public string ThumbnailImage { get; set; } = string.Empty;
            public int CurrencyType { get; set; } = 2;
            public int Currency { get; set; }
            public int XP { get; set; }
            public int GiftContext { get; set; }
            public int Rarity { get; set; }
            public int Platform { get; set; } = -1;
            public int PlatformMask { get; set; } = -1;
            public int? BalanceType { get; set; } = -1;
            public bool IsQuery { get; set; }
            public bool Unique { get; set; } = true;
        }

        public class PartyInviteRecord
        {
            public long PartyId { get; set; }
            public long InviterPlayerId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class PartySnapshot
        {
            public long PartyId { get; set; }
            public long LeaderPlayerId { get; set; }
            public List<long> MemberPlayerIds { get; set; } = new();
        }

        public class EquipmentItem
        {
            public string PrefabName { get; set; } = "";
            public string ModificationGuid { get; set; } = "";
            public int UnlockedLevel { get; set; } = 0;
            public bool Favorited { get; set; } = false;
            public int PlatformMask { get; set; } = -1;
            public string FriendlyName { get; set; } = "";
            public string Tooltip { get; set; } = "";
            public int Rarity { get; set; } = 0;
            public string ThumbnailImage { get; set; } = "";
        }

        public class RoomVisit
        {
            public long RoomId { get; set; }
            public DateTime VisitedAt { get; set; }
        }

        public class PlayerCurrency
        {
            public int Balance { get; set; }
            public CurrencyType CurrencyType { get; set; }
            public BalanceType BalanceType { get; set; }
        }

        public class Avatar
        {
            public string OutfitSelections { get; set; } = "";
            public string FaceFeatures { get; set; } = "";
            public string SkinColor { get; set; } = "";
            public string HairColor { get; set; } = "";
        }

        public class Heartbeat
        {
            public string appVersion { get; set; } = ServerConfig.GameVersion.ToString();
            public DeviceClasses? deviceClass { get; set; } = DeviceClasses.Unknown;
            public MatchmakingErrorCode? errorCode { get; set; } = null;
            public bool isOnline { get; set; } = false;
            public long playerId { get; set; } = 0;
            public RoomInstance? roomInstance { get; set; } = null;
            public StatusVisibility statusVisibility { get; set; } = StatusVisibility.Online;
            public int vrMovementMode { get; set; } = 0;

            [JsonIgnore]
            public long lastHeartbeatUnixTime { get; set; } = 0;
        }

        public class RoomInstance
        {
            public bool encryptVoiceChat { get; set; }
            public long clubId { get; set; } = 0;
            public string? dataBlob { get; set; }
            public long eventId { get; set; } = 0;
            public bool isFull { get; set; }
            public bool isInProgress { get; set; }
            public bool isPrivate { get; set; }
            public string location { get; set; }
            public int maxCapacity { get; set; }
            public string Name { get; set; }
            public string photonRegion { get; set; }
            public string photonRegionId { get; set; }
            public string photonRoomId { get; set; }
            public string roomCode { get; set; } = "";
            public long roomId { get; set; }
            public long roomInstanceId { get; set; }
            public RoomInstanceType roomInstanceType { get; set; }
            public long subRoomId { get; set; }
            public long subRoomDataSaveId { get; set; }

            [JsonIgnore]
            public DateTime createdAt { get; set; }
        }

        public class Reputation
        {
            public long AccountId { get; set; }
            public bool IsCheerful { get; set; }
            public double Noteriety { get; set; }
            public CheerCategory SelectedCheer { get; set; }
            public int CheerCredit { get; set; } = 15;

            [JsonIgnore]
            public long CheerCreditRefreshAtUnixTime { get; set; }
            public int CheerGeneral { get; set; }
            public int CheerHelpful { get; set; }
            public int CheerCreative { get; set; }
            public int CheerGreatHost { get; set; }
            public int CheerSportsman { get; set; }
            public int SubscriberCount { get; set; }
            public int SubscribedCount { get; set; }
        }
        public class ModerationBlockDetails
        {
            public ReportCategory ReportCategory { get; set; } = ReportCategory.Moderator;
            public int Duration { get; set; } = 0;
            public long GameSessionId { get; set; } = 0;
            public bool? IsBan { get; set; } = false;
            public bool? IsHostKick { get; set; } = false;
            public string? Message { get; set; } = "";
            public ulong? PlayerIdReporter { get; set; } = null;
            [JsonIgnore]
            public long ModerationSetUnixTime { get; set; } = 0;
            [JsonIgnore]
            public ulong BannedByPlayerId { get; set; } = 0;
            [JsonIgnore]
            public string? AppealCode { get; set; }
            [JsonIgnore]
            public bool AppealSubmitted { get; set; }
            [JsonIgnore]
            public DateTime? AppealSubmittedAt { get; set; }
        }

        public class Setting
        {
            public required string Key { get; set; }
            public required string Value { get; set; }
        }

        public class mPlatformID
        {
            public Platforms Platform { get; set; }
            public ulong PlatformId { get; set; }
        }

        public class CachedLogins
        {
            public Platforms platform { get; set; }
            public string? platformId { get; set; }
            public long accountId { get; set; }
            public DateTime? lastLoginTime { get; set; }
            public bool requirePassword { get; set; }
        }

        public class PlayerProgressionDTO
        {
            public long PlayerId { get; set; }
            public int Level { get; set; }
            public int XP { get; set; }
        }

        public class SavedOutfit : Avatar
        {
            public int Slot { get; set; }
            public string? PreviewImageName { get; set; }
        }
    }

    public static class RoleUtils
    {
        public static bool TryParseRole(string roleName, out PlayerDBClasses.PlayerRoles role)
        {
            role = default;
            if (string.IsNullOrWhiteSpace(roleName))
                return false;

            switch (roleName.Trim().ToLowerInvariant())
            {
                case "developer":
                case "dev":
                    role = PlayerDBClasses.PlayerRoles.Developer;
                    return true;
                case "communityteam":
                case "community-team":
                case "community_team":
                case "community":
                case "ct":
                    role = PlayerDBClasses.PlayerRoles.CommunityTeam;
                    return true;
                case "moderator":
                    role = PlayerDBClasses.PlayerRoles.Moderator;
                    return true;
                case "screenshare":
                    role = PlayerDBClasses.PlayerRoles.Screenshare;
                    return true;
                case "influencer":
                    role = PlayerDBClasses.PlayerRoles.Influencer;
                    return true;
                case "rrplus":
                case "rr+":
                    role = PlayerDBClasses.PlayerRoles.RRPlus;
                    return true;
                case "keepsake":
                    role = PlayerDBClasses.PlayerRoles.Keepsake;
                    return true;
                default:
                    return Enum.TryParse(roleName, true, out role);
            }
        }
    }
}
