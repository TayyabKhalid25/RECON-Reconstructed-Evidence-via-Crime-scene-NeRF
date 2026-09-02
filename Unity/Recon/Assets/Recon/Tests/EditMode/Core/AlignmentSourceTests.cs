using NUnit.Framework;
using Recon.Alignment;

namespace Recon.Tests.Core
{
    /// <summary>
    /// The fallback chain from docs/ANCHORING.md, as a rule rather than a comment. Which path
    /// placed the twin decides whether a measurement taken inside it may be quoted at all, so the
    /// mapping from path to "metric verified" is worth a test of its own.
    /// </summary>
    public class AlignmentSourceTests
    {
        [Test]
        public void OnlyTheMarkerIsMetricVerified()
        {
            // The marker gives pose and scale together against a physically measured 170 mm edge,
            // and is the only path the 2 cm accuracy claim belongs to. A cloud anchor resolve adds
            // its own unmeasured error on top (FTW-56 is that measurement) and manual placement is
            // a user's finger, so neither may be presented as verified.
            Assert.That(AlignmentSources.IsMetricVerified(AlignmentSource.Marker), Is.True);
            Assert.That(AlignmentSources.IsMetricVerified(AlignmentSource.CloudAnchor), Is.False);
            Assert.That(AlignmentSources.IsMetricVerified(AlignmentSource.Manual), Is.False);
            Assert.That(AlignmentSources.IsMetricVerified(AlignmentSource.None), Is.False);
        }

        [Test]
        public void PriorityFollowsTheFallbackTable()
        {
            // Marker beats anchor beats manual, so a re-detection can take over from a manual
            // placement but never the other way round.
            Assert.That(AlignmentSources.Priority(AlignmentSource.Marker),
                Is.GreaterThan(AlignmentSources.Priority(AlignmentSource.CloudAnchor)));
            Assert.That(AlignmentSources.Priority(AlignmentSource.CloudAnchor),
                Is.GreaterThan(AlignmentSources.Priority(AlignmentSource.Manual)));
            Assert.That(AlignmentSources.Priority(AlignmentSource.Manual),
                Is.GreaterThan(AlignmentSources.Priority(AlignmentSource.None)));
        }

        [Test]
        public void LabelIsWhatGoesOnScreen()
        {
            Assert.That(AlignmentSources.Label(AlignmentSource.Marker), Does.Contain("METRIC"));
            Assert.That(AlignmentSources.Label(AlignmentSource.Manual), Does.Contain("NOT METRIC VERIFIED"));
            Assert.That(AlignmentSources.Label(AlignmentSource.CloudAnchor), Does.Contain("NOT METRIC VERIFIED"));
            Assert.That(AlignmentSources.Label(AlignmentSource.None), Does.Contain("NOT ALIGNED"));
        }
    }
}
