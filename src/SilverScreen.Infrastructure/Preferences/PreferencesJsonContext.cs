using System.Text.Json.Serialization;
using SilverScreen.Core.Common;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Preferences;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppPreferences))]
[JsonSerializable(typeof(PlayerShortcutBindings))]
[JsonSerializable(typeof(EquatableArray<string>))]
internal sealed partial class PreferencesJsonContext : JsonSerializerContext;