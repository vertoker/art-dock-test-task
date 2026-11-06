using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace ArtDock.Runtime
{
    public class AppLoop : IStartable
    {
        public void Start()
        {
            SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);
        }
    }
}
