using TP_HomeAssistant.Extensions;

namespace TP_HomeAssistant.Models
{
    // some of these will be forced read-only for the plugin
    public enum Domain
    {
        [DomainString("")]
        Unsupported,

        // entities used for automation, no states, triggerable from plugin
        Automation,
        Scene,

        // read-only: information only entities, no control from plugin
        [DomainString("binary_sensor")]
        BinarySensor,
        Sensor,
        Weather,

        // read/write: entities that can be controlled from plugin
        Light,
        Switch,
        [DomainString("media_player")] // not supporting media players for now
        MediaPlayer,

        // untested: no test devices available
        Vacuum, // domain not advertised in REST API
        Fan,
        Climate,
        Lock,
        Cover
    }
}
