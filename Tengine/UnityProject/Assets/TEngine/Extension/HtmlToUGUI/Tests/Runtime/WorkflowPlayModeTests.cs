using NUnit.Framework;
using UnityEngine;

namespace HtmlToUGUI.Tests
{
    /// <summary>Runtime smoke: the shared config asset contract used by the bake pipeline.</summary>
    public class WorkflowPlayModeTests
    {
        [Test]
        public void Config_Defaults_ExposeThreeResolutions()
        {
            var config = ScriptableObject.CreateInstance<HtmlToUGUIConfig>();
            Assert.AreEqual(3, config.supportedResolutions.Count);
            Assert.AreEqual(new Vector2(1920, 1080), config.supportedResolutions[0].resolution);
            Object.DestroyImmediate(config);
        }
    }
}
