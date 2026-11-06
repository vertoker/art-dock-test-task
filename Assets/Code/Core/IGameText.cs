namespace ArtDock.Core
{
    public interface IGameText
    {
        public string this[string key] { get; }
        public string Format(string key, params object[] arguments);
    }
}
