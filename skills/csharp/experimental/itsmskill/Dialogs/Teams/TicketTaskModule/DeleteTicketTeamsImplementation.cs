﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace ITSMSkill.Dialogs.Teams.TicketTaskModule
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using AdaptiveCards;
    using ITSMSkill.Dialogs.Teams.CreateTicketTaskModuleView;
    using ITSMSkill.Extensions;
    using ITSMSkill.Extensions.Teams;
    using ITSMSkill.Extensions.Teams.TaskModule;
    using ITSMSkill.Models;
    using ITSMSkill.Models.UpdateActivity;
    using ITSMSkill.Services;
    using ITSMSkill.TeamsChannels;
    using ITSMSkill.TeamsChannels.Invoke;
    using Microsoft.Bot.Builder;
    using Microsoft.Bot.Connector;
    using Microsoft.Bot.Schema;
    using Microsoft.Bot.Schema.Teams;
    using Microsoft.Extensions.DependencyInjection;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// DeleteTicketTeamsImplementation for Deleting a Ticket Handles OnFetch and OnSumbit Activity for TaskModules
    /// </summary>
    [TeamsInvoke(FlowType = nameof(TeamsFlowType.DeleteTicket_Form))]
    public class DeleteTicketTeamsImplementation : ITeamsTaskModuleHandler<TaskModuleResponse>
    {
        private readonly IStatePropertyAccessor<SkillState> _stateAccessor;
        private readonly ConversationState _conversationState;
        private readonly BotSettings _settings;
        private readonly BotServices _services;
        private readonly IServiceManager _serviceManager;
        private readonly IStatePropertyAccessor<ActivityReferenceMap> _activityReferenceMapAccessor;
        private readonly IConnectorClient _connectorClient;
        private readonly ITeamsActivity<AdaptiveCard> _teamsTicketUpdateActivity;

        public DeleteTicketTeamsImplementation(IServiceProvider serviceProvider)
        {
            _conversationState = serviceProvider.GetService<ConversationState>();
            _settings = serviceProvider.GetService<BotSettings>();
            _services = serviceProvider.GetService<BotServices>();
            _stateAccessor = _conversationState.CreateProperty<SkillState>(nameof(SkillState));
            _serviceManager = serviceProvider.GetService<IServiceManager>();
            _activityReferenceMapAccessor = _conversationState.CreateProperty<ActivityReferenceMap>(nameof(ActivityReferenceMap));
            _connectorClient = serviceProvider.GetService<IConnectorClient>();
            _teamsTicketUpdateActivity = serviceProvider.GetService<ITeamsActivity<AdaptiveCard>>();
        }

        public async Task<TaskModuleResponse> OnTeamsTaskModuleFetchAsync(ITurnContext context, CancellationToken cancellationToken)
        {
            var taskModuleMetadata = context.Activity.GetTaskModuleMetadata<TaskModuleMetadata>();

            var ticketDetails = taskModuleMetadata.FlowData != null ?
                    JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(taskModuleMetadata.FlowData))
                    .TryGetValue("IncidentDetails", out var ticket) ? (Ticket)ticket : null
                    : null;

            return new TaskModuleResponse()
            {
                Task = new TaskModuleContinueResponse()
                {
                    Value = new TaskModuleTaskInfo()
                    {
                        Title = "DeleteTicket",
                        Height = "medium",
                        Width = 500,
                        Card = new Attachment
                        {
                            ContentType = AdaptiveCard.ContentType,
                            Content = TicketDialogHelper.GetDeleteConfirmationCard(ticketDetails)
                        }
                    }
                }
            };
        }

        public async Task<TaskModuleResponse> OnTeamsTaskModuleSubmitAsync(ITurnContext context, CancellationToken cancellationToken)
        {
            var state = await _stateAccessor.GetAsync(context, () => new SkillState());
            var taskModuleMetadata = context.Activity.GetTaskModuleMetadata<TaskModuleMetadata>();

            var ticketDetails = taskModuleMetadata.FlowData != null ?
                    JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(taskModuleMetadata.FlowData))
                    .TryGetValue("IncidentDetails", out var ticket) ? (Ticket)ticket : null
                    : null;
            if (ticketDetails != null)
            {
                string ticketCloseReason = string.Empty;

                // Get User Input from AdatptiveCard
                var activityValueObject = JObject.FromObject(context.Activity.Value);

                var isDataObject = activityValueObject.TryGetValue("data", StringComparison.InvariantCultureIgnoreCase, out JToken dataValue);
                JObject dataObject = null;
                if (isDataObject)
                {
                    // Get TicketCloseReason
                    ticketCloseReason = dataObject.GetValue("IncidentCloseReason").Value<string>();
                }

                // Create Managemenet object
                var management = _serviceManager.CreateManagement(_settings, state.AccessTokenResponse, state.ServiceCache);

                // Create Ticket
                var result = await management.CloseTicket(ticketDetails.Id, ticketCloseReason);

                // TODO: Figure out what should we update the incident with in order
                ActivityReferenceMap activityReferenceMap = await _activityReferenceMapAccessor.GetAsync(
                    context,
                    () => new ActivityReferenceMap(),
                    cancellationToken)
                .ConfigureAwait(false);
                activityReferenceMap.TryGetValue(context.Activity.Conversation.Id, out var activityReference);

                await _teamsTicketUpdateActivity.UpdateTaskModuleActivityAsync(
                    context,
                    activityReference,
                    RenderCreateIncidentHelper.CloseTicketCard(result.Tickets[0]),
                    cancellationToken);

                // Return Closed Incident Envelope
                return new TaskModuleResponse()
                {
                    Task = new TaskModuleContinueResponse()
                    {
                        Type = "continue",
                        Value = new TaskModuleTaskInfo()
                        {
                            Title = "Incident Deleted",
                            Height = "small",
                            Width = 300,
                            Card = new Attachment
                            {
                                ContentType = AdaptiveCard.ContentType,
                                Content = RenderCreateIncidentHelper.ImpactTrackerResponseCard("Incident has been Deleted")
                            }
                        }
                    }
                };
            }

            // Failed to Delete Incident
            return new TaskModuleResponse
            {
                Task = new TaskModuleContinueResponse()
                {
                    Type = "continue",
                    Value = new TaskModuleTaskInfo()
                    {
                        Title = "Incident Delete Failed",
                        Height = "medium",
                        Width = 500,
                        Card = new Attachment
                        {
                            ContentType = AdaptiveCard.ContentType,
                            Content = RenderCreateIncidentHelper.ImpactTrackerResponseCard("Incident Delete Failed")
                        }
                    }
                }
            };
        }
    }
}