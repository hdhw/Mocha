using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mocha2023.Classes
{

    public static class PlayerInventoryStore
    {
        private static readonly object InventoryLock = new();
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true
        };

        public sealed class AvatarItemOwnership
        {
            public long AvatarItemId { get; set; }
            public string AvatarItemDesc { get; set; } = string.Empty;
            public string FriendlyName { get; set; } = string.Empty;
        }

        public sealed class ConsumableOwnership
        {
            public long ConsumableItemId { get; set; }
            public string ConsumableItemDesc { get; set; } = string.Empty;
            public string FriendlyName { get; set; } = string.Empty;
            public int Quantity { get; set; }
        }

        public static string GetAvatarItemsPath(long accountId)
        {
            string directory = Path.Combine(
                Program.dataDir,
                "AvatarItemsUnlocked",
                accountId.ToString());
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "AvatarItems.json");
        }

        public static string GetConsumablesPath(long accountId)
        {
            string directory = Path.Combine(
                Program.dataDir,
                "ConsumablesUnlocked",
                accountId.ToString());
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "Consumables.json");
        }

        public static void EnsureInitialized(
            long accountId,
            IEnumerable<string>? legacyAvatarDescriptors = null)
        {
            EnsureAvatarItemsInitialized(accountId, legacyAvatarDescriptors);
            EnsureConsumablesInitialized(accountId);
        }

        public static List<AvatarItemOwnership> GetAvatarCatalog() =>
            LoadAvatarCatalog();

        public static List<ConsumableOwnership> GetConsumableCatalog() =>
            LoadConsumableCatalog();

        public static void EnsureAvatarItemsInitialized(
            long accountId,
            IEnumerable<string>? legacyAvatarDescriptors = null)
        {
            lock (InventoryLock)
            {
                string playerPath = GetAvatarItemsPath(accountId);
                if (File.Exists(playerPath))
                    return;

                List<AvatarItemOwnership> owned = LoadAvatarCatalog()
                    .ToList();

                HashSet<string> legacy = (legacyAvatarDescriptors ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (legacy.Count > 0)
                {
                    foreach (AvatarItemOwnership item in LoadAvatarCatalog())
                    {
                        if (!legacy.Contains(item.AvatarItemDesc))
                            continue;
                        if (owned.Any(value => SameAvatar(value, item)))
                            continue;
                        owned.Add(item);
                    }

                    foreach (string descriptor in legacy)
                    {
                        if (owned.Any(value => string.Equals(
                                value.AvatarItemDesc,
                                descriptor,
                                StringComparison.OrdinalIgnoreCase)))
                            continue;

                        owned.Add(new AvatarItemOwnership
                        {
                            AvatarItemDesc = descriptor,
                            FriendlyName = descriptor
                        });
                    }
                }

                SaveAvatarItemsUnsafe(playerPath, owned);
            }
        }

        public static void EnsureConsumablesInitialized(long accountId)
        {
            lock (InventoryLock)
            {
                string playerPath = GetConsumablesPath(accountId);
                if (File.Exists(playerPath))
                    return;

                SaveConsumablesUnsafe(playerPath, LoadConsumableCatalog());
            }
        }

        public static List<AvatarItemOwnership> GetAvatarItems(
            long accountId,
            IEnumerable<string>? legacyAvatarDescriptors = null)
        {
            lock (InventoryLock)
            {
                EnsureAvatarItemsInitialized(accountId, legacyAvatarDescriptors);
                try
                {
                    return JsonSerializer.Deserialize<List<AvatarItemOwnership>>(
                               File.ReadAllText(GetAvatarItemsPath(accountId)),
                               ReadOptions)
                           ?? new List<AvatarItemOwnership>();
                }
                catch (JsonException)
                {
                    return new List<AvatarItemOwnership>();
                }
            }
        }

        public static List<ConsumableOwnership> GetConsumables(long accountId)
        {
            lock (InventoryLock)
            {
                EnsureConsumablesInitialized(accountId);
                try
                {
                    return JsonSerializer.Deserialize<List<ConsumableOwnership>>(
                               File.ReadAllText(GetConsumablesPath(accountId)),
                               ReadOptions)
                           ?? new List<ConsumableOwnership>();
                }
                catch (JsonException)
                {
                    return new List<ConsumableOwnership>();
                }
            }
        }

        public static bool OwnsAvatarItem(
            long accountId,
            string descriptor,
            long avatarItemId = 0,
            IEnumerable<string>? legacyAvatarDescriptors = null)
        {
            if (string.IsNullOrWhiteSpace(descriptor) && avatarItemId <= 0)
                return false;

            return GetAvatarItems(accountId, legacyAvatarDescriptors).Any(item =>
                (!string.IsNullOrWhiteSpace(descriptor) && string.Equals(
                    item.AvatarItemDesc,
                    descriptor,
                    StringComparison.OrdinalIgnoreCase)) ||
                (avatarItemId > 0 && item.AvatarItemId == avatarItemId));
        }

        public static bool SetAvatarItemOwned(
            long accountId,
            string descriptor,
            long avatarItemId,
            string? friendlyName,
            bool owned,
            IEnumerable<string>? legacyAvatarDescriptors = null)
        {
            if (string.IsNullOrWhiteSpace(descriptor) && avatarItemId <= 0)
                return false;

            // Callers that only have the raw descriptor (gift boxes, mainly)
            // used to leave FriendlyName/AvatarItemId blank, which fell back
            // to the descriptor GUID string as a fake friendly name and an
            // AvatarItemId stuck at 0. Resolve the real values from the full
            // catalog instead whenever they're missing.
            if (owned && (string.IsNullOrWhiteSpace(friendlyName) || avatarItemId <= 0))
            {
                (long Id, string Name)? resolved = LookupMasterAvatarCatalogItem(descriptor);
                if (resolved != null)
                {
                    if (string.IsNullOrWhiteSpace(friendlyName))
                        friendlyName = resolved.Value.Name;
                    if (avatarItemId <= 0 && resolved.Value.Id > 0)
                        avatarItemId = resolved.Value.Id;
                }
            }

            lock (InventoryLock)
            {
                List<AvatarItemOwnership> items = GetAvatarItems(
                    accountId,
                    legacyAvatarDescriptors);

                int index = items.FindIndex(item =>
                    (!string.IsNullOrWhiteSpace(descriptor) && string.Equals(
                        item.AvatarItemDesc,
                        descriptor,
                        StringComparison.OrdinalIgnoreCase)) ||
                    (avatarItemId > 0 && item.AvatarItemId == avatarItemId));

                if (owned)
                {
                    if (index < 0)
                    {
                        items.Add(new AvatarItemOwnership
                        {
                            AvatarItemId = avatarItemId,
                            AvatarItemDesc = descriptor?.Trim() ?? string.Empty,
                            FriendlyName = string.IsNullOrWhiteSpace(friendlyName)
                                ? descriptor?.Trim() ?? $"Avatar Item {avatarItemId}"
                                : friendlyName.Trim()
                        });
                    }
                    else
                    {
                        if (avatarItemId > 0)
                            items[index].AvatarItemId = avatarItemId;
                        if (!string.IsNullOrWhiteSpace(descriptor))
                            items[index].AvatarItemDesc = descriptor.Trim();
                        if (!string.IsNullOrWhiteSpace(friendlyName))
                            items[index].FriendlyName = friendlyName.Trim();
                    }
                }
                else if (index >= 0)
                {
                    items.RemoveAt(index);
                }

                SaveAvatarItemsUnsafe(GetAvatarItemsPath(accountId), items);
                return true;
            }
        }

        // One-time repair for records already written with the old bug: an
        // AvatarItemId stuck at 0 and FriendlyName set to the raw descriptor
        // GUID instead of a real name, because the caller granted the item by
        // descriptor alone. Re-resolves every account's file against the full
        // catalog. Returns (accounts fixed, items fixed).
        public static (int AccountsFixed, int ItemsFixed) RepairAvatarItemNames()
        {
            string root = Path.Combine(Program.dataDir, "AvatarItemsUnlocked");
            if (!Directory.Exists(root))
                return (0, 0);

            int accountsFixed = 0;
            int itemsFixed = 0;

            lock (InventoryLock)
            {
                foreach (string accountDir in Directory.EnumerateDirectories(root))
                {
                    string path = Path.Combine(accountDir, "AvatarItems.json");
                    if (!File.Exists(path))
                        continue;

                    List<AvatarItemOwnership> items;
                    try
                    {
                        items = JsonSerializer.Deserialize<List<AvatarItemOwnership>>(
                                    File.ReadAllText(path),
                                    ReadOptions)
                                ?? new List<AvatarItemOwnership>();
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    bool changed = false;
                    foreach (AvatarItemOwnership item in items)
                    {
                        bool brokenName = !string.IsNullOrWhiteSpace(item.AvatarItemDesc) &&
                            string.Equals(
                                item.FriendlyName,
                                item.AvatarItemDesc,
                                StringComparison.OrdinalIgnoreCase);
                        bool brokenId = item.AvatarItemId <= 0;
                        if (!brokenName && !brokenId)
                            continue;

                        (long Id, string Name)? resolved =
                            LookupMasterAvatarCatalogItem(item.AvatarItemDesc);
                        if (resolved == null)
                            continue;

                        if (brokenName && !string.IsNullOrWhiteSpace(resolved.Value.Name))
                        {
                            item.FriendlyName = resolved.Value.Name;
                            changed = true;
                            itemsFixed++;
                        }
                        if (brokenId && resolved.Value.Id > 0)
                        {
                            item.AvatarItemId = resolved.Value.Id;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        SaveAvatarItemsUnsafe(path, items);
                        accountsFixed++;
                    }
                }
            }

            return (accountsFixed, itemsFixed);
        }

        public static int GetConsumableQuantity(
            long accountId,
            string descriptor,
            long consumableItemId = 0)
        {
            if (string.IsNullOrWhiteSpace(descriptor) && consumableItemId <= 0)
                return 0;

            return SumConsumableQuantity(
                GetConsumables(accountId).Where(value =>
                    SameConsumable(value, descriptor, consumableItemId)));
        }

        public static int SetConsumableQuantity(
            long accountId,
            string descriptor,
            long consumableItemId,
            string? friendlyName,
            int quantity)
        {
            quantity = Math.Clamp(quantity, 0, 100_000);
            if (string.IsNullOrWhiteSpace(descriptor) && consumableItemId <= 0)
                return 0;

            lock (InventoryLock)
            {
                List<ConsumableOwnership> items = GetConsumables(accountId);
                List<int> matchingIndexes = items
                    .Select((value, index) => new { value, index })
                    .Where(entry => SameConsumable(
                        entry.value,
                        descriptor,
                        consumableItemId))
                    .Select(entry => entry.index)
                    .ToList();

                if (quantity <= 0)
                {
                    for (int match = matchingIndexes.Count - 1; match >= 0; match--)
                        items.RemoveAt(matchingIndexes[match]);
                }
                else if (matchingIndexes.Count == 0)
                {
                    items.Add(new ConsumableOwnership
                    {
                        ConsumableItemId = consumableItemId,
                        ConsumableItemDesc = descriptor?.Trim() ?? string.Empty,
                        FriendlyName = string.IsNullOrWhiteSpace(friendlyName)
                            ? descriptor?.Trim() ?? $"Consumable {consumableItemId}"
                            : friendlyName.Trim(),
                        Quantity = quantity
                    });
                }
                else
                {
                    int index = matchingIndexes[0];
                    items[index].Quantity = quantity;
                    if (consumableItemId > 0)
                        items[index].ConsumableItemId = consumableItemId;
                    if (!string.IsNullOrWhiteSpace(descriptor))
                        items[index].ConsumableItemDesc = descriptor.Trim();
                    if (!string.IsNullOrWhiteSpace(friendlyName))
                        items[index].FriendlyName = friendlyName.Trim();

                    for (int match = matchingIndexes.Count - 1; match >= 1; match--)
                        items.RemoveAt(matchingIndexes[match]);
                }

                SaveConsumablesUnsafe(GetConsumablesPath(accountId), items);
                return quantity;
            }
        }

        public static int AddConsumable(
            long accountId,
            string descriptor,
            long consumableItemId,
            string? friendlyName,
            int amount)
        {
            if (amount <= 0)
                return GetConsumableQuantity(accountId, descriptor, consumableItemId);

            lock (InventoryLock)
            {
                int current = GetConsumableQuantity(
                    accountId,
                    descriptor,
                    consumableItemId);
                int next = (int)Math.Min(100_000L, (long)current + amount);
                return SetConsumableQuantity(
                    accountId,
                    descriptor,
                    consumableItemId,
                    friendlyName,
                    next);
            }
        }

        public static bool TryConsumeConsumable(
            long accountId,
            string descriptor,
            long consumableItemId,
            int amount,
            out int previousQuantity,
            out int remainingQuantity)
        {
            previousQuantity = 0;
            remainingQuantity = 0;
            if (amount <= 0 ||
                (string.IsNullOrWhiteSpace(descriptor) && consumableItemId <= 0))
            {
                return false;
            }

            lock (InventoryLock)
            {
                List<ConsumableOwnership> items = GetConsumables(accountId);
                List<int> matchingIndexes = items
                    .Select((value, index) => new { value, index })
                    .Where(entry => SameConsumable(
                        entry.value,
                        descriptor,
                        consumableItemId))
                    .Select(entry => entry.index)
                    .ToList();

                previousQuantity = SumConsumableQuantity(
                    matchingIndexes.Select(index => items[index]));
                if (matchingIndexes.Count == 0 || previousQuantity <= 0)
                    return false;

                remainingQuantity = Math.Max(0, previousQuantity - amount);

                if (remainingQuantity == 0)
                {
                    for (int match = matchingIndexes.Count - 1; match >= 0; match--)
                        items.RemoveAt(matchingIndexes[match]);
                }
                else
                {
                    int index = matchingIndexes[0];
                    items[index].Quantity = remainingQuantity;
                    for (int match = matchingIndexes.Count - 1; match >= 1; match--)
                        items.RemoveAt(matchingIndexes[match]);
                }

                SaveConsumablesUnsafe(GetConsumablesPath(accountId), items);
                return true;
            }
        }

        private static List<AvatarItemOwnership> LoadAvatarCatalog()
        {
            string path = Path.Combine(
                Program.dataDir,
                "APIS",
                "Items",
                "AvatarItemsDefault.json");

            if (!File.Exists(path))
                return new List<AvatarItemOwnership>();

            try
            {
                JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
                if (root is not JsonArray array)
                    return new List<AvatarItemOwnership>();

                var results = new List<AvatarItemOwnership>();
                foreach (JsonNode? node in array)
                {
                    if (node is not JsonObject item)
                        continue;

                    long id = ReadLong(item, "AvatarItemId", "ItemId", "Id");
                    string descriptor = ReadString(
                        item,
                        "AvatarItemDesc",
                        "AvatarItemDescriptor",
                        "ItemDesc");
                    string name = ReadString(item, "FriendlyName", "Name");

                    if (id <= 0 && string.IsNullOrWhiteSpace(descriptor))
                        continue;

                    results.Add(new AvatarItemOwnership
                    {
                        AvatarItemId = id,
                        AvatarItemDesc = descriptor,
                        FriendlyName = string.IsNullOrWhiteSpace(name)
                            ? descriptor
                            : name
                    });
                }

                return results
                    .GroupBy(item => string.IsNullOrWhiteSpace(item.AvatarItemDesc)
                            ? $"id:{item.AvatarItemId}"
                            : $"desc:{item.AvatarItemDesc}",
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            catch
            {
                return new List<AvatarItemOwnership>();
            }
        }

        // The full catalog (thousands of items), not AvatarItemsDefault.json's
        // small starter set above - used to resolve a real FriendlyName/
        // AvatarItemId whenever a caller grants an item by descriptor alone
        // (e.g. a gift box) instead of falling back to the raw GUID descriptor
        // as a fake "friendly name" with AvatarItemId stuck at 0.
        private static Dictionary<string, (long Id, string Name)>? _masterAvatarCatalogCache;
        private static DateTime _masterAvatarCatalogCacheTimestamp;
        private static readonly object MasterAvatarCatalogLock = new();

        // AvatarItemDesc is "id,mod1,mod2,mod3" with unused slots either
        // omitted ("id,") or left as empty segments ("id,,,") depending on
        // which code path wrote it - both mean the same item, but a naive
        // string match treats them as different descriptors. Strip trailing
        // empty segments (keeping a trailing comma if there was one at all)
        // so both forms resolve to the same catalog lookup key.
        private static string NormalizeAvatarDescriptor(string descriptor)
        {
            if (string.IsNullOrEmpty(descriptor))
                return descriptor;

            string[] parts = descriptor.Split(',');
            int last = parts.Length - 1;
            while (last > 0 && parts[last].Length == 0)
                last--;

            return last == parts.Length - 1
                ? descriptor
                : string.Join(',', parts, 0, last + 1) + ",";
        }

        private static (long Id, string Name)? LookupMasterAvatarCatalogItem(string descriptor)
        {
            if (string.IsNullOrWhiteSpace(descriptor))
                return null;

            string path = Path.Combine(Program.dataDir, "APIS", "Items", "AvatarItems.json");
            if (!File.Exists(path))
                return null;

            DateTime timestamp = File.GetLastWriteTimeUtc(path);

            lock (MasterAvatarCatalogLock)
            {
                if (_masterAvatarCatalogCache == null ||
                    timestamp != _masterAvatarCatalogCacheTimestamp)
                {
                    var built = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
                        if (root is JsonArray array)
                        {
                            foreach (JsonNode? node in array)
                            {
                                if (node is not JsonObject item)
                                    continue;

                                string desc = ReadString(
                                    item,
                                    "AvatarItemDesc",
                                    "AvatarItemDescriptor",
                                    "ItemDesc");
                                if (string.IsNullOrWhiteSpace(desc))
                                    continue;
                                string normalizedDesc = NormalizeAvatarDescriptor(desc);
                                if (built.ContainsKey(normalizedDesc))
                                    continue;

                                long id = ReadLong(item, "AvatarItemId", "ItemId", "Id");
                                string name = ReadString(item, "FriendlyName", "Name");
                                built[normalizedDesc] = (id, string.IsNullOrWhiteSpace(name) ? desc : name);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                    }

                    _masterAvatarCatalogCache = built;
                    _masterAvatarCatalogCacheTimestamp = timestamp;
                }

                return _masterAvatarCatalogCache.TryGetValue(
                    NormalizeAvatarDescriptor(descriptor),
                    out var found)
                    ? found
                    : null;
            }
        }

        private static List<ConsumableOwnership> LoadConsumableCatalog()
        {
            string path = Path.Combine(
                Program.dataDir,
                "APIS",
                "Items",
                "ConsumablesDefault.json");

            if (!File.Exists(path))
                return new List<ConsumableOwnership>();

            try
            {
                JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
                if (root is not JsonArray array)
                    return new List<ConsumableOwnership>();

                var results = new List<ConsumableOwnership>();
                foreach (JsonNode? node in array)
                {
                    if (node is not JsonObject item)
                        continue;

                    long id = ReadLong(item, "ConsumableItemId", "ItemId", "Id");
                    if (id <= 0 && item["Ids"] is JsonArray ids)
                    {
                        foreach (JsonNode? idNode in ids)
                        {
                            if (TryLong(idNode, out long candidate) && candidate > 0)
                            {
                                id = candidate;
                                break;
                            }
                        }
                    }

                    string descriptor = ReadString(
                        item,
                        "ConsumableItemDesc",
                        "ConsumableDesc",
                        "ItemDesc");
                    string name = ReadString(item, "FriendlyName", "Name");
                    if (id <= 0 && string.IsNullOrWhiteSpace(descriptor))
                        continue;

                    long configuredQuantity = ReadLong(
                        item,
                        "Count",
                        "Quantity",
                        "InitialCount");
                    int quantity = configuredQuantity > 0
                        ? (int)Math.Min(100_000L, configuredQuantity)
                        : item["Ids"] is JsonArray itemIds
                            ? Math.Max(1, itemIds.Count)
                            : 1;

                    results.Add(new ConsumableOwnership
                    {
                        ConsumableItemId = id,
                        ConsumableItemDesc = descriptor,
                        FriendlyName = string.IsNullOrWhiteSpace(name)
                            ? descriptor
                            : name,
                        Quantity = quantity
                    });
                }

                return results
                    .GroupBy(item => string.IsNullOrWhiteSpace(item.ConsumableItemDesc)
                            ? $"id:{item.ConsumableItemId}"
                            : $"desc:{item.ConsumableItemDesc}",
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            catch
            {
                return new List<ConsumableOwnership>();
            }
        }

        private static bool SameAvatar(
            AvatarItemOwnership left,
            AvatarItemOwnership right) =>
            (!string.IsNullOrWhiteSpace(left.AvatarItemDesc) &&
             string.Equals(left.AvatarItemDesc, right.AvatarItemDesc, StringComparison.OrdinalIgnoreCase)) ||
            (left.AvatarItemId > 0 && left.AvatarItemId == right.AvatarItemId);

        private static bool SameConsumable(
            ConsumableOwnership item,
            string descriptor,
            long consumableItemId) =>
            (!string.IsNullOrWhiteSpace(descriptor) && string.Equals(
                item.ConsumableItemDesc,
                descriptor,
                StringComparison.OrdinalIgnoreCase)) ||
            (consumableItemId > 0 &&
             item.ConsumableItemId == consumableItemId);

        private static int SumConsumableQuantity(
            IEnumerable<ConsumableOwnership> items)
        {
            long total = items.Sum(item => (long)Math.Max(0, item.Quantity));
            return (int)Math.Min(100_000L, total);
        }

        private static void SaveAvatarItemsUnsafe(
            string path,
            List<AvatarItemOwnership> items) =>
            File.WriteAllText(path, JsonSerializer.Serialize(items, WriteOptions));

        private static void SaveConsumablesUnsafe(
            string path,
            List<ConsumableOwnership> items) =>
            File.WriteAllText(path, JsonSerializer.Serialize(items, WriteOptions));

        private static string ReadString(JsonObject item, params string[] names)
        {
            foreach (string name in names)
            {
                JsonNode? node = item[name];
                if (node == null)
                    continue;
                try
                {
                    string? value = node.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
                catch
                {
                    string value = node.ToJsonString().Trim('"');
                    if (!string.IsNullOrWhiteSpace(value) && value != "null")
                        return value;
                }
            }
            return string.Empty;
        }

        private static long ReadLong(JsonObject item, params string[] names)
        {
            foreach (string name in names)
                if (TryLong(item[name], out long value))
                    return value;
            return 0;
        }

        private static bool TryLong(JsonNode? node, out long value)
        {
            value = 0;
            if (node == null)
                return false;
            try
            {
                if (node is JsonValue jsonValue && jsonValue.TryGetValue(out value))
                    return true;
            }
            catch
            {
            }
            return long.TryParse(node.ToJsonString().Trim('"'), out value);
        }
    }
}
