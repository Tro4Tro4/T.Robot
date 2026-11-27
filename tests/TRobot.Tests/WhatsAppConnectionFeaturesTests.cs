using TRobot.Domain;

namespace TRobot.Tests;

public sealed class WhatsAppConnectionFeaturesTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewConnectionStartsWithAttendanceAndInformationOnly()
    {
        var connection = new WhatsAppConnection(Guid.NewGuid());

        Assert.True(connection.FeatureAttendance);
        Assert.True(connection.FeatureInformation);
        Assert.False(connection.FeatureOperations);
        Assert.False(connection.FeatureMealProposals);
        Assert.False(connection.FeatureMealPreferences);
        Assert.False(connection.FeatureFunReplies);
        Assert.Equal(WhatsAppFeatureSet.NewGroup, connection.Features);
        Assert.Equal(0, connection.FeaturesRevision);
        Assert.Equal(0, connection.Revision);
    }

    [Theory]
    [InlineData(WhatsAppFeature.Attendance)]
    [InlineData(WhatsAppFeature.Information)]
    [InlineData(WhatsAppFeature.Operations)]
    [InlineData(WhatsAppFeature.MealProposals)]
    [InlineData(WhatsAppFeature.MealPreferences)]
    [InlineData(WhatsAppFeature.FunReplies)]
    public void AllowsEachConfiguredFeatureIndependently(WhatsAppFeature feature)
    {
        var connection = new WhatsAppConnection(Guid.NewGuid());
        connection.UpdateFeatures(WhatsAppFeatureSet.All);

        Assert.True(connection.Allows(feature));
        Assert.False(connection.Allows((WhatsAppFeature)128));
        Assert.False(connection.Allows(WhatsAppFeature.None));
    }

    [Fact]
    public void UpdateFeaturesChangesOnlyTheFeatureRevision()
    {
        var connection = new WhatsAppConnection(Guid.NewGuid());
        connection.Activate("group@g.us", Guid.NewGuid(), Now);
        var connectionRevision = connection.Revision;

        var changed = connection.UpdateFeatures(WhatsAppFeatureSet.All);

        Assert.True(changed);
        Assert.Equal(connectionRevision, connection.Revision);
        Assert.Equal(1, connection.FeaturesRevision);
        Assert.True(connection.Allows(WhatsAppFeature.MealProposals));
        Assert.True(connection.Allows(WhatsAppFeature.MealPreferences));
        Assert.True(connection.Allows(WhatsAppFeature.FunReplies));
    }

    [Fact]
    public void SameFeaturesDoNotCreateAFeatureRevision()
    {
        var connection = new WhatsAppConnection(Guid.NewGuid());

        Assert.False(connection.UpdateFeatures(WhatsAppFeatureSet.NewGroup));
        Assert.Equal(0, connection.FeaturesRevision);
    }

    [Fact]
    public void InvalidFeatureBitsAreRejectedWithoutChangingState()
    {
        var connection = new WhatsAppConnection(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() =>
            connection.UpdateFeatures(new WhatsAppFeatureSet((WhatsAppFeature)128)));
        Assert.Equal(WhatsAppFeatureSet.NewGroup, connection.Features);
        Assert.Equal(0, connection.FeaturesRevision);
    }

    [Fact]
    public void ActivationAndDisconnectionPreserveFeatures()
    {
        var connection = new WhatsAppConnection(Guid.NewGuid());
        connection.UpdateFeatures(WhatsAppFeatureSet.All);
        connection.Activate("group@g.us", Guid.NewGuid(), Now);

        Assert.True(connection.Disable());
        Assert.Equal(WhatsAppFeatureSet.All, connection.Features);
        Assert.Equal(1, connection.FeaturesRevision);
        Assert.True(connection.Allows(WhatsAppFeature.Attendance));
    }
}
