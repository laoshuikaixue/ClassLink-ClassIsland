using System.Security.Cryptography;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ClassLink.Components;
using ClassLink.Models;
using ClassLink.Services;
using ClassLink.Settings;

namespace ClassLink;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
  public ClassLinkSettings Settings { get; private set; } = new();

  public override void Initialize(HostBuilderContext context, IServiceCollection services)
  {
    var settingsPath = Path.Combine(PluginConfigFolder, "Settings.json");
    Settings = ConfigureFileHelper.LoadConfig<ClassLinkSettings>(settingsPath);
    if (string.IsNullOrWhiteSpace(Settings.Token))
    {
      Settings.Token = GenerateToken();
      ConfigureFileHelper.SaveConfig(settingsPath, Settings);
    }
    Settings.PropertyChanged += (_, _) => ConfigureFileHelper.SaveConfig(settingsPath, Settings);

    services.AddSingleton(this);
    services.AddSingleton(Settings);
    services.AddSingleton<ClassLinkStateService>();
    services.AddSingleton<ClassLinkServer>();
    services.AddHostedService(provider => provider.GetRequiredService<ClassLinkServer>());
    services.AddComponent<ClassLinkLyricsComponent, ClassLinkLyricsComponentSettings>();
    services.AddComponent<ClassLinkNowPlayingComponent, ClassLinkNowPlayingComponentSettings>();
    services.AddSettingsPage<ClassLinkSettingsPage>();
  }

  public void RegenerateToken() => Settings.Token = GenerateToken();

  private static string GenerateToken() =>
      Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
