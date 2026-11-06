using System;
using System.Collections.Generic;
using System.Globalization;
using ArtDock.Core;
using UnityEngine;

namespace ArtDock.Runtime
{
    /// <summary> Stores editable game text and substitutes the game name into templates </summary>
    [CreateAssetMenu(menuName = "Game/Text settings", fileName = "GameText")]
    public class GameTextSettings : ScriptableObject, IGameText
    {
        /// <summary> A text key and value editable in the inspector </summary>
        [Serializable]
        private struct Entry
        {
            public string Key;
            [TextArea] public string Value;
        }

        [SerializeField] private Entry[] _entries;
        private Dictionary<string, string> values;

        public string this[string key]
        {
            get
            {
                if (key == TextKeys.GameTitle) return GameConstants.GameName;
                if (values == null) BuildLookup();

                return values[key].Replace("{GameName}", GameConstants.GameName);
            }
        }

        public string Format(string key, params object[] arguments)
            => string.Format(CultureInfo.InvariantCulture, this[key], arguments);

        private void BuildLookup()
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in _entries) values.Add(entry.Key, entry.Value);
        }

        private void OnEnable() => values = null;
        private void OnValidate() => values = null;
    }
}
