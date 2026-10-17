using System.Text.Json.Serialization;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

// Legacy import also works in trimmed mobile release builds.
[JsonSerializable(typeof(Song))]
[JsonSerializable(typeof(List<MusicCollection>), TypeInfoPropertyName = "Collections")]
internal partial class LibraryJsonContext : JsonSerializerContext;
