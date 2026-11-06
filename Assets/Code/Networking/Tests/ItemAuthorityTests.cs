using System.Reflection;
using ArtDock.Networking;
using NUnit.Framework;
using UnityEngine;

namespace ArtDock.Tests
{
    public class ItemAuthorityTests
    {
        [Test]
        public void OwnerStateRequestsCannotMoveRotateOrResizeTheServerBody()
        {
            var root = new GameObject("Item Authority Test");
            try
            {
                root.transform.position = new Vector3(1, 2, 3);
                root.transform.rotation = Quaternion.Euler(10, 20, 30);
                var target = root.AddComponent<ItemNetworkTransform>();
                typeof(ItemNetworkTransform).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(target, null);
                Assert.That(target.OnClientRequestChange, Is.Not.Null);
                var accepted = target.OnClientRequestChange(
                    new Vector3(float.NaN, 1000, 0), Quaternion.identity, Vector3.one * 100);
                Assert.That(accepted.pos, Is.EqualTo(root.transform.position));
                Assert.That(accepted.rotOut, Is.EqualTo(root.transform.rotation));
                Assert.That(accepted.scale, Is.EqualTo(Vector3.one));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
