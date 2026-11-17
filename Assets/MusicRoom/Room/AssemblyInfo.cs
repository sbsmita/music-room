// Test hooks (SnowWeather.Settle and friends) are internal: only the test assemblies see them.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MusicRoom.Tests.EditMode")]
[assembly: InternalsVisibleTo("MusicRoom.Tests.PlayMode")]
