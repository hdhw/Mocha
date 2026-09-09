using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mocha2023.Classes
{
    public static class StorefrontEditor
    {
        private static readonly object Sync = new();

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true
        };

        private static string StoreRoot => Path.Combine(Program.dataDir, "storefronts");

        private static string CategoryFile =>
            Path.Combine(Program.dataDir, "StorefrontCategories.json");

        public sealed class StorefrontSummary
        {
            public int StoreId { get; set; }
            public string Category { get; set; } = string.Empty;
            public int Items { get; set; }
            public int SubscriberDiscountPercent { get; set; }
            public List<string> Sets { get; set; } = new();
        }

        public sealed class StoreItemSummary
        {
            public int PurchasableItemId { get; set; }
            public string FriendlyName { get; set; } = string.Empty;
            public int Price { get; set; }
            public int Rarity { get; set; }
            public int ItemSetId { get; set; }
            public string ItemSetFriendlyName { get; set; } = string.Empty;
        }

        public sealed class CategoryApply
        {
            public int? Price { get; set; }
            public int? SubscriberDiscountPercent { get; set; }
            public long? AddSkuId { get; set; }
            public int? RemovePurchasableItemId { get; set; }
        }

        public sealed class ApplyResult
        {
            public List<int> StoreIds { get; set; } = new();
            public int ItemsRepriced { get; set; }
            public int ItemsAdded { get; set; }
            public int ItemsRemoved { get; set; }
            public List<string> Warnings { get; set; } = new();
        }

        public static List<int> KnownStoreIds()
        {
            if (!Directory.Exists(StoreRoot))
                return new List<int>();

            var ids = new List<int>();
            foreach (string path in Directory.EnumerateFiles(StoreRoot, "sf*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (int.TryParse(name.AsSpan(2), out int id))
                    ids.Add(id);
            }

            ids.Sort();
            return ids;
        }

        public static Dictionary<string, List<int>> GetCategories()
        {
            lock (Sync)
                return ReadCategories();
        }

        public static void SaveCategories(Dictionary<string, List<int>> categories)
        {
            lock (Sync)
            {
                var cleaned = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                var known = KnownStoreIds().ToHashSet();

                foreach (var pair in categories)
                {
                    string name = pair.Key.Trim();
                    if (name.Length == 0)
                        continue;

                    cleaned[name] = (pair.Value ?? new List<int>())
                        .Where(known.Contains)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToList();
                }

                WriteAtomic(CategoryFile, JsonSerializer.Serialize(cleaned, WriteOptions));
            }
        }

        public static List<StorefrontSummary> List()
        {
            lock (Sync)
            {
                var categories = ReadCategories();
                var byStore = new Dictionary<int, string>();
                foreach (var pair in categories)
                    foreach (int id in pair.Value)
                        byStore[id] = pair.Key;

                var results = new List<StorefrontSummary>();

                foreach (int id in KnownStoreIds())
                {
                    JsonNode? root = LoadStore(id);
                    JsonArray items = ItemsOf(root);

                    var sets = items
                        .Select(item => SetNameOf(item))
                        .Where(name => name.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    results.Add(new StorefrontSummary
                    {
                        StoreId = id,
                        Category = byStore.TryGetValue(id, out string? category) ? category : string.Empty,
                        Items = items.Count,
                        SubscriberDiscountPercent =
                            root?["SubscriberDiscountPercent"]?.GetValue<int>() ?? 0,
                        Sets = sets
                    });
                }

                return results;
            }
        }

        public static List<StoreItemSummary> Items(int storeId)
        {
            lock (Sync)
            {
                var results = new List<StoreItemSummary>();

                foreach (JsonNode? item in ItemsOf(LoadStore(storeId)))
                {
                    if (item == null)
                        continue;

                    JsonNode? gift = item["GiftDrop"];

                    results.Add(new StoreItemSummary
                    {
                        PurchasableItemId = item["PurchasableItemId"]?.GetValue<int>() ?? 0,
                        FriendlyName = gift?["FriendlyName"]?.GetValue<string>() ?? string.Empty,
                        Price = FirstPrice(item),
                        Rarity = gift?["Rarity"]?.GetValue<int>() ?? 0,
                        ItemSetId = gift?["ItemSetId"]?.GetValue<int>() ?? 0,
                        ItemSetFriendlyName = SetNameOf(item)
                    });
                }

                return results;
            }
        }

        public static ApplyResult ApplyToCategory(string category, CategoryApply apply)
        {
            lock (Sync)
            {
                var result = new ApplyResult();
                var categories = ReadCategories();

                if (!categories.TryGetValue(category, out List<int>? storeIds) || storeIds.Count == 0)
                {
                    result.Warnings.Add($"Category '{category}' has no storefronts.");
                    return result;
                }

                JsonObject? addition = null;
                if (apply.AddSkuId is long skuId)
                {
                    addition = BuildStoreItem(skuId, apply.Price);
                    if (addition == null)
                        result.Warnings.Add($"SKU {skuId} is not in the catalog; nothing was added.");
                }

                foreach (int storeId in storeIds)
                {
                    JsonNode? root = LoadStore(storeId);
                    if (root == null)
                    {
                        result.Warnings.Add($"sf{storeId}.json could not be read.");
                        continue;
                    }

                    JsonArray items = ItemsOf(root);
                    bool touched = false;

                    if (apply.RemovePurchasableItemId is int removeId)
                    {
                        for (int i = items.Count - 1; i >= 0; i--)
                        {
                            if (items[i]?["PurchasableItemId"]?.GetValue<int>() == removeId)
                            {
                                items.RemoveAt(i);
                                result.ItemsRemoved++;
                                touched = true;
                            }
                        }
                    }

                    if (apply.Price is int price)
                    {
                        foreach (JsonNode? item in items)
                        {
                            if (item == null)
                                continue;

                            SetPrices(item, price, apply.SubscriberDiscountPercent
                                ?? root["SubscriberDiscountPercent"]?.GetValue<int>()
                                ?? 0);
                            result.ItemsRepriced++;
                            touched = true;
                        }
                    }

                    if (apply.SubscriberDiscountPercent is int percent)
                    {
                        root["SubscriberDiscountPercent"] = percent;
                        touched = true;
                    }

                    if (addition != null &&
                        !items.Any(item => item?["PurchasableItemId"]?.GetValue<int>() ==
                            addition["PurchasableItemId"]?.GetValue<int>()))
                    {
                        items.Add(addition.DeepClone());
                        result.ItemsAdded++;
                        touched = true;
                    }

                    if (touched)
                    {
                        SaveStore(storeId, root);
                        result.StoreIds.Add(storeId);
                    }
                }

                return result;
            }
        }

        public static (int SetId, int Items) CreateSet(
            int storeId,
            string name,
            IEnumerable<int> purchasableItemIds)
        {
            lock (Sync)
            {
                string setName = name.Trim();
                if (setName.Length == 0)
                    throw new ArgumentException("A set needs a name.");

                var wanted = purchasableItemIds.ToHashSet();
                if (wanted.Count == 0)
                    throw new ArgumentException("Pick at least one item for the set.");

                JsonNode? root = LoadStore(storeId)
                    ?? throw new InvalidOperationException($"sf{storeId}.json was not found.");

                int setId = NextSetId();
                int touched = 0;

                foreach (JsonNode? item in ItemsOf(root))
                {
                    if (item?["GiftDrop"] is not JsonNode gift)
                        continue;
                    if (!wanted.Contains(item["PurchasableItemId"]?.GetValue<int>() ?? 0))
                        continue;

                    gift["ItemSetId"] = setId;
                    gift["ItemSetFriendlyName"] = setName;
                    touched++;
                }

                if (touched == 0)
                    throw new InvalidOperationException("None of those items are in that storefront.");

                SaveStore(storeId, root);
                return (setId, touched);
            }
        }

        public static int ClearSet(int storeId, int setId)
        {
            lock (Sync)
            {
                JsonNode? root = LoadStore(storeId);
                if (root == null)
                    return 0;

                int touched = 0;
                foreach (JsonNode? item in ItemsOf(root))
                {
                    if (item?["GiftDrop"] is not JsonNode gift)
                        continue;
                    if ((gift["ItemSetId"]?.GetValue<int>() ?? 0) != setId)
                        continue;

                    gift["ItemSetId"] = 0;
                    gift["ItemSetFriendlyName"] = string.Empty;
                    touched++;
                }

                if (touched > 0)
                    SaveStore(storeId, root);

                return touched;
            }
        }

        private static int NextSetId()
        {
            int highest = 0;

            foreach (int id in KnownStoreIds())
                foreach (JsonNode? item in ItemsOf(LoadStore(id)))
                {
                    int setId = item?["GiftDrop"]?["ItemSetId"]?.GetValue<int>() ?? 0;
                    if (setId > highest)
                        highest = setId;
                }

            return highest + 1;
        }

        private static JsonObject? BuildStoreItem(long skuId, int? price)
        {
            var item = Controllers.APIController.GetStorefrontCatalogItem(skuId);
            if (item == null)
                return null;

            int cost = price ?? (item.Price > 0 ? item.Price : 500);
            int purchasableId = (int)(item.SkuId % int.MaxValue);

            var gift = new JsonObject
            {
                ["AvatarItemDesc"] = item.AvatarItemDesc,
                ["AvatarItemType"] = item.AvatarItemType,
                ["ConsumableItemDesc"] = item.ConsumableItemDesc,
                ["Context"] = 0,
                ["Currency"] = 0,
                ["CurrencyType"] = 0,
                ["EquipmentModificationGuid"] = item.EquipmentModificationGuid,
                ["EquipmentPrefabName"] = item.EquipmentPrefabName,
                ["FriendlyName"] = item.FriendlyName,
                ["GiftDropId"] = purchasableId,
                ["IsQuery"] = false,
                ["ItemSetFriendlyName"] = string.Empty,
                ["ItemSetId"] = 0,
                ["Rarity"] = item.Rarity,
                ["SubscribersOnly"] = false,
                ["Tooltip"] = string.Empty,
                ["Unique"] = true
            };

            var result = new JsonObject
            {
                ["GiftDrop"] = gift,
                ["IsFeatured"] = false,
                ["PurchasableItemId"] = purchasableId,
                ["Type"] = 0
            };

            SetPrices(result, cost, 10);
            return result;
        }

        private static void SetPrices(JsonNode item, int price, int subscriberPercent)
        {
            int percent = Math.Clamp(subscriberPercent, 0, 100);
            int subscriber = (int)Math.Round(price * (100 - percent) / 100.0,
                MidpointRounding.AwayFromZero);

            item["Prices"] = PriceArray(price);
            item["SubscriberPrices"] = PriceArray(subscriber);
        }

        private static JsonArray PriceArray(int price) => new(
            new JsonObject
            {
                ["CurrencyType"] = 2,
                ["Price"] = price,
                ["StorefrontSaleData"] = null,
                ["Type"] = 0
            });

        private static int FirstPrice(JsonNode item) =>
            item["Prices"] is JsonArray prices && prices.Count > 0
                ? prices[0]?["Price"]?.GetValue<int>() ?? 0
                : 0;

        private static string SetNameOf(JsonNode? item) =>
            item?["GiftDrop"]?["ItemSetFriendlyName"]?.GetValue<string>() ?? string.Empty;

        private static JsonArray ItemsOf(JsonNode? root) =>
            root?["StoreItems"] as JsonArray ?? new JsonArray();

        private static JsonNode? LoadStore(int storeId)
        {
            string path = Path.Combine(StoreRoot, $"sf{storeId}.json");
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonNode.Parse(File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                ServerLog.Error($"sf{storeId}.json is not valid JSON: {ex.Message}");
                return null;
            }
        }

        private static void SaveStore(int storeId, JsonNode root) =>
            WriteAtomic(
                Path.Combine(StoreRoot, $"sf{storeId}.json"),
                root.ToJsonString(WriteOptions));

        private static Dictionary<string, List<int>> ReadCategories()
        {
            if (!File.Exists(CategoryFile))
                return new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, List<int>>>(
                    File.ReadAllText(CategoryFile)) is { } parsed
                    ? new Dictionary<string, List<int>>(parsed, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException ex)
            {
                ServerLog.Error($"StorefrontCategories.json is not valid JSON: {ex.Message}");
                return new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void WriteAtomic(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
    }
}
