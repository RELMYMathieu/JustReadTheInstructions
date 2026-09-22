using System.Collections.Generic;
using JustReadTheInstructions;
using Xunit;

namespace JRTI.Tests
{
    public class JRTICameraRuntimeTests
    {
        public JRTICameraRuntimeTests() => JRTICameraRuntime.Reset();

        [Fact]
        public void PreferredId_IsHonored_WhenFree()
        {
            Assert.Equal(5, JRTICameraRuntime.ResolveId(persistentId: 100, cameraIndex: 0, preferredId: 5));
        }

        [Fact]
        public void SameCamera_ReturnsCachedId_IgnoringNewPreferred()
        {
            int first = JRTICameraRuntime.ResolveId(100, 0, 1);
            int second = JRTICameraRuntime.ResolveId(100, 0, 9);
            Assert.Equal(first, second);
            Assert.Equal(1, second);
        }

        [Fact]
        public void CollidingPreferredId_BumpsToNextAvailable()
        {
            Assert.Equal(1, JRTICameraRuntime.ResolveId(100, 0, 1));
            Assert.Equal(2, JRTICameraRuntime.ResolveId(200, 0, 1));
        }

        [Fact]
        public void NonPositivePreferredId_FallsBackToLowestAvailable()
        {
            Assert.Equal(1, JRTICameraRuntime.ResolveId(100, 0, 0));
            Assert.Equal(2, JRTICameraRuntime.ResolveId(200, 0, -5));
        }

        [Fact]
        public void MultipleCamerasOnSamePart_GetDistinctIds()
        {
            int first = JRTICameraRuntime.ResolveId(100, 0, 0);
            int second = JRTICameraRuntime.ResolveId(100, 1, 0);
            int third = JRTICameraRuntime.ResolveId(100, 2, 0);

            Assert.Equal(new[] { 1, 2, 3 }, new[] { first, second, third });
        }

        [Fact]
        public void MultipleCamerasOnSamePart_OffsetPreferredIdByCameraIndex()
        {
            Assert.Equal(11, JRTICameraRuntime.ResolveId(100, 1, 10));
            Assert.Equal(10, JRTICameraRuntime.ResolveId(100, 0, 10));
            Assert.Equal(13, JRTICameraRuntime.ResolveId(100, 3, 10));
        }

        [Fact]
        public void NextAvailable_FillsLowestGap()
        {
            JRTICameraRuntime.ResolveId(100, 0, 1);
            JRTICameraRuntime.ResolveId(200, 0, 2);
            JRTICameraRuntime.ResolveId(300, 0, 3);

            JRTICameraRuntime.RetainOnly(new HashSet<(uint, int)> { (100, 0), (300, 0) });

            Assert.Equal(2, JRTICameraRuntime.ResolveId(400, 0, 0));
        }

        [Fact]
        public void RetainOnly_EvictsStaleIds_AndKeepsLiveOnes()
        {
            JRTICameraRuntime.ResolveId(100, 0, 1);
            JRTICameraRuntime.ResolveId(200, 0, 2);

            JRTICameraRuntime.RetainOnly(new HashSet<(uint, int)> { (100, 0) });

            Assert.Equal(1, JRTICameraRuntime.ResolveId(100, 0, 9));
            Assert.Equal(2, JRTICameraRuntime.ResolveId(200, 0, 2));
        }

        [Fact]
        public void RetainOnly_EvictsSingleCameraOnSharedPart()
        {
            JRTICameraRuntime.ResolveId(100, 0, 0);
            JRTICameraRuntime.ResolveId(100, 1, 0);

            JRTICameraRuntime.RetainOnly(new HashSet<(uint, int)> { (100, 1) });

            Assert.Equal(2, JRTICameraRuntime.ResolveId(100, 1, 0));
            Assert.Equal(1, JRTICameraRuntime.ResolveId(200, 0, 0));
        }

        [Fact]
        public void Reset_ClearsAllAssignments()
        {
            JRTICameraRuntime.ResolveId(100, 0, 1);
            JRTICameraRuntime.Reset();
            Assert.Equal(7, JRTICameraRuntime.ResolveId(100, 0, 7));
        }
    }
}
