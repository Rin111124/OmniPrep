using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Infrastructure.Services;
using Xunit;

namespace OmniPrep.Tests;

public class SubjectStrategyFactoryTests
{
    private readonly SubjectStrategyFactory _factory;

    public SubjectStrategyFactoryTests()
    {
        var strategies = new List<ISubjectStrategy>
        {
            new GeographyStrategy(),
            new GdktplStrategy()
        };
        _factory = new SubjectStrategyFactory(strategies);
    }

    [Fact]
    public void GetStrategy_Geo_ReturnsGeographyStrategy()
    {
        var strategy = _factory.GetStrategy(SubjectCode.GEO);
        Assert.NotNull(strategy);
        Assert.Equal(SubjectCode.GEO, strategy.SubjectCode);
        Assert.IsType<GeographyStrategy>(strategy);
    }

    [Fact]
    public void GetStrategy_Gdktpl_ReturnsGdktplStrategy()
    {
        var strategy = _factory.GetStrategy(SubjectCode.GDKTPL);
        Assert.NotNull(strategy);
        Assert.Equal(SubjectCode.GDKTPL, strategy.SubjectCode);
        Assert.IsType<GdktplStrategy>(strategy);
    }

    [Fact]
    public void GetStrategy_UnsupportedSubject_ThrowsNotSupportedException()
    {
        var ex = Assert.Throws<NotSupportedException>(() => _factory.GetStrategy(SubjectCode.MATH));
        Assert.Contains("Strategy cho môn", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
