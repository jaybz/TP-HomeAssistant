using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TP_HomeAssistant.Extensions;

namespace TP_HomeAssistant.Models
{
    public class EntityState
    {
        private string _entityId;
        [JsonProperty("entity_id", Required = Required.Always)]
        public string EntityId
        {
            get
            {
                return _entityId;
            }
            set
            {
                _entityId = value;
                try
                {
                    Domain = value.GetDomainFromString();
                }
                catch (UnsupportedDomainException)
                {
                    Domain = Domain.Unsupported;
                }
            }
        }

        [JsonProperty("last_changed")]
        public DateTime LastChanged { get; set; }

        [JsonProperty("last_updated")]
        public DateTime LastUpdated { get; set; }

        public string State { get; set; } // default options: on/off, probably should be dynamic

        private Dictionary<string, dynamic> _allAttributes;
        private Dictionary<string, dynamic> _attributes;
        public Dictionary<string, dynamic> Attributes
        {
            get
            {
                return _allAttributes;
            }
            set
            {
                _allAttributes = new Dictionary<string, dynamic>(value);

                foreach(string key in value.Keys)
                {
                    try
                    {
                        switch (key)
                        {
                            case "friendly_name":
                                FriendlyName = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "device_class":
                                DeviceClass = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "last_triggered":
                                LastTriggered = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "effect_list":
                                LightEffects = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "supported_color_modes":
                                SupportedColorModes = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "fan_speed_list":
                                FanSpeedList = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "hvac_modes":
                                PresetModes = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "preset_modes":
                                PresetModes = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "fan_modes":
                                FanModes = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "swing_modes":
                                SwingModes = value[key] is JArray ? ((JArray)value[key]).ToObject<List<string>>() : value[key];
                                value.Remove(key);
                                break;
                            case "speed_count":
                                SpeedCount = (int)value[key];
                                value.Remove(key);
                                break;
                            case "minmireds":
                            case "min_mireds":
                                MinColorTemp = (int)value[key];
                                value.Remove(key);
                                break;
                            case "maxmireds":
                            case "max_mireds":
                                MaxColorTemp = (int)value[key];
                                value.Remove(key);
                                break;
                            case "supported_features":
                                SupportedFeatures = (long)value[key];
                                value.Remove(key);
                                break;
                            // tuples currently unsupported
                            case "hs_color":
                            case "xy_color":
                            case "rgb_color":
                            case "rgbw_color":
                            case "rgbww_color":
                                value.Remove(key);
                                break;
                        }
                    }
                    catch { } // ignore exceptions, there may be some if reported data type is different from expected
                }

                _attributes = value;
            }
        }

        [JsonIgnore]
        public long SupportedFeatures { get; private set; }

        [JsonIgnore]
        public int SpeedCount { get; private set; }

        [JsonIgnore]
        public int MinColorTemp { get; private set; }

        [JsonIgnore]
        public int MaxColorTemp { get; private set; }

        [JsonIgnore]
        public List<string> HvacModes { get; private set; }

        [JsonIgnore]
        public List<string> SwingModes { get; private set; }

        [JsonIgnore]
        public List<string> FanModes { get; private set; }

        [JsonIgnore]
        public List<string> PresetModes { get; private set; }

        [JsonIgnore]
        public List<string> FanSpeedList { get; private set; }

        [JsonIgnore]
        public List<string> LightEffects { get; private set; }

        [JsonIgnore]
        public List<string> SupportedColorModes { get; private set; }

        [JsonIgnore]
        public DateTime LastTriggered { get; private set; }

        [JsonIgnore]
        public string DeviceClass { get; private set; }

        [JsonIgnore]
        public Dictionary<string, dynamic> OtherAttributes
        {
            get
            {
                return _attributes;
            }
        }

        public Dictionary<string, dynamic> Context { get; set; }

        [JsonIgnore]
        public string FriendlyName { get; private set; }

        [JsonIgnore]
        public virtual Domain Domain { get; private set; }

        [JsonIgnore]
        public int Subscribed { get; set; } = 0;
    }
}
