using System.Text.Json.Serialization;
using Tidy.Core.Catalog;
using Tidy.Core.Settings;

namespace Tidy.Core;

/// <summary>Source-generated JSON, so the app stays trim-safe and starts without reflection.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(CleanupCatalog))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Apps.AppCatalog))]
[JsonSerializable(typeof(Leftovers.KnownApps))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
