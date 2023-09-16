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
        private bool rebuildRequested = false;
        private string _exclusionSetting = "";
        private string _inclusionSetting = "";
        private List<string> exclusionFilters = new List<string>();
        private List<string> inclusionFilters = new List<string>();

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
                catch (Exception e)
                {
                    HttpResponseException httpException = null;
                    if (e is AggregateException && e.InnerException is HttpResponseException)
                        httpException = (HttpResponseException)e.InnerException;
                    else if (e is HttpResponseException)
                        httpException = (HttpResponseException)e;

                    if(httpException != null)
                    {
                        switch(httpException.StatusCode)
                        {
                            case 401:
                                badCredentials = true;
                                _logger.LogError($"Can't log in due to authentication error, please check your long-lived access token.");
                                return;
                            default:
                                _logger.LogError($"API Request Error. Code {httpException.StatusCode}.");
                                break;
                        }
                    }

                    throw;
                }

                loggedIn = true;
                _messageProcessor.UpdateState(new StateUpdate() { Id = "hassio_ready", Value = "1" });
            }
        }

        public void ClearStates()
        {
            foreach(var id in _dynamicStates.Keys)
            {
                _messageProcessor.RemoveState(new StateRemove { Id = id });
            }
            _dynamicStates.Clear();
            _currentStates.Clear();
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
            var supportedStates = convertedStates.Where(s => s.Domain != Domain.Unsupported && (inclusionFilters.Count()==0 || inclusionFilters.Any(i => s.EntityId.Contains(i))) && !exclusionFilters.Any(e => s.EntityId.Contains(e))).OrderBy(s => s.FriendlyName).ToList();

            if(rebuildRequested)
            {
                rebuildRequested = false;
                force = true;
                ClearStates();
            }

            bool newEntity = false;
            foreach (var state in supportedStates)
            {
                Monitor.Enter(_currentStates);
                try
                {
                    if (_currentStates.ContainsKey(state.EntityId))
                    {
                        if (force || _currentStates[state.EntityId].LastUpdated < state.LastUpdated)
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
                finally
                {
                    Monitor.Exit(_currentStates);
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

                    UpdateState($"{statePrefix}.entity_id", "Entity ID", state.EntityId, force, state.FriendlyName);
                    UpdateState($"{statePrefix}.domain", "Domain", state.Domain.GetDomainString(), force, state.FriendlyName);
                    UpdateState($"{statePrefix}.state", "State", state.State.ToString(), force, state.FriendlyName);

                    foreach (var (attribute, value) in state.OtherAttributes.OrderBy(a => a.Key).ToList())
                    {
                        if(value is List<string>)
                        {
                            int index = 0;
                            foreach (var item in value)
                            {
                                UpdateState($"{statePrefix}.attribute.{attribute}[{index}]", $"{attribute}[{index}]", Convert.ToString(item ?? ""), force, state.FriendlyName);
                                index++;
                            }
                        }
                        else
                            UpdateState($"{statePrefix}.attribute.{attribute}", attribute, Convert.ToString(value ?? ""), force, state.FriendlyName);
                    }
                    break;
            }
        }

        public void UpdateState(string id, string name, string value, bool force = false, string parentGroup = null)
        {
            if (!stopRequested)
            {
                if (_dynamicStates.ContainsKey(id))
                {
                    if (force || (_dynamicStates[id] != null && !_dynamicStates[id].Equals(value ?? "")))
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

                    _messageProcessor.CreateState(new StateCreate { Id = id, Desc = $"{parentGroup} {name}", DefaultValue = "", ParentGroup = parentGroup });
                    if (!string.IsNullOrWhiteSpace(value))
                        _messageProcessor.UpdateState(new StateUpdate { Id = id, Value = value });
                }
            }
        }

        public void UpdateFilters()
        {
            if (!string.IsNullOrWhiteSpace(_exclusionSetting))
            {
                exclusionFilters = _exclusionSetting.Split(",").Select(f => f.Trim()).ToList();
                exclusionFilters.RemoveAll(i => string.IsNullOrWhiteSpace(i));
            }
            else
                exclusionFilters.Clear();

            if (!string.IsNullOrWhiteSpace(_inclusionSetting))
            {
                inclusionFilters = _inclusionSetting.Split(",").Select(f => f.Trim()).ToList();
                inclusionFilters.RemoveAll(i => string.IsNullOrWhiteSpace(i));
            }
            else
                inclusionFilters.Clear();

            rebuildRequested = true;
            _ = ProcessStates(true);
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
                case "hassio_rebuild_states":
                    entityId = "";
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

            if (actionId.Equals("hassio_service"))
            {
                var hassio_service = data.Where(d => d.Id.Equals("hassio_service"))?.First()?.Value;
                var hassio_domain = data.Where(d => d.Id.Equals("hassio_domain"))?.First()?.Value;
                var hassio_data = data.Where(d => d.Id.Equals("hassio_data"))?.First()?.Value;

                if (!string.IsNullOrWhiteSpace(hassio_service) && !string.IsNullOrWhiteSpace(hassio_domain))
                    _hassioServices.CallService(hassio_domain, hassio_service, hassio_data);
                else if (!string.IsNullOrWhiteSpace(hassio_service))
                    _hassioServices.CallService(hassio_service, hassio_data);
            }
            else if (actionId.Equals("hassio_rebuild_states"))
            {
                rebuildRequested = true;
                _ = ProcessStates(true);
            }
            else if (_currentStates.ContainsKey(entityId))
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
                try
                {
                    _messageProcessor.UpdateState(new StateUpdate() { Id = "hassio_paired", Value = "1" });
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                    _logger.LogError(e.StackTrace);
                }
            };

            _messageProcessor.OnActionEvent += (actionId, dataList) =>
            {
                try
                {
                    HandleAction(actionId, ActionType.Press, dataList);
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                    _logger.LogError(e.StackTrace);
                }
            };

            _messageProcessor.OnHoldActionEvent += (actionId, held, dataList) =>
            {
                try
                {
                    HandleAction(actionId, held ? ActionType.Hold : ActionType.Release, dataList);
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                    _logger.LogError(e.StackTrace);
                }
            };

            _messageProcessor.OnListChangeEventHandler += (actionId, listId, instanceId, value) =>
            {
            };

            _messageProcessor.OnBroadcastEventHandler += (eventType, pageName) =>
            {
                try
                {
                    switch (eventType)
                    {
                        case "pageChange":
                            Console.WriteLine("HomeAssistant received pageChange broadcast");
                            ProcessStates(true).Wait();
                            break;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                    _logger.LogError(e.StackTrace);
                }
            };

            _messageProcessor.OnSettingEventHandler += (settings) =>
            {
                bool filtersUpdated = false;
                try
                {
                    foreach (var setting in settings)
                    {
                        foreach (var (key, value) in setting)
                        {
                            switch (key)
                            {
                                case "Home Assistant URL":
                                    _hassioUrl = ((string)value).Trim();
                                    break;
                                case "Home Assistant Access Token":
                                    _hassioKey = ((string)value).Trim();
                                    break;
                                case "Entity Exclusion Filter (comma separated)":
                                    string newExclusionFilter = ((string)value).Trim();
                                    if (!_exclusionSetting.Equals(newExclusionFilter))
                                    {
                                        _exclusionSetting = newExclusionFilter;
                                        filtersUpdated = true;
                                    }
                                    break;
                                case "Entity Inclusion Filter (comma separated)":
                                    string newInclusionFilter = ((string)value).Trim();
                                    if (!_inclusionSetting.Equals(newInclusionFilter))
                                    {
                                        _inclusionSetting = newInclusionFilter;
                                        filtersUpdated = true;
                                    }
                                    break;
                            }
                        }
                    }

                    if (filtersUpdated) UpdateFilters();
                    badCredentials = false;
                    loggedIn = false;
                    _messageProcessor.UpdateState(new StateUpdate() { Id = "hassio_ready", Value = "0" });
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                    _logger.LogError(e.StackTrace);
                }
            };

            _messageProcessor.OnCloseEventHandler += () => {
                try
                {
                    ClearStates();
                    stopRequested = true;
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                    _logger.LogError(e.StackTrace);
                }
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
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                _logger.LogError(e.StackTrace);
            }
            finally
            {
                _messageProcessor.UpdateState(new StateUpdate() { Id = "hassio_paired", Value = "0" });
                _hostApplicationLifetime.StopApplication();
            }
        }
    }
}
