namespace ArtDock.Core
{
    /// <summary> Looks up game text by key and formats message templates </summary>
    public interface IGameText
    {
        public string this[string key] { get; }
        public string Format(string key, params object[] arguments);
    }
}
