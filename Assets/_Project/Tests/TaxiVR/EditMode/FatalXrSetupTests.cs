using NUnit.Framework;
using TaxiVR.Bootstrap;
using UnityEngine;

namespace TaxiVR.Tests.EditMode
{
    /// <summary>§10: en Android el arranque no puede caer a escritorio cuando OpenXR falla. En Windows si, porque
    /// alli el escritorio es un modo de trabajo legitimo.</summary>
    public sealed class FatalXrSetupTests
    {
        [Test]
        public void AndroidWithFailedXrIsFatalInsteadOfDesktop()
        {
            Assert.That(FatalXrSetupScreen.MustHalt(RuntimePlatform.Android, false, false), Is.True);
        }

        [Test]
        public void AndroidKeepsPlayingWhenXrInitializes()
        {
            Assert.That(FatalXrSetupScreen.MustHalt(RuntimePlatform.Android, false, true), Is.False);
        }

        [Test]
        public void AndroidHonoursAnExplicitDesktopRequest()
        {
            Assert.That(FatalXrSetupScreen.MustHalt(RuntimePlatform.Android, true, false), Is.False);
        }

        [Test]
        public void WindowsStillFallsBackToDesktopWhenXrFails()
        {
            Assert.That(FatalXrSetupScreen.MustHalt(RuntimePlatform.WindowsPlayer, false, false), Is.False);
        }
    }
}
