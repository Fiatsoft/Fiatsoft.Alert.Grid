using Newtonsoft.Json;
using System.Configuration;

namespace Fiatsoft.Alert.Grid {
    public class Item {
        public Item() : this(false) { }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "To optionally-bypass gratuitous member initialization, with new(false)")]
        private Item(bool forOverload = false) {
            this.Data = [];
            this.Created = DateTime.Now;
        }

        public Item(Item item, List<string?> schema) : this(true) {
            Data = FilterBySchema(item.Data, schema);
            this.Action = item.Action;
            this.Created = item.Created;
            this.Hidden = item.Hidden;
        }

        public Item(Dictionary<string, string> data) : this(true) {
            Data = ItemExpandEnvironmentVariables ? data.ToDictionary(kv => kv.Key, kv => ProcessValue(kv.Value)) : data;
        }
        public Item(Dictionary<string,string> unordered, List<string?> schema) : this(true) {
            Data = FilterBySchema(unordered, schema);
        }

        static Dictionary<string, string> FilterBySchema(Dictionary<string, string> unordered, List<string?> schema) {
            if (schema != null && schema.Count > 0)
                return new Dictionary<string, string>(schema.ToDictionary(key => key??"null", key => unordered.TryGetValue(key??"null", out string? value) ? ProcessValue(value) : ""));
            else
                return unordered;
        }

        private static string ProcessValue(string value) {
            if (Item.ItemExpandEnvironmentVariables) {
                return Environment.ExpandEnvironmentVariables(value);
            }
            else 
                return value;
        }

        public Dictionary<string,string> Data { get; set; }
        [JsonIgnore]
        public string JSON { get { return Newtonsoft.Json.JsonConvert.SerializeObject(this); } }
        public string? Action { get; set; }
        public DateTime Created { get;  }
        public bool Hidden { get; set; }
        public static bool ItemExpandEnvironmentVariables { get; private set; } = Fiatsoft.Alert.Grid.Properties.Settings.Default.ItemExpandEnvironmentVariables;
        public static Item Empty { get; } = new Item();

    }
}
