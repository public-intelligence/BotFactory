using Microsoft.AspNetCore.Mvc.Testing;

namespace BotFactory.Tests.Infrastructure;

/// <summary>
/// Separate <see cref="WebApplicationFactory{TEntryPoint}"/> (and SQL container) for LiveKit turn tests that use
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/>.
/// Disposing those nested factories must not tear down the shared <see cref="BotFactoryWebApplicationFactory"/> used by the rest of the suite.
/// </summary>
public class LiveKitWebApplicationFactory : BotFactoryWebApplicationFactory;

[CollectionDefinition("LiveKitTurn")]
public class LiveKitTurnCollection : ICollectionFixture<LiveKitWebApplicationFactory>;
