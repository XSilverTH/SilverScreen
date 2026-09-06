using System.Text.Json.Serialization;
using SilverScreen.Core.Common;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Preferences;
/// <summary>
/// Statically generated <see cref="JsonSerializerContext"/> for application preferences.
/// </summary>
/// <remarks>
/// Explicitly registers <see cref="AppPreferences"/>, <see cref="PlayerShortcutBindings"/>, and
/// <see cref="EquatableArray{T}"/> collections of primitive/record types to ensure zero runtime reflection
/// or code generation during Native AOT publishing.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppPreferences))]
[JsonSerializable(typeof(PlayerShortcutBindings))]
[JsonSerializable(typeof(EquatableArray<string>))]
internal sealed partial class PreferencesJsonContext : JsonSerializerContext;