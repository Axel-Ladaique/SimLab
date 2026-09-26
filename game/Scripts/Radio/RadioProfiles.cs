using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>
/// How the radio screen's steps read and write radio profiles: from the store, or, in demo mode, from one in-memory
/// profile that is never written to disk. Every save hands out a fresh object (so the steps, which compare profiles by
/// reference, rebuild), invalidates the flight router's cached profiles and lets the screen reload its own.
/// </summary>
public sealed class RadioProfiles
{
    readonly Services _services;
    readonly System.Action _saved;
    RadioProfile? _demo;

    public RadioProfiles(Services services, System.Action saved)
    {
        _services = services;
        _saved = saved;
    }

    /// <summary>Serves <paramref name="profile"/> for its device instead of the store, and never saves to disk again.</summary>
    public void UseDemo(RadioProfile profile)
    {
        _demo = profile;
        _saved();
    }

    public bool IsDemo => _demo is not null;

    /// <summary>A fresh copy of the device's profile, or null when it has none (not calibrated) or it is unreadable.</summary>
    public RadioProfile? Load(string guid) =>
        _demo is { } demo ? (guid == demo.DeviceGuid ? RadioProfile.FromJson(demo.ToJson()) : null) : _services.Radios.Load(guid, out _);

    public void Save(RadioProfile profile)
    {
        if (_demo is not null)
        {
            if (profile.DeviceGuid == _demo.DeviceGuid) _demo = profile;
        }
        else
        {
            _services.Radios.Save(profile);
            _services.Router.InvalidateProfiles();
        }
        _saved();
    }

    /// <summary>Loads the device's profile fresh, edits it and saves it; nothing happens when it has no profile.</summary>
    public void Edit(string guid, System.Action<RadioProfile> edit)
    {
        if (Load(guid) is not { } profile) return;
        edit(profile);
        Save(profile);
    }
}
