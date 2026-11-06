using ArtDock.Networking;
using NUnit.Framework;
using UnityEngine;

namespace ArtDock.Tests
{
    public class ItemPresentationTests
    {
        [Test]
        public void ReleasedVisualOffsetCannotMoveTheCubeThroughAWall()
        {
            var item = new GameObject("Item Visual Test");
            var wall = new GameObject("Wall Test");
            try
            {
                var body = item.AddComponent<Rigidbody>();
                var box = item.AddComponent<BoxCollider>();
                box.size = Vector3.one * 0.6f;
                wall.transform.position = new Vector3(0.5f, 0, 0);
                var wallCollider = wall.AddComponent<BoxCollider>();
                wallCollider.size = new Vector3(0.1f, 4, 4);
                Physics.SyncTransforms();
                var physics = new ItemPhysics(body, box, new GameSettings.ItemConfiguration(), true);
                var position = physics.ConstrainPresentation(Vector3.right * 0.3f, null);
                Assert.That(Physics.ComputePenetration(box, position, Quaternion.identity,
                    wallCollider, wall.transform.position, Quaternion.identity, out _, out _), Is.False);
                Assert.That(position.x, Is.LessThan(0.15f));
            }
            finally
            {
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(wall);
            }
        }
    }
}
