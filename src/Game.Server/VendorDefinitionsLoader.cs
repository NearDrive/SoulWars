using System.Collections.Immutable;
using System.Text.Json;
using Game.Core;

namespace Game.Server;

public static class VendorDefinitionsLoader
{
    public static ImmutableArray<VendorDefinition> LoadFromDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException("Vendor definitions path cannot be empty.");
        }

        if (!Directory.Exists(directoryPath))
        {
            throw new InvalidOperationException($"Vendor definitions directory does not exist: '{directoryPath}'.");
        }

        string[] files = Directory
            .GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        ImmutableArray<VendorDefinition>.Builder vendors = ImmutableArray.CreateBuilder<VendorDefinition>();
        foreach (string file in files)
        {
            VendorDefinitionFile? parsed = JsonSerializer.Deserialize<VendorDefinitionFile>(File.ReadAllText(file), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed is null || parsed.ZoneId <= 0 || string.IsNullOrWhiteSpace(parsed.VendorId) || parsed.Offers is null)
            {
                throw new InvalidOperationException($"Invalid vendor json in '{file}'.");
            }

            ImmutableArray<VendorOfferDefinition> offers = parsed.Offers
                .Select(o => new VendorOfferDefinition(o.ItemId, o.BuyPrice, o.SellPrice, o.MaxPerTransaction))
                .OrderBy(o => o.ItemId, StringComparer.Ordinal)
                .ToImmutableArray();

            vendors.Add(new VendorDefinition(parsed.VendorId, new ZoneId(parsed.ZoneId), offers));
        }

        return vendors
            .ToImmutable()
            .OrderBy(v => v.ZoneId.Value)
            .ThenBy(v => v.VendorId, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private sealed class VendorDefinitionFile
    {
        public int ZoneId { get; set; }
        public string VendorId { get; set; } = string.Empty;
        public List<VendorOfferFile>? Offers { get; set; }
    }

    private sealed class VendorOfferFile
    {
        public string ItemId { get; set; } = string.Empty;
        public long BuyPrice { get; set; }
        public long SellPrice { get; set; }
        public int? MaxPerTransaction { get; set; }
    }
}
