using SpoofGUI.Database;
using SpoofGUI.Models;

namespace SpoofGUI.GUI.ViewModels;

public sealed class ConfigPageViewModel
{
    private readonly ProfileRepository _profiles;
    public ConfigPageViewModel(ProfileRepository profiles) => _profiles = profiles;

    public SpoofProfile LoadProfile() =>
        _profiles.GetActive() ?? new SpoofProfile
        {
            Name = "default",
            ConnectIp = "104.19.229.21",
            FakeSni = "www.hcaptcha.com",
            IsActive = true,
        };

    public void Save(SpoofProfile p) => _profiles.Upsert(p);
}
