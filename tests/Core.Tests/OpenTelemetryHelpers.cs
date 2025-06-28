using System.Diagnostics.Metrics;
using NSubstitute;

namespace YakShaveFx.OutboxKit.Core.Tests;

public static class OpenTelemetryHelpers
{
    public static IMeterFactory CreateMeterFactoryStub()
    {
        var factory = Substitute.For<IMeterFactory>();
        factory.Create(Arg.Any<MeterOptions>()).ReturnsForAnyArgs(m => new Meter(m.ArgAt<MeterOptions>(0)));
        return factory;
    }
}