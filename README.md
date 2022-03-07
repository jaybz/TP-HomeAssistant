# TP-HomeAssistant
This is a [Home Assistant](https://www.home-assistant.io/) plugin for [Touch Portal](https://www.touch-portal.com/). I wrote this plugin as I initially had Tuya devices (via OpenAPI) but am now gradually migrating to a different platform as I've had issues dealing with Tuya's API. Due to the complexity of setting up and getting the necessary keys for Tuya's OpenAPI, I won't be releasing that plugin. Instead, you can set up [Home Assistant](https://www.home-assistant.io/) and configure its Tuya integration or use [IFTTT](https://ifttt.com/home) to control your Tuya devices.

## Important Note
If you are upgrading from 0.9.4 or earlier to 0.9.5 or later, you may run into issues after the upgrade. If you do, first try deleting the plugin, importing the plugin again, then restart Touch Portal. That should fix any upgrade related issues. Your plugin settings should also be retained.

## Device Support
Most types of devices supported by [Home Assistant](https://www.home-assistant.io/) should be supported by this plugin. That said, I do not have all the different device types available for testing at the moment. Also, support for most attributes, states, and services are very basic at the moment.

### Robot Vacuums
This plugin can support various robot vacuums and other devices within the vacuum domain. Unfortunately, support for the vacuum domain on [Home Assistant](https://www.home-assistant.io/)'s REST API might not be present. I do not currently have one of these robot vacuums to be able to check.

## Configuration
There are two things you need to enter into Touch Portal's plugin settings for [Home Assistant](https://www.home-assistant.io/):
- Home Assistant URL - This can be your local/private URL or your public URL. eg: http://homeassistant.local:8123/
- Home Assistant Access Key - You need to create a long-lived access token by logging into your [Home Assistant](https://www.home-assistant.io/) server's web interface and going to your profile (eg: http://homeassistant.local:8123/profile). Scroll down to the Long-Lived Access Tokens section and click on the Create Token button.
- Entity Exclusion Filter - Comma separated list of values to filter out. Any entities whose Entity ID (including domain) contains one of the values in this list will not have states reported to Touch Portal. Whole domains can be excluded using this setting but keep in mind that domains like sensor, for example, can be part of Entity IDs in different domains as well. A restart is not needed for the filter setting to take effect, however, if the filter is not being applied, try restarting first.

You can find the appropriate location to place these by going to Touch Portal -> Settings -> Plugins. Then select the [Home Assistant](https://www.home-assistant.io/) Plugin from the drop down list.

## State sorting
As of version 0.9.8, states are now sorted using the corresponding entity's name. States for the individual attributes are also sorted except for the Entity ID, Domain, and State, which always comes first for a given entity. This sorting is done by changing the order at which the plugin sends the state creation command to [Touch Portal](https://www.touch-portal.com/).

## Available actions
- Set power state - Let's you call the turn_on or turn_off service for a particular [Home Assistant](https://www.home-assistant.io/) entity. Support for other states, if and when implemented later, will be through a different action.
- Toggle state - Let's you call the toggle service. Note that not all devices that support turn_on and turn_off support toggle and vice versa.
- Call Home Assistant Service - Other services are supported through this. You should be able to include the Entity ID state for an entity as part of the data parameter. Note that the Data field must contain valid JSON.
- Trigger Automation
- Apply Scene
- Rebuild Home Assistant States - This clears all states from the plugin and [Touch Portal](https://www.touch-portal.com/) and then re-creates them. This will let you remove states whose corresponding entities no longer exist in your [Home Assistant](https://www.home-assistant.io/) instance without restarting [Touch Portal](https://www.touch-portal.com/) or the plugin. This will also re-apply sorting of states as a side effect.

### On Hold actions
The following actions also support On Hold functionality:
- Set power state - Holding the button will perform the action, releasing performs the action but with the opposite state.
- Toggle state - Holding the button will perform the action, releasing repeats the action.

Note: If you set a button up to have both On Press and On Hold actions, those actions will run in this order: On Hold button action, released action, regular On Press action(s).

## Known issues
- When an entity id disappears from [Home Assistant](https://www.home-assistant.io/) (either because the entity is no longer present, or if the entity id is changed) this entity (and it's id) will continue to be visible in [Touch Portal](https://www.touch-portal.com/) until the plugin (or [Touch Portal](https://www.touch-portal.com/) itself) is restarted.
- Plugin states/variables for list [Home Assistant](https://www.home-assistant.io/) attributes are not currently supported. List values might later be supported via drop-down lists for presets and other similar settings. Tuples, however, are now supported.
- In some menus in Touch Portal, states lose their first underscore. This is a [Touch Portal](https://www.touch-portal.com/) issue and isn't something that can be reasonably dealt with on the plugin. This is also purely cosmetic.
- State sorting will not always apply immediately for entities added to [Home Assistant](https://www.home-assistant.io/) while the plugin is running. To apply sorting to newly added entites, you can either run the Rebuild Home Assistant States action, or restart the plugin or [Touch Portal](https://www.touch-portal.com/).

## Credits
- The integration into Touch Portal uses [TouchPortalAPI](https://github.com/tlewis17/TouchPortalAPI)
- Home Assistant calls are made via [HADotNet](https://github.com/qJake/HADotNet/).
- This plugin uses [Json.NET](https://github.com/JamesNK/Newtonsoft.Json) for JSON serialization/deserialization.
- Thanks to [Touch Portal](https://www.touch-portal.com/), without it, you won't be able to use this plugin.
- Thanks to [Home Assistant](https://www.home-assistant.io/), because this plugin will not do anything without it.

## Contributing
If you wish to contribute, simply fork this repository and submit your changes via pull request targeting the develop branch or a feature branch. Please do not target the release branch in your pull requests.

### TouchPortalAPI
I'm currently using my own fork of TouchPortalAPI to allow adding support for API features to this plugin that have not yet been merged back into upstream. Any changes applied to my fork will eventually make it back to the upstream as a pull request and I will not be building any releases of the fork other than as part of my own projects. I will only be entertaining issues and pull requests posted at my fork's Github if they involve any plugin(s) that I am using the fork with, including this one. I make no guarantees, however, that changes made to the upstream repo will make it to my fork.
