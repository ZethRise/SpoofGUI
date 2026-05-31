using SpoofGUI.Core;
using SpoofGUI.Database;
using SpoofGUI.Models;

namespace SpoofGUI.GUI.ViewModels;

public sealed class ConfigPageViewModel
{
    public const int MaxProfiles = 100;

    private readonly ProfileRepository _profiles;
    private readonly SniListService _sniList;

    public ConfigPageViewModel(ProfileRepository profiles, SniListService sniList)
    {
        _profiles = profiles;
        _sniList = sniList;
    }

    public IReadOnlyList<SpoofProfile> All() => _profiles.All();
    public int Count() => _profiles.Count();
    public bool CanAdd => _profiles.Count() < MaxProfiles;

    public SpoofProfile NewDraft()
    {
        var existing = _profiles.All();
        return new SpoofProfile
        {
            Name = UniqueName(existing.Select(p => p.Name), "profile"),
            ListenHost = "0.0.0.0",
            ListenPort = 40443,
            ConnectIp = "104.19.229.21",
            ConnectPort = 443,
            FakeSni = "www.hcaptcha.com",
            IsActive = existing.Count == 0,
        };
    }

    public bool NameIsTaken(string name, long exceptId) =>
        _profiles.All().Any(p => p.Id != exceptId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public long Save(SpoofProfile profile) => _profiles.Upsert(profile);
    public void SetActive(long id) => _profiles.SetActive(id);
    public void Delete(long id) => _profiles.Delete(id);

    public Task<IReadOnlyList<SniListEntry>> FetchSniListAsync() => _sniList.FetchAsync();

    public (int Added, int Skipped) AddFromEntries(IReadOnlyList<SniListEntry> entries)
    {
        var existing = _profiles.All();
        var names = new HashSet<string>(existing.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var slots = MaxProfiles - existing.Count;
        var added = 0;
        var skipped = 0;

        foreach (var entry in entries)
        {
            if (slots <= 0) { skipped++; continue; }
            var name = UniqueName(names, entry.Sni);
            names.Add(name);
            _profiles.Upsert(new SpoofProfile
            {
                Name = name,
                ListenHost = "0.0.0.0",
                ListenPort = 40443,
                ConnectIp = entry.Ip,
                ConnectPort = entry.Port,
                FakeSni = entry.Sni,
                IsActive = existing.Count == 0 && added == 0,
            });
            added++;
            slots--;
        }

        return (added, skipped);
    }

    private static string UniqueName(IEnumerable<string> existing, string baseName)
    {
        var names = existing as ICollection<string> ?? existing.ToList();
        var taken = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName)) return baseName;
        for (var i = 2; i <= 999; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!taken.Contains(candidate)) return candidate;
        }

        return $"{baseName} ({Guid.NewGuid():N})";
    }
}
