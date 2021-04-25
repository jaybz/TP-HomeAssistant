using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TouchPortalApi.Interfaces;
using TouchPortalApi.Models;
using HADotNet.Core;
using HADotNet.Core.Clients;
using TP_HomeAssistant.Models;
using TP_HomeAssistant.Extensions;
using HADotNet.Core.Domain;
using Newtonsoft.Json;
using System.Linq;
using System.Text.RegularExpressions;

namespace TP_HomeAssistant
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IMessageProcessor _messageProcessor;

        private readonly Dictionary<string, string> _dynamicStates = new Dictionary<string, string>();
        private readonly Dictionary<string, EntityState> _currentStates = new Dictionary<string, EntityState>();

        private string _hassioUrl;
        private string _hassioKey;
        private bool loggedIn = false;
        private bool badCredentials = false;
        private bool stopRequested = false;

        private EntityClient _hassioEntities;
        private StatesClient _hassioStates;
        private ServiceClient _hassioServices;

        public Worker(ILogger<Worker> logger, IHostApplicationLifetime hostApplicationLifetime, IMessageProcessor messageProcessor)
        {
            _logger = logger;
            _hostApplicationLifetime = hostApplicationLifetime;
            _messageProcessor = messageProcessor;
        }

        public async Task TryLogin()
        {
            if (!string.IsNullOrWhiteSpace(_hassioUrl) && !string.IsNullOrWhiteSpace(_hassioKey))
            {
                badCredentials = false;
                ClientFactory.Reset();
                ClientFactory.Initialize(_hassioUrl, _hassioKey);
                _hassioEntities = ClientFactory.GetClient<EntityClient>();
                _hassioStates = ClientFactory.GetClient<StatesClient>();
                _hassioServices = ClientFactory.GetClient<ServiceClient>();

                try
                {
                    var result = await ClientFactory.GetClient<ConfigClient>().GetConfiguration();
                }
                catch (AggregateException e)
                {
                    var inner = e.InnerException;
                    if (inner is HttpResponseException)
                    {
                        HttpResponseException exception = (HttpResponseException)inner;
                        if (exception.StatusCode == 401)
                        {
                            badCredentials = true;
                            return;
                        }
                    }
                }

                loggedIn = true;
                _messageProcessor.UpdateState(new StateUpdate() { Id = "hassio_ready", Value = "1" });
            }
        }

        public async Task ProcessStates(bool force = false)
        {
            string serializedState = "";
            try
            {
                var states = await _hassioStates.GetStates();
                serializedState = JsonConvert.SerializeObject(states);
            }
            catch
            {
                return; // not logged in?
            }
            var convertedStates = JsonConvert.DeserializeObject<List<EntityState>>(serializedState);
            var supportedStates = convertedStates.Where(s => s.Domain != Domain.Unsupported).ToList();

            bool newEntity = false;
            foreach(var state in supportedStates)
            {
                if(_currentStates.ContainsKey(state.EntityId))
                {
                    if (force || _currentStates[state.EntityId].LastChanged < state.LastChanged)
                    {
                        _currentStates[state.EntityId] = state;
                        ProcessState(state, force);
                    }
                }
                else
                {
                    _currentStates.Add(state.EntityId, state);
                    ProcessState(state, force);
                    newEntity = true;
                }
            }

            if(newEntity)
            {
                //hassio_entities_onoffstate
                var onoffStateEntities = _currentStates.Values.Where(e =>
                        new[] { 
                            Domain.Light,
                            Domain.Switch,
                            Domain.Fan,
                            Domain.Climate,
                            Domain.MediaPlayer,
                        }.Contains(e.Domain)
                    ).Select(e => 
                        $"{e.FriendlyName} ({e.EntityId})"
                    ).ToArray();
                _messageProcessor.UpdateChoice(new ChoiceUpdate { Id = "hassio_entities_onoffstate", Value = onoffStateEntities });

                var toggleEntities = _currentStates.Values.Where(e =>
                        new[] {
                            Domain.Light,
                            Domain.Switch,
                            Domain.Fan,
                            Domain.MediaPlayer,
                            Domain.Cover
                        }.Contains(e.Domain)
                    ).Select(e =>
                        $"{e.FriendlyName} ({e.EntityId})"
                    ).ToArray();
                _messageProcessor.UpdateChoice(new ChoiceUpdate { Id = "hassio_entities_togglestate", Value = toggleEntities });

                var automationEntities = _currentStates.Values.Where(e =>
                        e.Domain == Domain.Automation
                    ).Select(e =>
                        $"{e.FriendlyName} ({e.EntityId})"
                    ).ToArray();
                _messageProcessor.UpdateChoice(new ChoiceUpdate { Id = "hassio_automations", Value = automationEntities });

                var sceneEntities = _currentStates.Values.Where(e =>
                        e.Domain == Domain.Scene
                    ).Select(e =>
                        $"{e.FriendlyName} ({e.EntityId})"
                    ).ToArray();
                _messageProcessor.UpdateChoice(new ChoiceUpdate { Id = "hassio_scenes", Value = sceneEntities });
            }
        }

        public void ProcessState(EntityState state, bool force = false)
        {
            switch(state.Domain)
            {
                case Domain.Automation:
                case Domain.Scene:
                    break;

                case Domain.Unsupported:
                    break;

                default:
                    string statePrefix = $"hassio.{state.EntityId}";

                    UpdateState($"{statePrefix}.entity_id", $"{state.FriendlyName} Entity ID", state.EntityId, force);
                    UpdateState($"{statePrefix}.domain", $"{state.FriendlyName} Domain", state.Domain.GetDomainString(), force);
                    UpdateState($"{statePrefix}.state", $"{state.FriendlyName} State", state.State.ToString(), force);

                    foreach (var (attribute, value) in state.OtherAttributes.ToList())
                    {
                        UpdateState($"{statePrefix}.attribute.{attribute}", $"{state.FriendlyName} {attribute}", value.ToString(), force);
                    }
                    break;
            }
        }

        public void UpdateState(string id, string name, string value, bool force = false)
        {
            if (!stopRequested)
            {
                if (_dynamicStates.ContainsKey(id))
                {
                    if (force || !_dynamicStates[id].Equals(value))
                    {
                        _dynamicStates[id] = value;
                        if (force)
                            Console.WriteLine($"Force updating {id} to {value}");
                        _messageProcessor.UpdateState(new StateUpdate { Id = id, Value = value });
                    }
                }
                else
                {
                    _dynamicStates.Add(id, value);

                    _messageProcessor.CreateState(new StateCreate { Id = id, Desc = $"Home Assistant {name}", DefaultValue = "" });
                    if (!string.IsNullOrWhiteSpace(value))
                        _messageProcessor.UpdateState(new StateUpdate { Id = id, Value = value });
                }
            }
        }

        private void HandleAction(string actionId, ActionType actionType, List<ActionData> data)
        {
            string entityId = "";
            switch (actionId)
            {
                case "hassio_poweronoff":
                    entityId = data.Where(d => d.Id.Equals("hassio_entities_onoffstate"))?.First()?.Value;
                    break;
                case "hassio_powertoggle":
                    entityId = data.Where(d => d.Id.Equals("hassio_entities_togglestate"))?.First()?.Value;
                    break;
                case "hassio_scene":
                    entityId = data.Where(d => d.Id.Equals("hassio_scenes"))?.First()?.Value;
                    break;
                case "hassio_automation":
                    entityId = data.Where(d => d.Id.Equals("hassio_automations"))?.First()?.Value;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(entityId))
            {
                Regex entityIdRegex = new Regex(@"\((.+?)\)$");
                Match entityIdMatch = entityIdRegex.Match(entityId);
                if (entityIdMatch.Success && entityIdMatch.Groups.Count > 1)
                {
                    entityId = entityIdMatch.Groups[1].Value;
                }
            }

            if (_currentStates.ContainsKey(entityId))
            {
                Domain domain = _currentStates[entityId].Domain;
                switch (actionId)
                {
                    case "hassio_poweronoff":
                        var state = data.Where(d => d.Id.Equals("hassio_state"))?.First()?.Value;
                        string service = (state.Equals("On") ^ actionType == ActionType.Release) ? "turn_on" : "turn_off";
                        Console.WriteLine($"HomeAssistant action {actionId}: {entityId}={state}");
                        _hassioServices.CallService(domain.GetDomainString(), service, new { entity_id = entityId });
                        Console.WriteLine($"HomeAssistant /api/services/{domain.GetDomainString()}/{service}");
                        break;
                    case "hassio_powertoggle":
                        _hassioServices.CallService(domain.GetDomainString(), "toggle", new { entity_id = entityId });
                        break;
                    case "hassio_service":
                        var hassio_service = data.Where(d => d.Id.Equals("hassio_service"))?.First()?.Value;
                        var hassio_domain = data.Where(d => d.Id.Equals("hassio_domain"))?.First()?.Value;
                        var hassio_data = data.Where(d => d.Id.Equals("hassio_data"))?.First()?.Value;

                        if (!string.IsNullOrWhiteSpace(hassio_service) && !string.IsNullOrWhiteSpace(hassio_domain))
                            _hassioServices.CallService(hassio_domain, hassio_service, hassio_data);
                        break;
                    case "hassio_scene":
                        _hassioServices.CallService("scene", "turn_on", new { entity_id = entityId });
                        break;
                    case "hassio_automation":
                        _hassioServices.CallService("automation", "trigger", new { entity_id = entityId });
                        break;
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _messageProcessor.OnConnectEventHandler += () =>
            {
                _messageProcessor.UpdateState(new StateUpdate() { Id = "hassio_paired", Value = "1" });
            };

            _messageProcessor.OnActionEvent += (actionId, dataList) =>
            {
                HandleAction(actionId, ActionType.Press, dataList);
            };

            _messageProcessor.OnHoldActionEvent += (actionId, held, dataList) =>
            {
                HandleAction(actionId, held ? ActionType.Hold : ActionType.Release, dataList);
            };

            _messageProcessor.OnListChangeEventHandler += (actionId, listId, instanceId, value) =>
            {
            };

            _messageProcessor.OnBroadcastEventHandler += (eventType, pageName) =>
            {
                switch(eventType)
                {
                    case "pageChange":
                        Console.WriteLine("HomeAssistant received pageChange broadcast");
                        ProcessStates(true).Wait();
                        break;
                }
            };

            _messageProcessor.OnSettingEventHandler += (settings) =>
            {
                foreach (var setting in settings)
                {
                    foreach (var (key, value) in setting)
                    {
                        switch (key)
                        {
                            case "Home Assistant URL":
                                _hassioUrl = value;
                                break;
                            case "Home Assistant Access Token":
                                _hassioKey = value;
                                break;
                        }
                    }
                }

                badCredentials = false;
                loggedIn = false;
                _messageProcessor.UpdateState(new StateUpdate() { Id = "hassio_ready", Value = "0" });
            };

            _messageProcessor.OnCloseEventHandler += () => {
                foreach (string state in _dynamicStates.Keys)
                {
                    _messageProcessor.RemoveState(new StateRemove { Id = state });
                }
                _dynamicStates.Clear();
                stopRequested = true;
            };

            _messageProcessor.OnExitHandler += () =>
            {
                stopRequested = true;
            };

            // Run Listen and pairing
            _ = Task.WhenAll(new Task[] {
                    _messageProcessor.Listen(),
                    _messageProcessor.TryPairAsync()
                });

            try
            {
                while (!stoppingToken.IsCancellationRequested && !stopRequested)
                {
                    if (!loggedIn && !badCredentials)
                        await TryLogin();

                    if (loggedIn)
                        await ProcessStates();

                    await Task.Delay(500, stoppingToken);
                }
            }
            finally
            {
                _messageProcessor.UpdateState(new StateUpdate() { Id = "hassio_paired", Value = "0" });
                _hostApplicationLifetime.StopApplication();
            }
        }
    }
}
