using System.Text.Json.Serialization;
using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.Features.Preferences;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppPreferences))]
[JsonSerializable(typeof(PlayerShortcutBindings))]
internal sealed partial class PreferencesJsonContext : JsonSerializerContext;